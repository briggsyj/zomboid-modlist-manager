using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModlistManager.Data;

/// <summary>
/// Lets `dotnet ef migrations` create an AppDbContext without running the full app
/// (startup auth checks, hosted services, required ADMIN_PASSWORD, etc). Only used at design time.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=modlist.db");
        return new AppDbContext(optionsBuilder.Options);
    }
}
