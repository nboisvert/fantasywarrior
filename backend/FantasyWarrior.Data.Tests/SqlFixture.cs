using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// Whether a SQL Server is reachable at all, probed once per test run.
///
/// Evaluated at test-discovery time rather than inside a fixture, because xunit
/// decides what to skip before any fixture has run. Without this, a machine
/// with no SQL Server (a Linux CI runner, a fresh checkout) would report a wall
/// of failures rather than an honest "skipped".
/// </summary>
public static class SqlAvailability
{
    private const string DefaultConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=FantasyWarriorTests;Trusted_Connection=True;TrustServerCertificate=True;";

    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("FW_TEST_SQL_CONNECTION") ?? DefaultConnection;

    public static string? SkipReason { get; } = Probe();

    private static string? Probe()
    {
        try
        {
            var master = new SqlConnectionStringBuilder(ConnectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 30,
            }.ConnectionString;
            using var connection = new SqlConnection(master);
            connection.Open();
            return null;
        }
        catch (Exception ex)
        {
            return $"No SQL Server reachable ({ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}). "
                 + "Install LocalDB or set FW_TEST_SQL_CONNECTION.";
        }
    }
}

/// <summary>A test that needs the real database. Skips cleanly when there isn't one.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (SqlAvailability.SkipReason is { } reason) Skip = reason;
    }
}

/// <summary>
/// A real SQL Server database, migrated once for the whole test run.
///
/// **Deliberately not an in-memory or SQLite provider.** Everything worth
/// testing here is something only SQL Server does: a filtered unique index, a
/// persisted computed column, three CHECK constraints and four views written in
/// T-SQL. A fake provider would pass while the production database rejected the
/// same schema — the exact failure these tests exist to catch.
/// </summary>
public sealed class SqlFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (SqlAvailability.SkipReason is not null) return;
        await using var db = NewContext();
        // From scratch: these tests assert on totals, so leftovers from a
        // previous run would make them lie.
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (SqlAvailability.SkipReason is not null) return;
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    public static FantasyWarriorDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<FantasyWarriorDbContext>()
            .UseSqlServer(SqlAvailability.ConnectionString)
            .Options;
        return new FantasyWarriorDbContext(options);
    }
}

[CollectionDefinition(Name)]
public sealed class SqlCollection : ICollectionFixture<SqlFixture>
{
    public const string Name = "sql";
}
