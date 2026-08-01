using System.Text.Json;
using FantasyWarrior.Core.Leagues;
using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Time;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Ops;

/// <summary>
/// Snapshots every number the Firestore model currently believes, so the SQL
/// rebuild can be checked against it rather than against a feeling.
///
/// This is the correctness oracle for the whole Azure SQL migration: after the
/// season is re-imported and replayed on SQL, each team's weekly active points
/// must match this file exactly. Anything else is a bug, not a rounding
/// difference — the same game lines scored under the same rules can only
/// produce one answer.
///
/// Deliberately a throwaway: it reads Firestore and is deleted with the rest
/// of the Firestore code at cutover. It writes a file, touches nothing.
/// </summary>
public sealed class DumpGoldenJob(FirestoreDb db)
{
    public async Task<int> RunAsync(string outputPath, CancellationToken ct = default)
    {
        var simSnap = await SimulationClock.Document(db).GetSnapshotAsync(ct);
        var simulation = simSnap.Exists ? simSnap.ConvertTo<SimulationState>() : null;

        var periods = (await db.Collection("periods").GetSnapshotAsync(ct)).Documents
            .Select(d => d.ConvertTo<Period>())
            .OrderBy(p => p.Season).ThenBy(p => p.Index)
            .Select(p => new
            {
                p.Season,
                p.Index,
                p.StartDate,
                p.EndDate,
                p.GameCount,
                lockUtc = p.LockUtc.ToDateTime(),
                finalizedUtc = p.FinalizedUtc?.ToDateTime(),
            })
            .ToList();

        var leagues = new List<object>();
        foreach (var leagueDoc in (await db.Collection("leagues").GetSnapshotAsync(ct)).Documents)
        {
            var league = leagueDoc.ConvertTo<League>();
            Console.WriteLine($"--- {league.Name} [{leagueDoc.Id}] season {league.Season}");

            // Every spot, open or closed. A closed spot still owns the weeks it
            // was held for, so leaving them out would silently lose history.
            var spots = (await leagueDoc.Reference.Collection("rosterSpots").GetSnapshotAsync(ct)).Documents
                .Select(d => (Id: d.Id, Spot: d.ConvertTo<RosterSpot>()))
                .OrderBy(s => s.Spot.TeamUsername).ThenBy(s => s.Spot.PlayerId)
                .ToList();

            var lineups = (await leagueDoc.Reference.Collection("lineups").GetSnapshotAsync(ct)).Documents
                .Select(d => d.ConvertTo<Lineup>())
                .OrderBy(l => l.TeamUsername).ThenBy(l => l.PeriodIndex)
                .ToList();

            var teams = (await leagueDoc.Reference.Collection("teams").GetSnapshotAsync(ct)).Documents
                .Select(d => d.ConvertTo<Team>())
                .OrderBy(t => t.OwnerUsername)
                .ToList();

            var trades = (await leagueDoc.Reference.Collection("trades").GetSnapshotAsync(ct)).Documents
                .Select(d => (Id: d.Id, Trade: d.ConvertTo<Trade>()))
                .OrderBy(t => t.Trade.CreatedUtc.ToDateTime())
                .ToList();

            Console.WriteLine($"    {teams.Count} teams, {spots.Count} spots, {lineups.Count} lineups, {trades.Count} trades");

            leagues.Add(new
            {
                id = leagueDoc.Id,
                league.Name,
                league.Season,
                league.CapAmount,
                league.CommissionerUsername,
                members = league.MemberUsernames.OrderBy(m => m),
                rules = new
                {
                    scale = league.RuleConfig.ScoringScale().OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value),
                    slots = new
                    {
                        forwards = league.RuleConfig.TopCount.Forwards,
                        defense = league.RuleConfig.TopCount.Defense,
                        goalies = league.RuleConfig.TopCount.Goalies,
                    },
                    rosterSize = new { league.RuleConfig.RosterSize.Min, league.RuleConfig.RosterSize.Max },
                },
                teams = teams.Select(t => new
                {
                    t.OwnerUsername,
                    t.Name,
                    t.FranchiseAbbrev,
                    // The numbers that must reproduce exactly.
                    t.Score,
                    t.FinalizedScore,
                    t.PeriodPoints,
                    t.BenchScore,
                    t.FinalizedThroughPeriodIndex,
                    t.CurrentPeriodIndex,
                    periodScores = t.PeriodScores.OrderBy(kv => int.Parse(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
                    t.CapTotal,
                    t.RosterGamesPlayed,
                    playerIds = t.PlayerIds.OrderBy(id => id),
                }),
                // Keyed by spot id so a lineup's activeSpotIds can be resolved
                // back to players when comparing against SQL, where the ids differ.
                rosterSpots = spots.Select(s => new
                {
                    id = s.Id,
                    s.Spot.PlayerId,
                    s.Spot.TeamUsername,
                    s.Spot.PositionGroup,
                    s.Spot.StartDate,
                    s.Spot.EndDate,
                    s.Spot.StartReason,
                    s.Spot.EndReason,
                    s.Spot.ActivePoints,
                    s.Spot.BenchPoints,
                    s.Spot.ActiveGamesPlayed,
                    s.Spot.FinalizedActivePoints,
                    s.Spot.FinalizedBenchPoints,
                }),
                lineups = lineups.Select(l => new
                {
                    l.TeamUsername,
                    l.PeriodIndex,
                    l.SetBy,
                    l.ActivePoints,
                    l.BenchPoints,
                    activeSpotIds = l.ActiveSpotIds.OrderBy(id => id),
                    // Per-player weekly results: the finest grain that has to
                    // reproduce, and the one that localises a discrepancy to a
                    // single player-week instead of a whole season.
                    results = l.Results.OrderBy(kv => kv.Key).ToDictionary(
                        kv => kv.Key,
                        kv => (object)new
                        {
                            kv.Value.PlayerId,
                            kv.Value.PositionGroup,
                            kv.Value.Points,
                            kv.Value.GamesPlayed,
                            kv.Value.FromDate,
                            kv.Value.ToDate,
                        }),
                }),
                trades = trades.Select(t => new
                {
                    id = t.Id,
                    t.Trade.ProposerUsername,
                    t.Trade.CounterpartyUsername,
                    t.Trade.PlayersFromProposer,
                    t.Trade.PlayersFromCounterparty,
                    t.Trade.Status,
                    createdUtc = t.Trade.CreatedUtc.ToDateTime(),
                    processedUtc = t.Trade.ProcessedUtc?.ToDateTime(),
                }),
            });
        }

        var payload = new
        {
            capturedUtc = DateTimeOffset.UtcNow,
            note = "Pre-Azure-SQL snapshot of every Firestore-computed number. "
                 + "After the SQL rebuild replays the season, weekly active points per team must match exactly.",
            simulation = simulation is null ? null : new { simulation.AsOfDate, simulation.Season, simulation.Enabled },
            counts = new
            {
                players = await CountAsync("players", ct),
                games = await CountAsync("games", ct),
                playerGameStats = await CountAsync("playerGameStats", ct),
                playerSeasonStats = await CountAsync("playerSeasonStats", ct),
                news = await CountAsync("news", ct),
                users = await CountAsync("users", ct),
            },
            periods,
            leagues,
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            // Keep it readable: this file gets diffed by a human when a number
            // disagrees.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await File.WriteAllTextAsync(outputPath, json, ct);

        Console.WriteLine($"\nWrote {outputPath} ({json.Length / 1024} KB)");
        return 0;
    }

    /// <summary>
    /// Count via an aggregation query — the whole point is to record how much
    /// reference data exists without paying a read per document to find out.
    /// </summary>
    private async Task<long> CountAsync(string collection, CancellationToken ct)
    {
        var result = await db.Collection(collection).Count().GetSnapshotAsync(ct);
        return result.Count ?? 0;
    }
}
