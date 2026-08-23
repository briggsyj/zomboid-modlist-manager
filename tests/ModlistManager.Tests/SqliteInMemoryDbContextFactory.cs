using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ModlistManager.Data;

namespace ModlistManager.Tests;

/// <summary>
/// An isolated in-memory database per test, behind the same IDbContextFactory the app uses.
///
/// Each DbContext gets its own connection to a uniquely named shared-cache database, rather than all
/// sharing one <c>:memory:</c> connection. That matters because a SqliteConnection is not safe for
/// concurrent use, and tests that exercise the background fetch service read from the test thread
/// while the service writes from a worker - which raced, intermittently reading a row before its
/// related rows were visible. A connection each lets SQLite do the locking.
///
/// The keep-alive connection exists only to stop the database being dropped: a shared-cache in-memory
/// database lives exactly as long as at least one connection to it is open.
/// </summary>
public sealed class SqliteInMemoryDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteInMemoryDbContextFactory()
    {
        var connectionString = $"Data Source=file:memdb-{Guid.NewGuid():N}?mode=memory&cache=shared";

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connectionString)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose() => _keepAlive.Dispose();
}
