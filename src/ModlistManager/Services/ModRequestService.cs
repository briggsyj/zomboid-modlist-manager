using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ModlistManager.Data;
using ModlistManager.Data.Entities;

namespace ModlistManager.Services;

public partial class ModRequestService(IDbContextFactory<AppDbContext> dbContextFactory, SteamCmdFetchQueue fetchQueue)
{
    public record CreateResult(bool Success, string? Error, int? RequestId);

    public async Task<CreateResult> CreateRequestAsync(string? title, string? workshopInput, string? requesterName, string game = Mod.DefaultGame)
    {
        title = title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return new CreateResult(false, "Title is required.", null);
        }

        var workshopId = WorkshopIdParser.TryParse(workshopInput);
        if (workshopId is null)
        {
            return new CreateResult(false, "Enter a valid Steam Workshop URL or numeric item ID.", null);
        }

        var normalizedName = NormalizeName(requesterName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return new CreateResult(false, "Your name is required.", null);
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();

        var mod = await db.Mods.FirstOrDefaultAsync(m => m.Game == game && m.WorkshopId == workshopId);
        var isNewMod = mod is null;
        if (mod is null)
        {
            mod = new Mod { Game = game, WorkshopId = workshopId, FetchStatus = ModIdFetchStatus.Queued };
            db.Mods.Add(mod);
        }

        var request = new ModRequest
        {
            Title = title,
            Mod = mod,
            RequesterName = normalizedName,
            Status = RequestStatus.Pending
        };
        db.ModRequests.Add(request);

        await db.SaveChangesAsync();

        if (isNewMod)
        {
            fetchQueue.Enqueue(mod.Id);
        }

        return new CreateResult(true, null, request.Id);
    }

    /// <summary>Lowercases and collapses whitespace so near-duplicate requester names converge.</summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return CollapseWhitespaceRegex().Replace(name.Trim(), " ").ToLowerInvariant();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespaceRegex();

    public async Task<List<string>> GetDistinctRequesterNamesAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.ModRequests
            .Select(r => r.RequesterName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();
    }

    public async Task<List<string>> GetDistinctGamesAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Mods
            .Select(m => m.Game)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    public async Task<List<ModRequest>> GetRequestsAsync(RequestStatus? status = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var query = db.ModRequests
            .Include(r => r.Mod!).ThenInclude(m => m.PzModIds)
            .OrderByDescending(r => r.CreatedAtUtc)
            .AsQueryable();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        return await query.ToListAsync();
    }

    /// <summary>The current modlist: every Mod with at least one approved request, optionally filtered by game.</summary>
    public async Task<List<Mod>> GetModlistAsync(string? game = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var query = db.Mods
            .Include(m => m.PzModIds)
            .Include(m => m.Requests)
            .Where(m => m.IsInModlist)
            .AsQueryable();
        if (game is not null)
        {
            query = query.Where(m => m.Game == game);
        }

        return await query.OrderBy(m => m.Id).ToListAsync();
    }

    public async Task SetStatusAsync(int requestId, RequestStatus status)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var request = await db.ModRequests.Include(r => r.Mod).FirstOrDefaultAsync(r => r.Id == requestId);
        if (request?.Mod is null)
        {
            return;
        }

        request.Status = status;
        request.DecidedAtUtc = DateTime.UtcNow;

        if (status == RequestStatus.Approved)
        {
            request.Mod.IsInModlist = true;
            request.Mod.AddedToModlistAtUtc ??= DateTime.UtcNow;
        }
        else
        {
            var stillApprovedElsewhere = await db.ModRequests.AnyAsync(r =>
                r.Id != requestId && r.ModId == request.ModId && r.Status == RequestStatus.Approved);
            if (!stillApprovedElsewhere)
            {
                request.Mod.IsInModlist = false;
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task RetryFetchAsync(int modId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == modId);
        if (mod is null)
        {
            return;
        }

        mod.FetchStatus = ModIdFetchStatus.Queued;
        mod.FetchLog = null;
        await db.SaveChangesAsync();

        fetchQueue.Enqueue(modId);
    }

    public async Task AddManualModIdAsync(int modId, string modIdValue, string? modName)
    {
        modIdValue = modIdValue.Trim();
        if (modIdValue.Length == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.PzModIds.Add(new PzModId
        {
            ModId = modId,
            Value = modIdValue,
            Name = string.IsNullOrWhiteSpace(modName) ? null : modName.Trim(),
            IsManual = true
        });
        await db.SaveChangesAsync();
    }

    public async Task DeletePzModIdAsync(int pzModIdId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var entry = await db.PzModIds.FirstOrDefaultAsync(m => m.Id == pzModIdId);
        if (entry is null)
        {
            return;
        }

        db.PzModIds.Remove(entry);
        await db.SaveChangesAsync();
    }

    /// <summary>Semicolon-delimited Steam Workshop item IDs for every mod currently on a modlist, regardless of game.</summary>
    public async Task<string> BuildApprovedWorkshopIdExportAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ids = await db.Mods
            .Where(m => m.IsInModlist)
            .OrderBy(m => m.Id)
            .Select(m => m.WorkshopId)
            .ToListAsync();
        return string.Join(';', ids);
    }

    /// <summary>
    /// Semicolon-delimited Project Zomboid Mod IDs for mods currently on the Project Zomboid modlist,
    /// each prefixed with a backslash (matching the Mods= line format PZ server configs expect).
    /// </summary>
    public async Task<string> BuildApprovedZomboidModIdExportAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var modIds = await db.Mods
            .Where(m => m.IsInModlist && m.Game == Mod.DefaultGame)
            .OrderBy(m => m.Id)
            .SelectMany(m => m.PzModIds)
            .Select(p => p.Value)
            .ToListAsync();
        return string.Join(';', modIds.Select(id => $"\\{id}"));
    }
}
