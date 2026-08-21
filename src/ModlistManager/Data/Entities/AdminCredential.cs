namespace ModlistManager.Data.Entities;

/// <summary>Single-row table holding the admin login credential.</summary>
public class AdminCredential
{
    public int Id { get; set; }

    public string Username { get; set; } = "admin";

    public required string PasswordHash { get; set; }
}
