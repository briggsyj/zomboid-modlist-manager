using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ModlistManager.Data;

namespace ModlistManager.Tests;

/// <summary>
/// Keeps a single open SQLite ":memory:" connection alive for the lifetime of a test so multiple
/// short-lived DbContext instances (as the app creates via IDbContextFactory) all see the same data.
/// </summary>
public sealed class SqliteInMemoryDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteInMemoryDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose() => _connection.Dispose();
}
