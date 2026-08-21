using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace ModlistManager.Services;

/// <summary>
/// Downloads a workshop item with SteamCMD (anonymous login) and reads the Mod ID(s) out of the
/// mod.info file(s) it contains. This is the authoritative source, but requires SteamCMD to be
/// installed and actually runnable on the host - see <see cref="SteamCmdOptions.Enabled"/>.
/// </summary>
public class SteamCmdModInfoReader(IOptions<SteamCmdOptions> options)
{
    private readonly SteamCmdOptions _options = options.Value;

    public record Result(IReadOnlyList<PzModIdResult> ModIds, string? Error);

    public async Task<Result> ReadModIdsAsync(string workshopId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.WorkshopContentRoot))
        {
            return new Result([], "SteamCmd:WorkshopContentRoot is not configured.");
        }

        var contentDir = Path.Combine(
            _options.WorkshopContentRoot, "steamapps", "workshop", "content", _options.AppId, workshopId);

        var run = await RunSteamCmdAsync(workshopId, cancellationToken);
        if (!run.Success)
        {
            return new Result([], run.Message);
        }

        if (!Directory.Exists(contentDir))
        {
            return new Result([],
                $"SteamCMD reported success but no content appeared at {contentDir} (check SteamCmd:WorkshopContentRoot).");
        }

        var modInfoFiles = ModInfoParser.FindModInfoFiles(contentDir);
        var discovered = new List<PzModIdResult>();
        foreach (var file in modInfoFiles)
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (ModInfoParser.Parse(content) is { } parsed)
            {
                discovered.Add(new PzModIdResult(parsed.ModId, parsed.ModName));
            }
        }

        if (_options.CleanupAfterFetch)
        {
            TryDeleteDirectory(contentDir);
        }

        return discovered.Count > 0
            ? new Result(discovered, null)
            : new Result([], $"Downloaded content had no mod.info with an id= field ({modInfoFiles.Count} scanned).");
    }

    private async Task<(bool Success, string? Message)> RunSteamCmdAsync(string workshopId, CancellationToken cancellationToken)
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
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return (false, $"executable not found at '{_options.ExecutablePath}'.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return (false, $"timed out after {_options.TimeoutSeconds}s.");
        }

        if (process.ExitCode != 0)
        {
            var output = (stdOut.ToString() + stdErr).Trim();

            // SteamCMD only ships a 32-bit x86 binary. On an arm64 host emulating amd64 (Apple
            // Silicon Docker) it cannot execute at all, and the giveaway is a non-zero exit with
            // no output whatsoever - worth calling out, since it otherwise looks like a network error.
            if (output.Length == 0)
            {
                return (false,
                    $"exited with code {process.ExitCode} and produced no output. SteamCMD is a 32-bit x86 " +
                    "binary, which cannot run on arm64 hosts emulating amd64 (e.g. Docker on Apple Silicon).");
            }

            return (false, $"exited with code {process.ExitCode}. {Tail(output, 1500)}");
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
}
