namespace ModlistManager.Data.Entities;

/// <summary>Where a mod's Project Zomboid Mod ID(s) came from.</summary>
public enum ModIdSource
{
    /// <summary>Not resolved yet, or the lookup failed.</summary>
    Unknown,

    /// <summary>Read from the "Mod ID:" line in the item's Steam Workshop description.</summary>
    SteamWorkshopApi,

    /// <summary>Read from mod.info inside the item downloaded by SteamCMD - authoritative.</summary>
    SteamCmd
}
