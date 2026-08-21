using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModlistManager.Data;
using ModlistManager.Data.Entities;

namespace ModlistManager.Services;

/// <summary>
/// Consumes queued ModRequest IDs and shells out to SteamCMD (anonymous login) to download the
/// referenced workshop item, then parses any mod.info files it contains for the real Project Zomboid
/// Mod ID(s). Runs in the background so submitting a request never blocks on a Steam download.
/// </summary>
public class SteamCmdFetchService(
    SteamCmdFetchQueue queue,
    IDbContextFactory<AppDbContext> dbContextFactory,
    IOptions<SteamCmdOptions> options,
    ILogger<SteamCmdFetchService> logger) : BackgroundService
{
    private readonly SteamCmdOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var modRequestId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(modRequestId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error fetching mod IDs for request {RequestId}", modRequestId);
                await MarkFailedAsync(modRequestId, $"Unexpected error: {ex.Message}");
            }
        }
    }

    private async Task ProcessAsync(int modRequestId, CancellationToken stoppingToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(stoppingToken);
        var request = await db.ModRequests.FirstOrDefaultAsync(r => r.Id == modRequestId, stoppingToken);
        if (request is null)
        {
            return;
        }

        request.FetchStatus = ModIdFetchStatus.Processing;
        await db.SaveChangesAsync(stoppingToken);

        var contentDir = string.IsNullOrWhiteSpace(_options.WorkshopContentRoot)
            ? null
            : Path.Combine(_options.WorkshopContentRoot, "steamapps", "workshop", "content", _options.AppId, request.WorkshopId);

        if (contentDir is null)
        {
            await FailAsync(db, request,
                "SteamCmd:WorkshopContentRoot is not configured. Set it to the directory SteamCMD uses for downloads, or add the Mod ID manually below.");
            return;
        }

        var runResult = await RunSteamCmdAsync(request.WorkshopId, stoppingToken);
        if (!runResult.Success)
        {
            await FailAsync(db, request, runResult.Message!);
            return;
        }

        if (!Directory.Exists(contentDir))
        {
            await FailAsync(db, request,
                $"SteamCMD reported success but the expected content folder was not found:\n{contentDir}\nCheck the SteamCmd:WorkshopContentRoot setting.");
            return;
        }

        var modInfoFiles = ModInfoParser.FindModInfoFiles(contentDir);
        var discovered = new List<ModInfoParser.ParsedMod>();
        foreach (var file in modInfoFiles)
        {
            var content = await File.ReadAllTextAsync(file, stoppingToken);
            var parsed = ModInfoParser.Parse(content);
            if (parsed is not null)
            {
                discovered.Add(parsed.Value);
            }
        }

        if (_options.CleanupAfterFetch)
        {
            TryDeleteDirectory(contentDir);
        }

        if (discovered.Count == 0)
        {
            await FailAsync(db, request,
                $"Download succeeded but no mod.info file with an id= field was found ({modInfoFiles.Count} mod.info file(s) scanned). " +
                "This workshop item may not be a valid Project Zomboid mod, or uses a non-standard layout - add the Mod ID manually below.");
            return;
        }

        // Replace previously auto-discovered entries; keep any manually-entered ones.
        var previousAuto = await db.ModRequestModIds
            .Where(m => m.ModRequestId == request.Id && !m.IsManual)
            .ToListAsync(stoppingToken);
        db.ModRequestModIds.RemoveRange(previousAuto);

        foreach (var mod in discovered)
        {
            db.ModRequestModIds.Add(new ModRequestModId
            {
                ModRequestId = request.Id,
                ModId = mod.ModId,
                ModName = mod.ModName,
                IsManual = false
            });
        }

        request.FetchStatus = ModIdFetchStatus.Completed;
        request.FetchLog = $"Found {discovered.Count} mod ID(s): {string.Join(", ", discovered.Select(m => m.ModId))}";
        await db.SaveChangesAsync(stoppingToken);
    }

    private async Task<(bool Success, string? Message)> RunSteamCmdAsync(string workshopId, CancellationToken stoppingToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            Arguments = $"+login anonymous +workshop_download_item {_options.AppId} {workshopId} +quit",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return (false,
                $"SteamCMD executable not found at '{_options.ExecutablePath}'. Install SteamCMD and/or set " +
                "SteamCmd:ExecutablePath in appsettings, or add the Mod ID manually below.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            TryKill(process);
            return (false, $"SteamCMD timed out after {_options.TimeoutSeconds}s.");
        }

        if (process.ExitCode != 0)
        {
            var tail = Tail(stdOut.ToString() + stdErr, 2000);
            return (false, $"SteamCMD exited with code {process.ExitCode}.\n{tail}");
        }

        return (true, null);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort; nothing further to do if the process already exited.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; leftover files just consume disk space.
        }
    }

    private static string Tail(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[^maxLength..];

    private async Task FailAsync(AppDbContext db, ModRequest request, string message)
    {
        request.FetchStatus = ModIdFetchStatus.Failed;
        request.FetchLog = message;
        await db.SaveChangesAsync();
    }

    private async Task MarkFailedAsync(int modRequestId, string message)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var request = await db.ModRequests.FirstOrDefaultAsync(r => r.Id == modRequestId);
        if (request is null)
        {
            return;
        }

        request.FetchStatus = ModIdFetchStatus.Failed;
        request.FetchLog = message;
        await db.SaveChangesAsync();
    }
}
