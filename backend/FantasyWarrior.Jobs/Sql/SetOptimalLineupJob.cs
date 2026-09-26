using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Sets one period's active lineup for every team in a league to the best
/// available under the league's own scoring scale, ranked on a prior season's
/// totals -- for opening a season nobody has actually set a lineup for yet,
/// rather than leaving week 1 on whatever active/reserve split a roster
/// import happened to carry (a real fact about a past week, not a claim about
/// which nine forwards are actually the team's best).
///
/// Écurie (Équipe/`T`) spots are untouched -- they carry no lineup choice, see
/// <see cref="SeedMordusJob"/>'s opening lineup.
/// </summary>
public sealed class SetOptimalLineupJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        string leagueCode, int periodNumber, string statsSeason, bool dryRun, CancellationToken ct = default)
    {
        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode, ct);
        if (league is null) { Console.Error.WriteLine($"No league with join code {leagueCode}."); return 1; }

        var activeSeason = await db.LeagueSeasons
            .Where(s => s.LeagueId == league.LeagueId && s.Phase != LeagueSeasonPhase.Complete)
            .FirstOrDefaultAsync(ct);
        if (activeSeason is null) { Console.Error.WriteLine($"{league.Name} has no open season."); return 1; }
        if (activeSeason.Rules.IsUnwritten) { Console.Error.WriteLine($"{league.Name}'s rules are unwritten."); return 1; }
        var slots = activeSeason.Rules.Lineup.Slots;

        var period = await db.Periods
            .FirstOrDefaultAsync(p => p.Season == league.Season && p.Number == periodNumber, ct);
        if (period is null) { Console.Error.WriteLine($"No period {periodNumber} for season {league.Season}."); return 1; }

        Console.WriteLine($"=== set-optimal-lineup{(dryRun ? "  [DRY RUN]" : "")}  {league.Name}, "
            + $"week {periodNumber} ({period.StartDate:yyyy-MM-dd}), ranked on {statsSeason} ===");
        Console.WriteLine($"Slots: {slots.Forwards}F / {slots.Defense}D / {slots.Goalies}G\n");

        var spots = await db.RosterSpots
            .Where(s => s.LeagueId == league.LeagueId && s.EndDate == null
                && s.PlayerId != null && s.PositionGroup != "T")
            .Select(s => new { s.RosterSpotId, s.TeamId, s.PlayerId, s.PositionGroup })
            .ToListAsync(ct);

        var playerIds = spots.Select(s => s.PlayerId!.Value).Distinct().ToList();
        var totals = await SeasonTotalsQuery.ForAsync(db, statsSeason, playerIds, asOf: null, ct);
        var players = await db.Players.Where(p => playerIds.Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, p => $"{p.FirstName} {p.LastName}", ct);

        var scaleByGroup = new[] { "F", "D", "G" }.ToDictionary(g => g, activeSeason.Rules.Scoring.ScaleFor);
        double ScoreOf(long playerId, string group) =>
            totals.TryGetValue(playerId, out var t) ? StatColumns.ToStatLine(t).Score(scaleByGroup[group]) : 0;

        var teams = await db.Teams.Where(t => t.LeagueId == league.LeagueId)
            .Select(t => new { t.TeamId, t.Name, Username = t.Owner!.Username }).ToListAsync(ct);

        var existing = (await db.RosterAssignments
            .Where(a => a.PeriodId == period.PeriodId && a.RosterSpot!.LeagueId == league.LeagueId)
            .ToListAsync(ct))
            .ToDictionary(a => a.RosterSpotId);

        var activated = 0;
        var benched = 0;
        var missingAssignment = 0;

        foreach (var team in teams)
        {
            var mine = spots.Where(s => s.TeamId == team.TeamId).ToList();
            var chosen = new HashSet<int>();
            foreach (var (group, take) in new[] { ("F", slots.Forwards), ("D", slots.Defense), ("G", slots.Goalies) })
            {
                var best = mine.Where(s => s.PositionGroup == group)
                    .Select(s => (s.RosterSpotId, s.PlayerId, Score: ScoreOf(s.PlayerId!.Value, group)))
                    .OrderByDescending(x => x.Score).ThenBy(x => x.RosterSpotId)
                    .Take(take);
                foreach (var b in best) chosen.Add(b.RosterSpotId);
            }

            Console.WriteLine($"  {team.Username,-12} {team.Name,-24} {chosen.Count,2} active of {mine.Count}");
            foreach (var s in mine)
            {
                var isActive = chosen.Contains(s.RosterSpotId);
                if (isActive) activated++; else benched++;

                if (!existing.TryGetValue(s.RosterSpotId, out var assignment))
                {
                    missingAssignment++;
                    if (dryRun) continue;
                    assignment = new RosterAssignment
                    {
                        RosterSpotId = s.RosterSpotId,
                        PeriodId = period.PeriodId,
                        EffectiveFrom = period.StartDate,
                        EffectiveTo = period.EndDate,
                        ScoredUtc = DateTime.UtcNow,
                    };
                    db.RosterAssignments.Add(assignment);
                }
                assignment.IsActive = isActive;

                if (isActive)
                    Console.WriteLine($"      {s.PositionGroup}  {players.GetValueOrDefault(s.PlayerId!.Value, "?"),-22} "
                        + $"{ScoreOf(s.PlayerId!.Value, s.PositionGroup!),6:0.0} pts ({statsSeason})");
            }
        }

        Console.WriteLine($"\n{activated} active, {benched} benched across {teams.Count} teams.");
        if (missingAssignment > 0)
            Console.WriteLine($"{missingAssignment} spot(s) had no RosterAssignment yet for this period -- created.");

        if (dryRun) { Console.WriteLine("[DRY RUN] Nothing written."); return 0; }
        await db.SaveChangesAsync(ct);
        return 0;
    }
}
