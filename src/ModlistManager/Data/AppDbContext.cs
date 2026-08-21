using Microsoft.EntityFrameworkCore;
using ModlistManager.Data.Entities;

namespace ModlistManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Mod> Mods => Set<Mod>();

    public DbSet<PzModId> PzModIds => Set<PzModId>();

    public DbSet<ModRequest> ModRequests => Set<ModRequest>();

    public DbSet<AdminCredential> AdminCredentials => Set<AdminCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Mod>(entity =>
        {
            entity.Property(m => m.FetchStatus).HasConversion<string>();
            entity.HasIndex(m => new { m.Game, m.WorkshopId }).IsUnique();

            // Default true so mods added before this column existed stay in the Mods= export
            // after upgrading, rather than all silently going inactive.
            entity.Property(m => m.IsActive).HasDefaultValue(true);

            entity.HasMany(m => m.PzModIds)
                .WithOne(p => p.Mod)
                .HasForeignKey(p => p.ModId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(m => m.Requests)
                .WithOne(r => r.Mod)
                .HasForeignKey(r => r.ModId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModRequest>(entity =>
        {
            entity.Property(r => r.Status).HasConversion<string>();
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.RequesterName);
        });
    }
}
