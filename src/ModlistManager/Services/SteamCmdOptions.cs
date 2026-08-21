namespace ModlistManager.Services;

public class SteamCmdOptions
{
    public const string SectionName = "SteamCmd";

    /// <summary>Path to the steamcmd executable. Defaults to "steamcmd" (resolved via PATH).</summary>
    public string ExecutablePath { get; set; } = "steamcmd";

    /// <summary>
    /// Directory that SteamCMD treats as its own home/install directory - the one under which it creates
    /// "steamapps/workshop/content/{AppId}/{itemId}" after a workshop_download_item call. This varies by how
    /// SteamCMD was installed, so it must be configured explicitly for the fetch feature to work.
    /// </summary>
    public string? WorkshopContentRoot { get; set; }

    /// <summary>Steam App ID for Project Zomboid.</summary>
    public string AppId { get; set; } = "108600";

    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Delete the downloaded workshop item content after parsing mod.info files, to save disk space.</summary>
    public bool CleanupAfterFetch { get; set; } = true;
}
