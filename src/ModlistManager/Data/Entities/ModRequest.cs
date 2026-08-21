namespace ModlistManager.Data.Entities;

public class ModRequest
{
    public int Id { get; set; }

    public required string Title { get; set; }

    public int ModId { get; set; }

    public Mod? Mod { get; set; }

    /// <summary>Lowercase-normalized requester name.</summary>
    public required string RequesterName { get; set; }

    /// <summary>Why the requester thinks the server needs this mod. Optional.</summary>
    public string? Reason { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DecidedAtUtc { get; set; }

    public string? AdminNotes { get; set; }
}
