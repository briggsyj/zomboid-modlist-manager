using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ModlistManager.Data;
using ModlistManager.Data.Entities;

namespace ModlistManager.Services;

public partial class ModRequestService(IDbContextFactory<AppDbContext> dbContextFactory, ModIdFetchQueue fetchQueue)
{
    public record CreateResult(bool Success, string? Error, int? RequestId);

    /// <summary>An existing request for the same workshop item, used to warn about duplicates.</summary>
    public record ExistingRequestInfo(string RequesterName, RequestStatus Status, string? ModTitle);

    /// <remarks>
    /// There's deliberately no title parameter: the mod's name comes from the workshop item itself,
    /// resolved by the background lookup, so two people can't file the same mod under different names.
    /// </remarks>
    public async Task<CreateResult> CreateRequestAsync(
        string? workshopInput, string? requesterName, string? reason = null, string game = Mod.DefaultGame)
    {
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
            Mod = mod,
            RequesterName = normalizedName,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
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

    /// <summary>
    /// Finds requests already covering the workshop item the given input points at, so the request
    /// form can warn about duplicates before anything is submitted. Accepts a full workshop URL or a
    /// bare ID, and returns an empty list when the input isn't a usable workshop reference yet.
    /// </summary>
    public async Task<List<ExistingRequestInfo>> FindExistingRequestsAsync(string? workshopInput, string game = Mod.DefaultGame)
    {
        var workshopId = WorkshopIdParser.TryParse(workshopInput);
        if (workshopId is null)
        {
            return [];
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.ModRequests
            .Where(r => r.Mod!.Game == game && r.Mod.WorkshopId == workshopId)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => new ExistingRequestInfo(r.RequesterName, r.Status, r.Mod!.Title))
            .ToListAsync();
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

    /// <summary>
    /// Requests in any of the given statuses, newest first. Lets a caller fetch several statuses in
    /// one round trip rather than querying per status.
    /// </summary>
    public async Task<List<ModRequest>> GetRequestsByStatusAsync(params RequestStatus[] statuses)
    {
        if (statuses.Length == 0)
        {
            return [];
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.ModRequests
            .Include(r => r.Mod!).ThenInclude(m => m.PzModIds)
            .Where(r => statuses.Contains(r.Status))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync();
    }

    /// <summary>
    /// The current modlist: every Mod with at least one approved request, optionally filtered by game.
    /// </summary>
    /// <param name="activeOnly">
    /// Restrict to mods the server actually loads. Visitors are shown this view, so a mod an admin has
    /// parked isn't advertised as being on the server; admins see everything so they can un-park it.
    /// This is a query filter rather than a UI concern on purpose - the modlist page is public.
    /// </param>
    public async Task<List<Mod>> GetModlistAsync(string? game = null, bool activeOnly = false)
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

        if (activeOnly)
        {
            query = query.Where(m => m.IsActive);
        }

        // Same order as the clipboard exports, so the table can be read against a pasted list.
        var mods = await query.ToListAsync();
        return [.. mods.OrderBy(m => WorkshopIdOrder(m.WorkshopId))];
    }

    /// <summary>
    /// Approved mods the server isn't currently loading - approved, then switched off by an admin.
    /// The counterpart to the visitor modlist: between them they cover every approved mod.
    /// </summary>
    public async Task<List<Mod>> GetParkedModsAsync(string? game = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var query = db.Mods
            .Include(m => m.PzModIds)
            .Include(m => m.Requests)
            .Where(m => m.IsInModlist && !m.IsActive)
            .AsQueryable();
        if (game is not null)
        {
            query = query.Where(m => m.Game == game);
        }

        // Ordered like the modlist and the exports, so every listing reads the same way.
        var parked = await query.ToListAsync();
        return [.. parked.OrderBy(m => WorkshopIdOrder(m.WorkshopId))];
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

    /// <summary>
    /// Marks a mod as active (loaded by the server) or inactive. Inactive mods stay on the modlist
    /// and keep their workshop item in the WorkshopItems= export, but drop out of Mods=.
    /// </summary>
    public async Task SetModActiveAsync(int modId, bool isActive)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == modId);
        if (mod is null)
        {
            return;
        }

        mod.IsActive = isActive;
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

    /// <summary>
    /// Sort key putting workshop IDs in ascending numeric order. They're stored as text, so sorting
    /// them as strings would order "498441420" after "2872282653" - older 9-digit items really do
    /// exist alongside current 10-digit ones. Unparseable values sort last rather than throwing.
    /// </summary>
    private static ulong WorkshopIdOrder(string workshopId) =>
        ulong.TryParse(workshopId, out var value) ? value : ulong.MaxValue;

    /// <summary>
    /// Semicolon-delimited Steam Workshop item IDs for every mod currently on a modlist, regardless
    /// of game. Deliberately includes inactive mods: the server should still download the item so
    /// the mod can be switched back on without a re-download.
    /// </summary>
    public async Task<string> BuildApprovedWorkshopIdExportAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ids = await db.Mods
            .Where(m => m.IsInModlist)
            .Select(m => m.WorkshopId)
            .ToListAsync();

        return string.Join(';', ids.OrderBy(WorkshopIdOrder));
    }

    /// <summary>
    /// Semicolon-delimited Project Zomboid Mod IDs for mods currently on the Project Zomboid modlist,
    /// each prefixed with a backslash (matching the Mods= line format PZ server configs expect).
    /// Only active mods are included - that's what makes a mod inactive.
    ///
    /// Ordered by workshop ID to match the WorkshopItems= export, so the two lists can be read
    /// side by side. A workshop item bundling several mods contributes its Mod IDs together, in the
    /// order they were discovered.
    /// </summary>
    public async Task<string> BuildApprovedZomboidModIdExportAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var mods = await db.Mods
            .Include(m => m.PzModIds)
            .Where(m => m.IsInModlist && m.IsActive && m.Game == Mod.DefaultGame)
            .ToListAsync();

        var modIds = mods
            .OrderBy(m => WorkshopIdOrder(m.WorkshopId))
            .SelectMany(m => m.PzModIds.OrderBy(p => p.Id).Select(p => p.Value));

        return string.Join(';', modIds.Select(id => $"\\{id}"));
    }
}
