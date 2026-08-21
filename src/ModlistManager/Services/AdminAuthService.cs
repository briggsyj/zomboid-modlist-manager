using Microsoft.EntityFrameworkCore;
using ModlistManager.Data;
using ModlistManager.Data.Entities;

namespace ModlistManager.Services;

public class AdminAuthService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    private const string Username = "admin";

    /// <summary>
    /// Upserts the single admin credential row from the configured startup password.
    /// Runs on every startup so rotating the ADMIN_PASSWORD config/env var takes effect on restart.
    /// </summary>
    public async Task SeedAsync(string plainTextPassword)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        var credential = await db.AdminCredentials.FirstOrDefaultAsync();
        var hash = PasswordHasher.Hash(plainTextPassword);

        if (credential is null)
        {
            db.AdminCredentials.Add(new AdminCredential { Username = Username, PasswordHash = hash });
        }
        else
        {
            credential.PasswordHash = hash;
        }

        await db.SaveChangesAsync();
    }

    public async Task<bool> VerifyPasswordAsync(string plainTextPassword)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var credential = await db.AdminCredentials.FirstOrDefaultAsync();
        return credential is not null && PasswordHasher.Verify(plainTextPassword, credential.PasswordHash);
    }
}
