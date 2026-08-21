namespace ModlistManager.Data.Entities;

public class ModRequest
{
    public const string DefaultGame = "Project Zomboid";

    public int Id { get; set; }

    public required string Title { get; set; }

    public string Game { get; set; } = DefaultGame;

    /// <summary>Raw text as submitted (full URL or bare ID).</summary>
    public required string WorkshopUrlInput { get; set; }

    /// <summary>Parsed numeric Steam Workshop published file ID.</summary>
    public required string WorkshopId { get; set; }

    /// <summary>Lowercase-normalized requester name.</summary>
    public required string RequesterName { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DecidedAtUtc { get; set; }

    public string? AdminNotes { get; set; }

    public ModIdFetchStatus FetchStatus { get; set; } = ModIdFetchStatus.Queued;

    /// <summary>Diagnostic output from the last SteamCMD fetch attempt (stdout/stderr tail, error message, etc).</summary>
    public string? FetchLog { get; set; }

    public List<ModRequestModId> ModIds { get; set; } = [];
}
