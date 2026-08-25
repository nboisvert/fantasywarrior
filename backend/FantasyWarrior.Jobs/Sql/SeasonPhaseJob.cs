using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Moves one league's active <see cref="LeagueSeason"/> row forward one phase,
/// with the side effects each step carries — see <c>season-lifecycle.md</c> §5.
///
/// **Deliberately not run on a cron.** Every other job in this file reacts to a
/// clock (a night passing, a date reached); this one reacts to a commissioner's
/// decision — "the protection window is closed", "the draft is done" — that no
/// calendar can make for him. It is a command, run by hand, the same posture as
/// <c>draft-picks-init</c>.
/// </summary>
public sealed class SeasonPhaseJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string? leagueCode, string? toPhase, bool dryRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leagueCode))
        {
            Console.Error.WriteLine("season-phase needs --league <joinCode>.");
            return 1;
        }

        if (!Enum.TryParse<LeagueSeasonPhase>(toPhase, ignoreCase: true, out var target))
        {
            Console.Error.WriteLine(
                $"--to must be one of: {string.Join(", ", Enum.GetNames<LeagueSeasonPhase>())}.");
            return 1;
        }

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode, ct);
        if (league is null)
        {
            Console.Error.WriteLine($"No league with join code {leagueCode}.");
            return 1;
        }

        var active = await db.LeagueSeasons
            .Where(s => s.LeagueId == league.LeagueId && s.Phase != LeagueSeasonPhase.Complete)
            .FirstOrDefaultAsync(ct);

        Console.WriteLine($"=== season-phase  {league.Name} ===");

        // No open row at all — the previous season closed and nothing has been
        // opened for the next one. The only legal move from here is starting a
        // brand new row at Preparing; anything else has no season to apply to.
        if (active is null)
        {
            if (target != LeagueSeasonPhase.Preparing)
            {
                Console.Error.WriteLine(
                    $"{league.Name} has no open season. The only next step is --to Preparing, to open one.");
                return 1;
            }

            var last = await db.LeagueSeasons
                .Where(s => s.LeagueId == league.LeagueId)
                .OrderByDescending(s => s.Number)
                .FirstOrDefaultAsync(ct);
            if (last is null)
            {
                Console.Error.WriteLine($"{league.Name} has no season history at all — nothing to build the next one from.");
                return 1;
            }

            var nextSeason = Season.Next(last.Season);
            Console.WriteLine($"  Opening season {last.Number + 1} ({Season.Display(nextSeason)}), Preparing.");
            if (dryRun) return 0;

            db.LeagueSeasons.Add(new LeagueSeason
            {
                LeagueId = league.LeagueId,
                Season = nextSeason,
                Number = last.Number + 1,
                Phase = LeagueSeasonPhase.Preparing,
                StartedUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return 0;
        }

        if (!SeasonPhaseRules.CanTransition(active.Phase, target))
        {
            Console.Error.WriteLine(
                $"{league.Name} season {active.Number} is {active.Phase}. "
                + $"The only legal next step is {SeasonPhaseRules.Next(active.Phase)?.ToString() ?? "none — it is already Complete"}.");
            return 1;
        }

        Console.WriteLine($"  Season {active.Number} ({Season.Display(active.Season)}): {active.Phase} -> {target}");
        if (dryRun) return 0;

        active.Phase = target;

        if (target == LeagueSeasonPhase.InSeason)
        {
            // The moment the standings flip to this season — every screen that
            // reads League.Season has shown the season that just closed right up
            // until this line.
            league.Season = active.Season;
            await new ProtectionResetJob(db).RunAsync(leagueCode, dryRun: false, ct);
            Console.WriteLine($"  League.Season -> {active.Season}. Protections cleared.");
        }

        if (target == LeagueSeasonPhase.Complete)
        {
            var champion = await db.Standings
                .Where(s => s.LeagueId == league.LeagueId)
                .OrderByDescending(s => s.Score)
                .Select(s => (int?)s.TeamId)
                .FirstOrDefaultAsync(ct);
            active.ChampionTeamId = champion;
            active.CompletedUtc = DateTime.UtcNow;
            Console.WriteLine($"  Champion: team {champion?.ToString() ?? "none (no teams)"}.");
        }

        await db.SaveChangesAsync(ct);
        return 0;
    }
}
