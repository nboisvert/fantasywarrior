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

    /// <summary>
    /// The connection string, from the first source that has one:
    /// <list type="number">
    /// <item>the <c>AZURE_SQL_CONNECTION</c> environment variable — what CI and
    /// Cloud Run set;</item>
    /// <item><c>appsettings.Local.json</c> next to the executable — the local
    /// dev file, holding real credentials in clear text and excluded by
    /// .gitignore (<c>appsettings.*.Local.json</c>). This repository is public,
    /// so a password committed here would be permanently readable by anyone;
    /// </item>
    /// <item><c>appsettings.json</c> — committed, and therefore only ever a
    /// placeholder.</item>
    /// </list>
    ///
    /// Read directly rather than through Microsoft.Extensions.Configuration so
    /// the console jobs and the API resolve it identically, with no host
    /// builder and no extra dependency.
    /// </summary>
    public static string ResolveConnectionString()
    {
        if (Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } fromEnv)
            return fromEnv;

        foreach (var file in (string[])["appsettings.Local.json", "appsettings.json"])
            if (ReadFromJson(file) is { } fromFile)
                return fromFile;

        throw new InvalidOperationException(
            $"No connection string. Set {ConnectionStringVariable}, or put one in "
            + "appsettings.Local.json as { \"ConnectionStrings\": { \"FantasyWarrior\": \"...\" } }.");
    }

    private static string? ReadFromJson(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("ConnectionStrings", out var section)) return null;
            if (!section.TryGetProperty("FantasyWarrior", out var value)) return null;
            var connection = value.GetString();
            // A placeholder is not a connection string; fall through to the next
            // source rather than failing with an unhelpful login error.
            return string.IsNullOrWhiteSpace(connection) || connection.Contains("{your_") ? null : connection;
        }
        catch (System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"  ! {fileName} is not valid JSON — ignoring it.");
            return null;
        }
    }

    /// <summary>
    /// A context for a console job, where there is no DI container. Same
    /// provider settings as the API so a job and a request never behave
    /// differently against the same database.
    /// </summary>
    public static FantasyWarriorDbContext CreateContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<FantasyWarriorDbContext>()
            .UseSqlServer(connectionString ?? ResolveConnectionString(), Configure)
            .Options;
        return new FantasyWarriorDbContext(options);
    }

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
