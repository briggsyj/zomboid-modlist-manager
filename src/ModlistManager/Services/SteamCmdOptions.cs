namespace ModlistManager.Services;

public class SteamCmdOptions
{
    public const string SectionName = "SteamCmd";

    /// <summary>
    /// Opt in to downloading each workshop item with SteamCMD and reading mod.info for authoritative
    /// Mod IDs. Off by default: SteamCMD only ships a 32-bit x86 binary, which cannot execute under
    /// arm64 hosts emulating amd64 (Apple Silicon Docker), and it downloads the whole mod just to read
    /// one file. When off - or when it fails - Mod IDs come from the Steam Workshop API instead.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Path to the steamcmd executable. Defaults to "steamcmd" (resolved via PATH).</summary>
    public string ExecutablePath { get; set; } = "steamcmd";

    /// <summary>
    /// Override for the directory SteamCMD treats as its Steam root - the one under which it creates
    /// "steamapps/workshop/content/{AppId}/{itemId}". Leave empty to derive it from wherever the
    /// executable actually ends up running from, which is what <see cref="SteamCmdInstallResolver"/>
    /// works out.
    /// </summary>
    public string? WorkshopContentRoot { get; set; }

    /// <summary>
    /// Writable directory to bootstrap a private SteamCMD into when the installed one sits in a
    /// read-only location (a system-wide install such as Chocolatey's). SteamCMD keeps its whole
    /// state - config, logs and downloaded workshop content - next to its own executable and simply
    /// crashes if it cannot write there. Empty means "under the user's local application data".
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Steam App ID for Project Zomboid.</summary>
    public string AppId { get; set; } = "108600";

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Delete the downloaded workshop item content after parsing mod.info files, to save disk space.</summary>
    public bool CleanupAfterFetch { get; set; } = true;
}
