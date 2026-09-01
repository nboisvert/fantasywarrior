using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Time;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Writes one <see cref="TeamStandingsSnapshot"/> per team, per league, for
/// last night — the standings rank at that moment and what the active
/// roster scored specifically that day. Runs last in the nightly chain
/// (after scoring, banking and week-ahead prep), so it reads a night that
/// is fully settled.
///
/// This is the one place in the app that stores a fact "as of a past night"
/// rather than deriving it live — see the entity's own doc comment for why
/// rank movement needs that.
/// </summary>
public sealed class StandingsSnapshotJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        int? onlyLeagueId, bool dryRun, DateTimeOffset? nowOverride = null, CancellationToken ct = default)
    {
        var now = nowOverride ?? await new SimulationClockService(db).NowAsync(ct);
        var lastStatDate = PoolClock.LastStatDate(now);

        var leagues = await db.Leagues
            .Where(l => onlyLeagueId == null || l.LeagueId == onlyLeagueId)
            .ToListAsync(ct);

        foreach (var league in leagues)
        {
            try
            {
                await SnapshotLeagueAsync(league, lastStatDate, dryRun, ct);
            }
            catch (RuleSetUnavailableException ex)
            {
                // Same posture as PeriodRollupJob: one misconfigured league
                // must not stop every other league's snapshot.
                Console.Error.WriteLine($"    {league.Name}: cannot be scored — {ex.Message}");
            }
        }
        return 0;
    }

    private async Task SnapshotLeagueAsync(League league, DateOnly lastStatDate, bool dryRun, CancellationToken ct)
    {
        var teams = await db.Teams
            .Where(t => t.LeagueId == league.LeagueId)
            .Select(t => new { t.TeamId, t.Name })
            .ToListAsync(ct);
        if (teams.Count == 0) return;

        // Same ordering LeagueEndpoints.cs uses to render the live table —
        // the rank this snapshot records must agree with what a GM sees.
        var scoreByTeam = await db.Standings
            .Where(s => s.LeagueId == league.LeagueId)
            .ToDictionaryAsync(s => s.TeamId, s => s.Score, ct);
        var ranked = teams
            .OrderByDescending(t => scoreByTeam.GetValueOrDefault(t.TeamId))
            .ThenBy(t => t.Name)
            .Select((t, i) => (t.TeamId, Rank: i + 1))
            .ToList();

        var period = await db.Periods
            .Where(p => p.Season == league.Season && p.StartDate <= lastStatDate && p.EndDate >= lastStatDate)
            .FirstOrDefaultAsync(ct);

        // No week covers last night (season not started, or between
        // seasons) — nothing was played, so there is nothing to score. Rank
        // is still worth recording for the movement pill.
        var pointsByTeam = new Dictionary<int, double>();
        if (period is not null)
        {
            var rules = await RuleSetResolver.ForScoringAsync(db, league, ct);
            var scale = rules.Scoring.Values;

            var spots = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId
                            && s.StartDate <= lastStatDate
                            && (s.EndDate == null || s.EndDate >= lastStatDate))
                .ToListAsync(ct);
            var spotIds = spots.Select(s => s.RosterSpotId).ToList();

            // The lineup decision for this week already exists — written by
            // PeriodRollupJob earlier tonight. Read it; never recompute it.
            var assignments = await db.RosterAssignments
                .Where(a => a.PeriodId == period.PeriodId && spotIds.Contains(a.RosterSpotId))
                .ToDictionaryAsync(a => a.RosterSpotId, ct);
            var activeSpots = spots
                .Where(s => assignments.TryGetValue(s.RosterSpotId, out var a) && a.IsActive)
                .ToList();

            var playerIds = activeSpots.Where(s => s.PlayerId != null)
                .Select(s => s.PlayerId!.Value).Distinct().ToList();
            var lines = await db.PlayerGameStats
                .Where(l => l.GameDate == lastStatDate && l.Season == league.Season
                            && l.GameType == GameType.RegularSeason && playerIds.Contains(l.PlayerId))
                .ToListAsync(ct);
            var byPlayer = lines.ToLookup(l => l.PlayerId);

            var franchiseActive = activeSpots.Any(s => s.IsFranchise);
            var games = !franchiseActive
                ? []
                : (await db.Games
                    .Where(g => g.GameDate == lastStatDate && g.Season == league.Season
                                && g.GameType == GameType.RegularSeason)
                    .Select(g => new GameResult(
                        g.HomeTeamAbbrev, g.AwayTeamAbbrev, g.HomeScore, g.AwayScore, g.LastPeriodType))
                    .ToListAsync(ct));

            foreach (var spot in activeSpots)
            {
                var points = spot.IsFranchise
                    ? FranchiseResults.For(spot.FranchiseAbbrev!, games).Score(scale)
                    : StatLine.Sum(byPlayer[spot.PlayerId!.Value].Select(StatColumns.ToStatLine)).Score(scale);
                pointsByTeam[spot.TeamId] = pointsByTeam.GetValueOrDefault(spot.TeamId) + points;
            }
        }

        var teamIds = teams.Select(t => t.TeamId).ToList();
        var existingSnapshots = await db.TeamStandingsSnapshots
            .Where(x => teamIds.Contains(x.TeamId) && x.AsOfDate == lastStatDate)
            .ToDictionaryAsync(x => x.TeamId, ct);

        foreach (var (teamId, rank) in ranked)
        {
            var points = pointsByTeam.GetValueOrDefault(teamId);
            if (existingSnapshots.TryGetValue(teamId, out var row))
            {
                row.Rank = rank;
                row.LastNightPoints = points;
            }
            else
            {
                db.TeamStandingsSnapshots.Add(new TeamStandingsSnapshot
                {
                    TeamId = teamId,
                    AsOfDate = lastStatDate,
                    Rank = rank,
                    LastNightPoints = points,
                    CreatedUtc = DateTime.UtcNow,
                });
            }
        }

        Console.WriteLine($"    {league.Name}: {ranked.Count} team(s) snapshotted for {lastStatDate:yyyy-MM-dd}"
            + (period is null ? " (rank only, no week covers that day)" : ""));

        if (!dryRun) await db.SaveChangesAsync(ct);
    }
}
