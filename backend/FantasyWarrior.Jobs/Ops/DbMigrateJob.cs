using FantasyWarrior.Data;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Ops;

/// <summary>
/// Brings the database up to the latest migration.
///
/// **A command, never a startup hook.** Migrating on API start looks tidy and
/// is wrong here: Cloud Run scales to zero and can bring up several instances
/// at once, so a cold start after a deploy would race several processes into
/// the same schema change. Making it an explicit step also means a deploy that
/// needs a migration says so, instead of performing one silently.
/// </summary>
public sealed class DbMigrateJob
{
    public static async Task<int> RunAsync(bool listOnly, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<FantasyWarriorDbContext>()
            .UseSqlServer(DataServiceCollectionExtensions.ResolveConnectionString(),
                DataServiceCollectionExtensions.Configure)
            .Options;
        await using var db = new FantasyWarriorDbContext(options);

        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();

        Console.WriteLine($"Applied: {applied.Count}");
        foreach (var m in applied) Console.WriteLine($"  [x] {m}");
        Console.WriteLine($"Pending: {pending.Count}");
        foreach (var m in pending) Console.WriteLine($"  [ ] {m}");

        if (listOnly)
        {
            Console.WriteLine("\n(--list: nothing applied)");
            return 0;
        }
        if (pending.Count == 0)
        {
            Console.WriteLine("\nUp to date.");
            return 0;
        }

        Console.WriteLine($"\nApplying {pending.Count} migration(s)...");
        await db.Database.MigrateAsync(ct);
        Console.WriteLine("Done.");
        return 0;
    }
}
