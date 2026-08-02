using FantasyWarrior.Core.Players;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.Nhl;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Backfills/refreshes career stats (season-by-season GP/G/A/PTS/PIM for
/// skaters, the goalie equivalent) from the NHL's player-landing endpoint —
/// the same one <see cref="DraftSyncJob"/> already calls, just reading a
/// different part of the payload.
///
/// Unlike draft details, a career row is not fetch-once-forever: the current
/// season's line keeps changing all year. <see
/// cref="Data.Entities.Player.CareerStatsSyncedUtc"/> is a timestamp, not a
/// one-time flag, so this re-visits whichever players are stalest rather than
/// only ones never checked — self-healing on a rolling basis instead of
/// needing a season-rollover special case.
/// </summary>
public sealed class CareerStatsSyncJob(NhlApiClient nhl, FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(int? limit = null, int maxAgeDays = 30, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
        var pending = await db.Players
            .Where(p => p.CareerStatsSyncedUtc == null || p.CareerStatsSyncedUtc < cutoff)
            .OrderBy(p => p.CareerStatsSyncedUtc ?? DateTime.MinValue)
            .Take(limit ?? int.MaxValue)
            .ToListAsync(ct);

        Console.WriteLine($"=== career-sync  {pending.Count} player(s) stale or never synced ===");
        if (pending.Count == 0)
        {
            Console.WriteLine("Nothing to do.");
            return 0;
        }

        int synced = 0, failed = 0;
        foreach (var (player, index) in pending.Select((p, i) => (p, i)))
        {
            var landing = await nhl.GetPlayerLandingAsync(player.PlayerId, ct);
            if (landing is null)
            {
                // Leave CareerStatsSyncedUtc alone so a later run retries —
                // a network blip must not look like "we checked, nothing there".
                failed++;
                continue;
            }

            var rows = landing.SeasonTotals
                .Where(s => s.GameTypeId == GameType.RegularSeason && NotableLeagues.IsNotable(s.LeagueAbbrev))
                .ToList();

            // Full replace, not a field-matched upsert: a player's career
            // history is a few dozen rows at most, and re-deriving the whole
            // set from the source of truth is simpler than reconciling adds/
            // removes/edits against whatever was there before (a stint that
            // NHL's own data later revises, e.g. a corrected team credit,
            // would otherwise leave the old row stranded).
            var existingRows = await db.PlayerCareerSeasonStats
                .Where(x => x.PlayerId == player.PlayerId)
                .ToListAsync(ct);
            db.PlayerCareerSeasonStats.RemoveRange(existingRows);

            db.PlayerCareerSeasonStats.AddRange(rows.Select(s => new PlayerCareerSeasonStat
            {
                PlayerId = player.PlayerId,
                Season = s.Season.ToString(),
                GameType = GameType.RegularSeason,
                LeagueAbbrev = s.LeagueAbbrev!,
                TeamName = s.TeamName?.Default,
                GamesPlayed = s.GamesPlayed,
                Goals = s.Goals,
                Assists = s.Assists,
                Points = s.Points,
                Pim = s.Pim,
                PlusMinus = s.PlusMinus,
                Wins = s.Wins,
                Losses = s.Losses,
                OtLosses = s.OtLosses,
                GoalsAgainst = s.GoalsAgainst,
                GoalsAgainstAvg = s.GoalsAgainstAvg,
                SavePctg = s.SavePctg,
                Shutouts = s.Shutouts,
            }));

            player.CareerStatsSyncedUtc = DateTime.UtcNow;
            synced++;

            if ((index + 1) % 20 == 0)
            {
                await db.SaveChangesAsync(ct);
                Console.WriteLine($"  {index + 1}/{pending.Count} — {synced} synced, {failed} failed");
            }
        }

        await db.SaveChangesAsync(ct);
        Console.WriteLine($"\n{synced} synced, {failed} left for a later run.");
        return 0;
    }
}
