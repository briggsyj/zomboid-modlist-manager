namespace ModlistManager.Data.Entities;

/// <summary>
/// A single internal Project Zomboid mod (mod.info) found inside a workshop item. One workshop
/// item can bundle multiple PZ mods, so this is a one-to-many child of Mod.
/// </summary>
public class PzModId
{
    public int Id { get; set; }

    public int ModId { get; set; }

    public Mod? Mod { get; set; }

    /// <summary>The internal Project Zomboid mod id, e.g. "MyCoolMod" (as used in server Mods= lines).</summary>
    public required string Value { get; set; }

    public string? Name { get; set; }

    /// <summary>False when discovered automatically via SteamCMD/mod.info; true when entered by an admin.</summary>
    public bool IsManual { get; set; }

    /// <summary>
    /// Whether this particular Mod ID goes into the Mods= export. A workshop item can bundle several
    /// mods where only some are wanted, so they're toggled individually - separate from Mod.IsActive,
    /// which switches the whole item off.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
