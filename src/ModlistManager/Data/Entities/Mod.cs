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

    public ModIdFetchStatus FetchStatus { get; set; } = ModIdFetchStatus.Queued;

    /// <summary>Diagnostic output from the last SteamCMD fetch attempt (stdout/stderr tail, error message, etc).</summary>
    public string? FetchLog { get; set; }

    /// <summary>True once at least one request for this mod has been approved.</summary>
    public bool IsInModlist { get; set; }

    /// <summary>When this mod first entered the modlist. Preserved across later approve/un-approve toggles.</summary>
    public DateTime? AddedToModlistAtUtc { get; set; }

    public List<PzModId> PzModIds { get; set; } = [];

    public List<ModRequest> Requests { get; set; } = [];
}
