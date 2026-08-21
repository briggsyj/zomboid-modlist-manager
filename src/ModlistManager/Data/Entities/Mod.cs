using System.ComponentModel.DataAnnotations.Schema;

namespace ModlistManager.Data.Entities;

/// <summary>
/// A specific Steam Workshop item for a game. Deduplicated by (Game, WorkshopId) so multiple
/// requests for the same item share one Mod row instead of each tracking their own fetch/mod-id state.
/// </summary>
public class Mod
{
    public const string DefaultGame = "Project Zomboid";

    public int Id { get; set; }

    public string Game { get; set; } = DefaultGame;

    /// <summary>Parsed numeric Steam Workshop published file ID.</summary>
    public required string WorkshopId { get; set; }

    /// <summary>
    /// The item's real title on the Steam Workshop, resolved during the mod ID fetch. Null until the
    /// lookup completes, or if it failed - use <see cref="DisplayName"/> when showing it.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>The workshop title once known, falling back to the item ID while it isn't.</summary>
    [NotMapped]
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? $"Workshop item {WorkshopId}" : Title;

    public ModIdFetchStatus FetchStatus { get; set; } = ModIdFetchStatus.Queued;

    /// <summary>Diagnostic output from the last SteamCMD fetch attempt (stdout/stderr tail, error message, etc).</summary>
    public string? FetchLog { get; set; }

    /// <summary>True once at least one request for this mod has been approved.</summary>
    public bool IsInModlist { get; set; }

    /// <summary>
    /// Whether the mod should actually be loaded by the server. Inactive mods stay on the modlist and
    /// keep their workshop item downloaded (they're still in the WorkshopItems= export), but their
    /// Mod IDs are left out of the Mods= export - which is how a PZ server disables a mod without
    /// removing it. Admin-controlled from the modlist page.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When this mod first entered the modlist. Preserved across later approve/un-approve toggles.</summary>
    public DateTime? AddedToModlistAtUtc { get; set; }

    public List<PzModId> PzModIds { get; set; } = [];

    public List<ModRequest> Requests { get; set; } = [];
}
