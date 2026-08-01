using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FantasyWarrior.Data;

/// <summary>
/// Lets <c>dotnet ef migrations</c> build the model without starting the app.
///
/// The fallback connection string is deliberate: generating and reviewing a
/// migration should not require a live database, or a credentialed machine, or
/// a network. It is never used to connect — only to pick the provider.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FantasyWarriorDbContext>
{
    public FantasyWarriorDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable(DataServiceCollectionExtensions.ConnectionStringVariable)
                 ?? "Server=(localdb)\\mssqllocaldb;Database=FantasyWarriorDesignTime;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<FantasyWarriorDbContext>()
            .UseSqlServer(cs, DataServiceCollectionExtensions.Configure)
            .Options;
        return new FantasyWarriorDbContext(options);
    }
}
