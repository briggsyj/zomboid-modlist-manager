using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace ModlistManager.Services;

/// <summary>Where SteamCMD will actually run from, and the directory it will treat as its Steam root.</summary>
public sealed record SteamCmdInstall(string ExecutablePath, string SteamRoot);

/// <summary>
/// Works out a SteamCMD installation that can actually run.
///
/// SteamCMD has no notion of a separate data directory: config, logs, and downloaded workshop content
/// all live next to its own executable, and it dies with a stack overflow rather than an error message
/// if that directory is read-only. System-wide installs (Chocolatey's lives under
/// C:\ProgramData\chocolatey) are read-only to a normal user, so this resolver copies the bootstrapper
/// into a writable working directory and runs SteamCMD from there instead - a bare steamcmd.exe
/// redownloads everything else it needs on first launch.
/// </summary>
public sealed class SteamCmdInstallResolver(
    IOptions<SteamCmdOptions> options,
    ILogger<SteamCmdInstallResolver> logger)
{
    private readonly SteamCmdOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (SteamCmdInstall? Install, string? Error)? _cached;

    public record Result(SteamCmdInstall? Install, string? Error);

    /// <summary>
    /// Resolved once and reused: locating the executable can mean starting a process, and every
    /// queued mod would otherwise pay for it.
    /// </summary>
    public async Task<Result> ResolveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _cached ??= Resolve();
            return new Result(_cached.Value.Install, _cached.Value.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    private (SteamCmdInstall? Install, string? Error) Resolve()
    {
        var executable = LocateExecutable(_options.ExecutablePath);
        if (executable is null)
        {
            return (null, $"executable not found at '{_options.ExecutablePath}' (not an existing file, and not on PATH).");
        }

        if (OperatingSystem.IsWindows() && !LooksLikeSteamCmdDirectory(Path.GetDirectoryName(executable)!))
        {
            // Package managers put a launcher on PATH rather than the real binary - Chocolatey's
            // 130KB shim, for instance. Copying that around would just re-launch the read-only
            // original, so ask it where the real executable lives.
            executable = ResolveChocolateyShim(executable) ?? executable;
        }

        var installDirectory = Path.GetDirectoryName(executable)!;

        if (IsWritable(installDirectory))
        {
            return (new SteamCmdInstall(executable, SteamRootFor(installDirectory)), null);
        }

        if (!OperatingSystem.IsWindows())
        {
            return (null,
                $"its install directory '{installDirectory}' is not writable, and SteamCMD keeps its state " +
                "there. Reinstall it somewhere writable, or point SteamCmd:ExecutablePath at a copy you own.");
        }

        var workingDirectory = ResolveWorkingDirectory();
        try
        {
            executable = BootstrapInto(executable, workingDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null,
                $"its install directory '{installDirectory}' is not writable and bootstrapping a copy " +
                $"into '{workingDirectory}' failed: {ex.Message}");
        }

        return (new SteamCmdInstall(executable, SteamRootFor(workingDirectory)), null);
    }

    private string SteamRootFor(string directory) =>
        _options.WorkshopContentRoot is { Length: > 0 } configured ? configured : directory;

    /// <summary>Full path to the given executable, resolved against PATH when it has no directory part.</summary>
    public static string? LocateExecutable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        if (executablePath.Contains(Path.DirectorySeparatorChar) || executablePath.Contains(Path.AltDirectorySeparatorChar))
        {
            return File.Exists(executablePath) ? Path.GetFullPath(executablePath) : null;
        }

        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".bat", ".cmd", "" } : new[] { "", ".sh" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executablePath + extension);
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>A real Windows SteamCMD install always ships steamclient.dll beside steamcmd.exe.</summary>
    private static bool LooksLikeSteamCmdDirectory(string directory) =>
        File.Exists(Path.Combine(directory, "steamclient.dll"));

    /// <summary>
    /// Pulls the real target out of a Chocolatey shim's "--shimgen-noop" report. Returns null for
    /// anything that is not a shim, which is the common case.
    /// </summary>
    public static string? ParseShimTarget(string shimOutput)
    {
        const string marker = "path to executable:";
        foreach (var line in shimOutput.Split('\n'))
        {
            var index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var target = line[(index + marker.Length)..].Trim().Trim('"');
            if (target.Length > 0)
            {
                return target;
            }
        }

        return null;
    }

    private string? ResolveChocolateyShim(string shimPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = shimPath,
                Arguments = "--shimgen-noop",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            var output = new StringBuilder();
            output.Append(process.StandardOutput.ReadToEnd());
            output.Append(process.StandardError.ReadToEnd());
            if (!process.WaitForExit(10_000))
            {
                TryKill(process);
                return null;
            }

            var target = ParseShimTarget(output.ToString());
            if (target is null || !File.Exists(target) ||
                string.Equals(Path.GetFullPath(target), shimPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            logger.LogInformation("Resolved SteamCMD launcher {Shim} to {Target}", shimPath, target);
            return Path.GetFullPath(target);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not resolve {Shim} as a Chocolatey shim", shimPath);
            return null;
        }
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

    private string ResolveWorkingDirectory()
    {
        if (_options.WorkingDirectory is { Length: > 0 } configured)
        {
            return Path.GetFullPath(configured);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = localAppData.Length > 0 ? localAppData : Path.GetTempPath();
        return Path.Combine(root, "ZomboidModlistManager", "steamcmd");
    }

    /// <summary>
    /// Copies the bootstrapper into <paramref name="workingDirectory"/> so SteamCMD can own that
    /// directory outright. Only copies when it is not already there: SteamCMD replaces its own
    /// executable when it self-updates, and overwriting that with the older system copy would make it
    /// redownload its whole client on every fetch.
    /// </summary>
    private string BootstrapInto(string executable, string workingDirectory)
    {
        Directory.CreateDirectory(workingDirectory);
        var destination = Path.Combine(workingDirectory, Path.GetFileName(executable));

        if (!File.Exists(destination))
        {
            File.Copy(executable, destination);
            logger.LogInformation(
                "SteamCMD's install directory is read-only; bootstrapped a private copy at {Destination}", destination);
        }

        return destination;
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".modlist-write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException)
        {
            return false;
        }
    }
}
