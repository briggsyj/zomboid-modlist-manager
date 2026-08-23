using ModlistManager.Services;
using Xunit;

namespace ModlistManager.Tests;

public class SteamCmdInstallResolverTests
{
    /// <summary>
    /// Chocolatey puts a shim on PATH rather than steamcmd.exe itself, and the shim reports its real
    /// target under "--shimgen-noop". Getting this wrong means copying the shim into the working
    /// directory, where it quietly re-launches the read-only original.
    /// </summary>
    [Fact]
    public void ParseShimTarget_ReadsTheTargetOutOfShimgenOutput()
    {
        const string output = """
            [shim]: Set up Shim to run with the following parameters:
              path to executable: C:\ProgramData\chocolatey\lib\steamcmd\tools\steamcmd.exe
              working directory: C:\somewhere
              is gui? False
            """;

        Assert.Equal(
            @"C:\ProgramData\chocolatey\lib\steamcmd\tools\steamcmd.exe",
            SteamCmdInstallResolver.ParseShimTarget(output));
    }

    [Fact]
    public void ParseShimTarget_ReturnsNullForOrdinarySteamCmdOutput()
    {
        const string output = "Steam Console Client (c) Valve Corporation\nLoading Steam API...OK\n";

        Assert.Null(SteamCmdInstallResolver.ParseShimTarget(output));
    }

    [Fact]
    public void LocateExecutable_ReturnsNullForAPathThatDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-steamcmd", "steamcmd.exe");

        Assert.Null(SteamCmdInstallResolver.LocateExecutable(missing));
    }

    [Fact]
    public void LocateExecutable_ResolvesAnExistingPathToAFullPath()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var executable = Path.Combine(directory.FullName, "steamcmd.exe");
            File.WriteAllText(executable, string.Empty);

            var located = SteamCmdInstallResolver.LocateExecutable(
                Path.Combine(directory.FullName, ".", "steamcmd.exe"));

            Assert.Equal(executable, located);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>A bare name is looked up on PATH, the way "steamcmd" is meant to work by default.</summary>
    [Fact]
    public void LocateExecutable_SearchesPathForABareName()
    {
        var directory = Directory.CreateTempSubdirectory();
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var name = OperatingSystem.IsWindows() ? "fake-steamcmd.exe" : "fake-steamcmd";
            var executable = Path.Combine(directory.FullName, name);
            File.WriteAllText(executable, string.Empty);
            Environment.SetEnvironmentVariable("PATH", directory.FullName);

            Assert.Equal(executable, SteamCmdInstallResolver.LocateExecutable("fake-steamcmd"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            directory.Delete(recursive: true);
        }
    }
}
