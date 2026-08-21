using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ModlistManager.Data;
using ModlistManager.Data.Entities;

namespace ModlistManager.Services;

public partial class ModRequestService(IDbContextFactory<AppDbContext> dbContextFactory, SteamCmdFetchQueue fetchQueue)
{
    public record CreateResult(bool Success, string? Error, int? RequestId);

    public async Task<CreateResult> CreateRequestAsync(string? title, string? workshopInput, string? requesterName, string game = ModRequest.DefaultGame)
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
        var request = new ModRequest
        {
            Title = title,
            Game = game,
            WorkshopUrlInput = workshopInput!.Trim(),
            WorkshopId = workshopId,
            RequesterName = normalizedName,
            Status = RequestStatus.Pending,
            FetchStatus = ModIdFetchStatus.Queued
        };

        db.ModRequests.Add(request);
        await db.SaveChangesAsync();

        fetchQueue.Enqueue(request.Id);

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
        return await db.ModRequests
            .Select(r => r.Game)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync();
    }

    public async Task<List<ModRequest>> GetRequestsAsync(RequestStatus? status = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var query = db.ModRequests.Include(r => r.ModIds).OrderByDescending(r => r.CreatedAtUtc).AsQueryable();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        return await query.ToListAsync();
    }

    public async Task SetStatusAsync(int requestId, RequestStatus status)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var request = await db.ModRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null)
        {
            return;
        }

        request.Status = status;
        request.DecidedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task RetryFetchAsync(int requestId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var request = await db.ModRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null)
        {
            return;
        }

        request.FetchStatus = ModIdFetchStatus.Queued;
        request.FetchLog = null;
        await db.SaveChangesAsync();

        fetchQueue.Enqueue(requestId);
    }

    public async Task AddManualModIdAsync(int requestId, string modId, string? modName)
    {
        modId = modId.Trim();
        if (modId.Length == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.ModRequestModIds.Add(new ModRequestModId
        {
            ModRequestId = requestId,
            ModId = modId,
            ModName = string.IsNullOrWhiteSpace(modName) ? null : modName.Trim(),
            IsManual = true
        });
        await db.SaveChangesAsync();
    }

    public async Task DeleteModIdAsync(int modRequestModIdId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var entry = await db.ModRequestModIds.FirstOrDefaultAsync(m => m.Id == modRequestModIdId);
        if (entry is null)
        {
            return;
        }

        db.ModRequestModIds.Remove(entry);
        await db.SaveChangesAsync();
    }

    /// <summary>Semicolon-delimited Steam Workshop item IDs for every approved request, regardless of game.</summary>
    public async Task<string> BuildApprovedWorkshopIdExportAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var ids = await db.ModRequests
            .Where(r => r.Status == RequestStatus.Approved)
            .Select(r => r.WorkshopId)
            .ToListAsync();
        return string.Join(';', ids);
    }

    /// <summary>
    /// Semicolon-delimited Project Zomboid Mod IDs for approved Project Zomboid requests, each prefixed
    /// with a backslash (matching the Mods= line format PZ server configs expect).
    /// </summary>
    public async Task<string> BuildApprovedZomboidModIdExportAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var modIds = await db.ModRequests
            .Where(r => r.Status == RequestStatus.Approved && r.Game == ModRequest.DefaultGame)
            .SelectMany(r => r.ModIds)
            .Select(m => m.ModId)
            .ToListAsync();
        return string.Join(';', modIds.Select(id => $"\\{id}"));
    }
}
