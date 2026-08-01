using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FantasyWarrior.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Environment variable holding the Azure SQL connection string. The same
    /// name is used by the API, the jobs and the GitHub Actions workflows, so
    /// there is one thing to rotate.
    /// </summary>
    public const string ConnectionStringVariable = "AZURE_SQL_CONNECTION";

    public static string ResolveConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable)
        ?? throw new InvalidOperationException(
            $"Set {ConnectionStringVariable} to the Azure SQL connection string.");

    public static IServiceCollection AddFantasyWarriorData(
        this IServiceCollection services, string? connectionString = null)
    {
        var cs = connectionString ?? ResolveConnectionString();
        services.AddDbContext<FantasyWarriorDbContext>(o => o.UseSqlServer(cs, Configure));
        return services;
    }

    /// <summary>
    /// The provider settings that matter on the Azure SQL free tier, where the
    /// database auto-pauses after an idle hour.
    ///
    /// Resuming takes tens of seconds and the first connection attempt during it
    /// fails with a transient error, so retries are not optional here — without
    /// them the first request after a quiet night simply errors. The long
    /// command timeout covers the same wake-up.
    /// </summary>
    public static void Configure(Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder sql)
    {
        sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(20), errorNumbersToAdd: null);
        sql.CommandTimeout(60);
    }
}
