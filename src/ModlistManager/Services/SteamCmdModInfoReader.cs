using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace ModlistManager.Services;

/// <summary>
/// Downloads a workshop item with SteamCMD (anonymous login) and reads the Mod ID(s) out of the
/// mod.info file(s) it contains. This is the authoritative source, but requires SteamCMD to be
/// installed and actually runnable on the host - see <see cref="SteamCmdOptions.Enabled"/>.
/// </summary>
public class SteamCmdModInfoReader(IOptions<SteamCmdOptions> options, SteamCmdInstallResolver installResolver)
{
    private readonly SteamCmdOptions _options = options.Value;

    public record Result(IReadOnlyList<PzModIdResult> ModIds, string? Error);

    public async Task<Result> ReadModIdsAsync(string workshopId, CancellationToken cancellationToken = default)
    {
        var resolved = await installResolver.ResolveAsync(cancellationToken);
        if (resolved.Install is null)
        {
            return new Result([], resolved.Error);
        }

        var contentDir = Path.Combine(
            resolved.Install.SteamRoot, "steamapps", "workshop", "content", _options.AppId, workshopId);

        var run = await RunSteamCmdAsync(resolved.Install, workshopId, cancellationToken);

        // The exit code is checked only as a fallback for explaining an empty download: SteamCMD
        // reports non-zero for benign states too (notably straight after it self-updates), so the
        // content on disk is the more reliable signal that the download worked.
        if (!Directory.Exists(contentDir))
        {
            return new Result([], run.Success
                ? $"reported success but no content appeared at {contentDir} (set SteamCmd:WorkshopContentRoot if SteamCMD stores it elsewhere)."
                : run.Message);
        }

        var modInfoFiles = ModInfoParser.FindModInfoFiles(contentDir);

        // Mods commonly ship the same mod.info more than once - once at the mod root and again under
        // a per-build subdirectory (42/) - so the same ID would otherwise be recorded twice.
        var discovered = new List<PzModIdResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in modInfoFiles)
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            if (ModInfoParser.Parse(content) is { } parsed && seen.Add(parsed.ModId))
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

    private async Task<(bool Success, string? Message)> RunSteamCmdAsync(
        SteamCmdInstall install, string workshopId, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = install.ExecutablePath,
            Arguments = $"+login anonymous +workshop_download_item {_options.AppId} {workshopId} +quit",
            // SteamCMD resolves some of its own state relative to the current directory, so run it
            // from the directory it owns rather than from wherever the web app happens to be.
            WorkingDirectory = Path.GetDirectoryName(install.ExecutablePath)!,
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
            return (false, $"could not start '{install.ExecutablePath}': {ex.Message}");
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

            if (process.ExitCode == StackOverflowExitCode)
            {
                return (false,
                    $"crashed on startup (exit code 0x{StackOverflowExitCode:X8}) while running from " +
                    $"'{Path.GetDirectoryName(install.ExecutablePath)}'. SteamCMD does this when it cannot " +
                    "write to its own directory; set SteamCmd:WorkingDirectory to somewhere writable.");
            }

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

    /// <summary>Windows STATUS_STACK_OVERFLOW, which is how SteamCMD fails in a read-only directory.</summary>
    private const int StackOverflowExitCode = unchecked((int)0xC00000FD);

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
