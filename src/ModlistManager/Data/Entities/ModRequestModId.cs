namespace ModlistManager.Data.Entities;

/// <summary>
/// A single internal mod (mod.info) identified for a workshop request. One workshop item
/// can bundle multiple Project Zomboid mods, so this is a one-to-many child of ModRequest.
/// </summary>
public class ModRequestModId
{
    public int Id { get; set; }

    public int ModRequestId { get; set; }

    public ModRequest? ModRequest { get; set; }

    public required string ModId { get; set; }

    public string? ModName { get; set; }

    /// <summary>False when discovered automatically via SteamCMD/mod.info; true when entered by an admin.</summary>
    public bool IsManual { get; set; }
}
