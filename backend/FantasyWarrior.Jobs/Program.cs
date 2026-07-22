using FantasyWarrior.Jobs.Nhl;
using Google.Cloud.Firestore;

// Usage: dotnet run -- <job> [options]
//   player-sync [--season 20262027]
//   salary-import --file <path.csv>   (columns: nhlId?,firstName,lastName,teamAbbrev,capHit)
//   player-check
//
// Firestore target: set FIRESTORE_EMULATOR_HOST (e.g. localhost:8090) for local dev;
// otherwise GOOGLE_APPLICATION_CREDENTIALS + FIRESTORE_PROJECT_ID for production.

var job = args.FirstOrDefault();
if (job is null)
{
    Console.Error.WriteLine("Usage: FantasyWarrior.Jobs <player-sync> [options]");
    return 1;
}

var projectId = Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID")
    ?? (Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") is not null
        ? "demo-fantasy-warrior"
        : null);
if (projectId is null)
{
    Console.Error.WriteLine("Set FIRESTORE_PROJECT_ID (or FIRESTORE_EMULATOR_HOST for local dev).");
    return 1;
}

var db = await new FirestoreDbBuilder
{
    ProjectId = projectId,
    EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
}.BuildAsync();

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("FantasyWarrior/0.1");

switch (job)
{
    case "player-sync":
    {
        var season = GetOption(args, "--season") ?? CurrentSeason();
        await new PlayerSyncJob(new NhlApiClient(http), db).RunAsync(season);
        return 0;
    }
    case "salary-import":
    {
        var file = GetOption(args, "--file");
        if (file is null || !File.Exists(file))
        {
            Console.Error.WriteLine("Usage: salary-import --file <path.csv>");
            return 1;
        }
        await new FantasyWarrior.Jobs.Salaries.SalaryImportJob(db).RunAsync(file);
        return 0;
    }
    case "player-check":
    {
        var players = db.Collection("players");
        var count = (await players.Count().GetSnapshotAsync()).Count;
        var sample = await players.WhereEqualTo("teamAbbrev", "MTL").Limit(3).GetSnapshotAsync();
        Console.WriteLine($"players collection: {count} documents");
        foreach (var doc in sample.Documents)
        {
            var p = doc.ConvertTo<FantasyWarrior.Core.Players.Player>();
            Console.WriteLine($"  [{doc.Id}] {p.FirstName} {p.LastName} ({p.Position}, {p.TeamAbbrev}, {p.Status}) capHit={p.CapHit?.ToString() ?? "null"}");
        }
        return 0;
    }
    default:
        Console.Error.WriteLine($"Unknown job '{job}'.");
        return 1;
}

static string? GetOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// NHL seasons roll over in July (post-draft/free agency).
static string CurrentSeason()
{
    var today = DateTime.UtcNow;
    var startYear = today.Month >= 7 ? today.Year : today.Year - 1;
    return $"{startYear}{startYear + 1}";
}
