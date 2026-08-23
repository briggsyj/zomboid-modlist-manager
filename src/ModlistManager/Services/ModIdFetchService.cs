using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModlistManager.Data;
using ModlistManager.Data.Entities;

namespace ModlistManager.Services;

/// <summary>
/// Resolves the Project Zomboid Mod ID(s) for a queued Mod, in the background so submitting a
/// request never blocks on network I/O.
///
/// The Steam Workshop API is the default source: PZ's upload convention puts "Mod ID: X" in the
/// item description, so the ID can be read without downloading the mod. SteamCMD (which reads the
/// authoritative mod.info out of the downloaded content) is used first when explicitly enabled,
/// falling back to the API if it fails.
/// </summary>
public class ModIdFetchService(
    ModIdFetchQueue queue,
    IDbContextFactory<AppDbContext> dbContextFactory,
    SteamWorkshopApiClient workshopApi,
    SteamCmdModInfoReader steamCmdReader,
    IOptions<SteamCmdOptions> options,
    ILogger<ModIdFetchService> logger) : BackgroundService
{
    private readonly SteamCmdOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var modId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(modId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error fetching mod IDs for Mod {ModId}", modId);
                await SaveFailureAsync(modId, $"Unexpected error: {ex.Message}");
            }
        }
    }

    private async Task ProcessAsync(int modId, CancellationToken stoppingToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(stoppingToken);
        var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == modId, stoppingToken);
        if (mod is null)
        {
            return;
        }

        mod.FetchStatus = ModIdFetchStatus.Processing;
        await db.SaveChangesAsync(stoppingToken);

        var notes = new List<string>();
        IReadOnlyList<PzModIdResult> discovered = [];
        var source = ModIdSource.Unknown;

        if (_options.Enabled)
        {
            var steamCmdResult = await steamCmdReader.ReadModIdsAsync(mod.WorkshopId, stoppingToken);
            if (steamCmdResult.ModIds.Count > 0)
            {
                discovered = steamCmdResult.ModIds;
                source = ModIdSource.SteamCmd;
                notes.Add($"SteamCMD found {discovered.Count} mod ID(s) in mod.info.");
            }
            else
            {
                notes.Add($"SteamCMD: {steamCmdResult.Error} Falling back to the Steam Workshop API.");
            }
        }

        // The API also gives us the item's real title, which is worth storing even when SteamCMD
        // already supplied the mod IDs - the UI shows it instead of a bare numeric workshop ID.
        var item = await workshopApi.GetItemAsync(mod.WorkshopId, stoppingToken);
        if (item is not null && !string.IsNullOrWhiteSpace(item.Title))
        {
            mod.Title = item.Title;
        }

        if (discovered.Count == 0)
        {
            if (item is null)
            {
                await SaveFailureAsync(db, mod, Join(notes,
                    $"Steam has no workshop item with ID {mod.WorkshopId}. Check the link, or add the Mod ID manually below."));
                return;
            }

            var fromDescription = PzModIdExtractor.Extract(item.Description);
            if (fromDescription.Count == 0)
            {
                await SaveFailureAsync(db, mod, Join(notes,
                    "The workshop description doesn't state a \"Mod ID:\", so it couldn't be read automatically. " +
                    "Add the Mod ID manually below (it's the folder name under the mod's media/ directory)."));
                return;
            }

            discovered = [.. fromDescription.Select(id => new PzModIdResult(id, null))];
            source = ModIdSource.SteamWorkshopApi;
            notes.Add($"Read {discovered.Count} mod ID(s) from the workshop description.");
        }

        // Replace previously auto-discovered entries; keep any manually-entered ones.
        var previousAuto = await db.PzModIds
            .Where(p => p.ModId == mod.Id && !p.IsManual)
            .ToListAsync(stoppingToken);
        db.PzModIds.RemoveRange(previousAuto);

        foreach (var found in discovered)
        {
            db.PzModIds.Add(new PzModId
            {
                ModId = mod.Id,
                Value = found.Value,
                Name = found.Name,
                IsManual = false
            });
        }

        mod.FetchStatus = ModIdFetchStatus.Completed;
        mod.ModIdSource = source;
        mod.FetchLog = Join(notes, $"Mod ID(s): {string.Join(", ", discovered.Select(d => d.Value))}");
        await db.SaveChangesAsync(stoppingToken);
    }

    private static string Join(IEnumerable<string> notes, string summary) =>
        string.Join('\n', notes.Append(summary));

    private static async Task SaveFailureAsync(AppDbContext db, Mod mod, string message)
    {
        mod.FetchStatus = ModIdFetchStatus.Failed;
        mod.FetchLog = message;
        await db.SaveChangesAsync();
    }

    private async Task SaveFailureAsync(int modId, string message)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var mod = await db.Mods.FirstOrDefaultAsync(m => m.Id == modId);
        if (mod is not null)
        {
            await SaveFailureAsync(db, mod, message);
        }
    }
}

public readonly record struct PzModIdResult(string Value, string? Name);
