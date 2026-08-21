using Microsoft.EntityFrameworkCore;
using ModlistManager.Data.Entities;

namespace ModlistManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ModRequest> ModRequests => Set<ModRequest>();

    public DbSet<ModRequestModId> ModRequestModIds => Set<ModRequestModId>();

    public DbSet<AdminCredential> AdminCredentials => Set<AdminCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModRequest>(entity =>
        {
            entity.Property(r => r.Status).HasConversion<string>();
            entity.Property(r => r.FetchStatus).HasConversion<string>();
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.Game);
            entity.HasIndex(r => r.RequesterName);

            entity.HasMany(r => r.ModIds)
                .WithOne(m => m.ModRequest)
                .HasForeignKey(m => m.ModRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
