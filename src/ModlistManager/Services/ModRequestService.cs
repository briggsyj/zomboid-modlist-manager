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

        // The admin-defined load order, which the clipboard exports also follow.
        return await query.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();
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
        return await query.OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();
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
            var joiningModlist = !request.Mod.IsInModlist;
            request.Mod.IsInModlist = true;
            request.Mod.AddedToModlistAtUtc ??= DateTime.UtcNow;

            if (joiningModlist)
            {
                // Append to the end; an admin can drag it into place afterwards. Re-approving a mod
                // that's already listed leaves its position alone.
                var highest = await db.Mods
                    .Where(m => m.IsInModlist && m.Id != request.ModId)
                    .Select(m => (int?)m.SortOrder)
                    .MaxAsync() ?? 0;
                request.Mod.SortOrder = highest + 1;
            }
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

    /// <summary>Saves an admin's free-text notes against a mod. Blank clears them.</summary>
    public async Task SetModAdminNotesAsync(int modId, string? notes)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == modId);
        if (mod is null)
        {
            return;
        }

        mod.AdminNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
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

        // A duplicate would be emitted twice in the Mods= line, so ignore it rather than storing it.
        var alreadyPresent = await db.PzModIds
            .AnyAsync(p => p.ModId == modId && p.Value == modIdValue);
        if (alreadyPresent)
        {
            return;
        }

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
    /// Includes or excludes a single Mod ID from the Mods= export, without deleting it - useful when
    /// a workshop item bundles several mods and only some are wanted.
    /// </summary>
    public async Task SetPzModIdEnabledAsync(int pzModIdId, bool isEnabled)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var entry = await db.PzModIds.FirstOrDefaultAsync(m => m.Id == pzModIdId);
        if (entry is null)
        {
            return;
        }

        entry.IsEnabled = isEnabled;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Corrects a Mod ID's value - the automatic lookup can read a stale or wrong one from a
    /// workshop description. A blank value is ignored rather than wiping the entry; deleting is a
    /// separate, explicit action.
    /// </summary>
    /// <returns>True if the value changed.</returns>
    public async Task<bool> UpdatePzModIdValueAsync(int pzModIdId, string? newValue)
    {
        newValue = newValue?.Trim();
        if (string.IsNullOrEmpty(newValue))
        {
            return false;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var entry = await db.PzModIds.FirstOrDefaultAsync(m => m.Id == pzModIdId);
        if (entry is null || string.Equals(entry.Value, newValue, StringComparison.Ordinal))
        {
            return false;
        }

        // Renaming onto a value the same mod already carries would duplicate it in the export.
        var wouldDuplicate = await db.PzModIds
            .AnyAsync(p => p.ModId == entry.ModId && p.Id != entry.Id && p.Value == newValue);
        if (wouldDuplicate)
        {
            return false;
        }

        entry.Value = newValue;

        // Hand-corrected, so a later re-fetch shouldn't quietly overwrite it - the fetch only
        // replaces entries it discovered itself.
        entry.IsManual = true;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Reorders the modlist to the given sequence of mod ids, renumbering SortOrder from 1. Ids that
    /// aren't on the modlist are ignored, and any modlist mod the caller omitted keeps its relative
    /// position at the end - so a stale page can't silently drop a mod out of the order.
    /// </summary>
    public async Task ReorderModlistAsync(IReadOnlyList<int> modIdsInOrder)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var mods = await db.Mods.Where(m => m.IsInModlist).ToListAsync();

        var position = 0;
        foreach (var modId in modIdsInOrder)
        {
            var mod = mods.FirstOrDefault(m => m.Id == modId);
            if (mod is not null)
            {
                mod.SortOrder = ++position;
            }
        }

        foreach (var leftover in mods
            .Where(m => !modIdsInOrder.Contains(m.Id))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id))
        {
            leftover.SortOrder = ++position;
        }

        await db.SaveChangesAsync();
    }

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
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .Select(m => m.WorkshopId)
            .ToListAsync();

        return string.Join(';', ids);
    }

    /// <summary>
    /// Semicolon-delimited Project Zomboid Mod IDs for mods currently on the Project Zomboid modlist,
    /// each prefixed with a backslash (matching the Mods= line format PZ server configs expect).
    /// Only active mods are included - that's what makes a mod inactive.
    ///
    /// Follows the admin-defined modlist order, matching the WorkshopItems= export. Load order is
    /// significant in Project Zomboid, so this is the order the server will apply. A workshop item
    /// bundling several mods contributes its Mod IDs together, in the order they were discovered -
    /// minus any an admin has unticked.
    /// </summary>
    public async Task<string> BuildApprovedZomboidModIdExportAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var mods = await db.Mods
            .Include(m => m.PzModIds)
            .Where(m => m.IsInModlist && m.IsActive && m.Game == Mod.DefaultGame)
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id)
            .ToListAsync();

        var modIds = mods.SelectMany(m => m.PzModIds
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Id)
            .Select(p => p.Value));

        return string.Join(';', modIds.Select(id => $"\\{id}"));
    }
}
