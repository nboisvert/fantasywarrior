using FantasyWarrior.Core.Cockcoin;
using FantasyWarrior.Core.Lineups;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Prepares the week ahead: materializes its lineup, and retires the trades
/// that have reached it.
///
/// <b>This used to be ProcessTradesJob, and it no longer executes trades.</b>
/// Accepting one does that, dated forward — see <see cref="TradeExecution"/>.
/// What is left here is the other half of the same idea: if a GM is to set next
/// week's lineup with the roster he will actually have, next week's lineup has
/// to exist.
///
/// It did not, before. The lineup endpoint computed a preview of what the
/// carry-forward *would* pick and never wrote it, so the rows only appeared
/// when the scoring pass reached the week — by which time it was locked. That
/// preview is gone: the rows below are the lineup, and a trade edits them like
/// anything else.
///
/// <b>Existing rows are never rewritten.</b> That is the whole of the
/// forgotten-lineup rule: a week is filled in once, from the week before it,
/// and after that it belongs to the GM. Re-running any number of times changes
/// nothing, which is what makes a nightly job safe to replay.
/// </summary>
public sealed class WeekAheadJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(DateOnly today, CancellationToken ct = default)
    {
        var landed = await LandDueTradesAsync(today, ct);
        var filled = await MaterializeNextWeekAsync(today, ct);
        return landed + filled;
    }

    /// <summary>
    /// Marks accepted trades as processed once their effective date arrives —
    /// the moment the swap stops being a promise and becomes the roster.
    ///
    /// Nothing about the rosters happens here; they changed at acceptance. The
    /// status is what the rest of the app reads to know a trade is history:
    /// it is what opens the vote, and what keeps <c>vStandings</c>' engaged
    /// columns from describing a trade that has already landed.
    ///
    /// A trade with no effective date at all was accepted before acceptance
    /// started dating them. It is applied here rather than left to rot, so a
    /// database that predates this job comes forward on its own.
    /// </summary>
    private async Task<int> LandDueTradesAsync(DateOnly today, CancellationToken ct)
    {
        var accepted = await db.Trades
            .Where(t => t.Status == TradeStatus.Accepted)
            .Include(t => t.Assets)
            .ToListAsync(ct);

        if (accepted.Count == 0)
        {
            Console.WriteLine("  No trades waiting.");
            return 0;
        }

        var done = 0;
        foreach (var trade in accepted.Where(t => t.EffectiveDate == null))
        {
            var league = await db.Leagues.FirstAsync(l => l.LeagueId == trade.LeagueId, ct);
            var next = await db.Periods
                .Where(p => p.Season == league.Season && p.StartDate > today)
                .OrderBy(p => p.StartDate)
                .FirstOrDefaultAsync(ct);
            if (next is null)
            {
                Console.Error.WriteLine(
                    $"  ! trade {trade.TradeId} accepted with no date and no week left to land in.");
                continue;
            }

            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    trade.EffectiveDate = next.StartDate;
                    await TradeExecution.ApplyAsync(db, trade, next.StartDate, next, ct);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                    Console.WriteLine($"  trade {trade.TradeId}: applied late, effective {next.StartDate:yyyy-MM-dd}");
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    // One bad trade must not stop the rest of the night.
                    Console.Error.WriteLine($"  ! trade {trade.TradeId} failed, rolled back: {ex.Message}");
                    db.ChangeTracker.Clear();
                }
            });
            done++;
        }

        var due = accepted.Where(t => t.EffectiveDate is { } d && d <= today).ToList();

        // Both GMs' owners, looked up once for every trade landing tonight —
        // the "done deal" bonus is flat and goes to both sides.
        var dueTeamIds = due.SelectMany(t => new[] { t.ProposerTeamId, t.CounterpartyTeamId }).Distinct().ToList();
        var owners = dueTeamIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.Teams.Where(t => dueTeamIds.Contains(t.TeamId))
                .ToDictionaryAsync(t => t.TeamId, t => t.OwnerUserId, ct);

        foreach (var trade in due)
        {
            trade.Status = TradeStatus.Processed;
            trade.ProcessedUtc = DateTime.UtcNow;
            var players = trade.Assets.Count(a => a.AssetType == TradeAssetType.Player);
            var picks = trade.Assets.Count(a => a.AssetType == TradeAssetType.DraftPick);
            var franchises = trade.Assets.Count(a => a.AssetType == TradeAssetType.Franchise);
            Console.WriteLine($"  trade {trade.TradeId}: {players} player(s), {picks} pick(s)"
                + (franchises == 0 ? "" : $", {franchises} franchise(s)") + " — landed");

            foreach (var teamId in new[] { trade.ProposerTeamId, trade.CounterpartyTeamId })
                db.CockcoinAwards.Add(new CockcoinAward
                {
                    UserId = owners[teamId],
                    Amount = CockcoinReasons.DoneDealAmount,
                    Reason = CockcoinReasons.DoneDeal,
                    AwardedUtc = DateTime.UtcNow,
                });
        }
        if (due.Count > 0) await db.SaveChangesAsync(ct);

        return done + due.Count;
    }

    /// <summary>
    /// Gives every spot that will own next week a lineup row, carried forward
    /// from the week in progress and topped up to the slot limits.
    ///
    /// Per league, because the slot configuration is a league rule. Skipped
    /// entirely once the rows exist, which is the usual case after the first
    /// night of a week.
    /// </summary>
    private async Task<int> MaterializeNextWeekAsync(DateOnly today, CancellationToken ct)
    {
        var written = 0;
        foreach (var league in await db.Leagues.ToListAsync(ct))
        {
            var periods = await db.Periods
                .Where(p => p.Season == league.Season)
                .OrderBy(p => p.StartDate)
                .ToListAsync(ct);

            var next = periods.FirstOrDefault(p => p.StartDate > today);
            if (next is null) continue;
            // The week in progress, whose active set is what carries forward.
            // Null before the season opens, which is fine: there is nothing to
            // carry and AutoFill picks the whole lineup on merit.
            var current = periods.LastOrDefault(p => p.StartDate <= today);

            var spots = await db.RosterSpots
                .Where(s => s.LeagueId == league.LeagueId)
                .Where(RosterWindow.OwnsPeriod(next.StartDate, next.EndDate))
                .ToListAsync(ct);
            if (spots.Count == 0) continue;

            var spotIds = spots.Select(s => s.RosterSpotId).ToList();
            var existing = await db.RosterAssignments
                .Where(a => a.PeriodId == next.PeriodId && spotIds.Contains(a.RosterSpotId))
                .Select(a => a.RosterSpotId)
                .ToListAsync(ct);
            var missing = spots.Where(s => !existing.Contains(s.RosterSpotId)).ToList();
            if (missing.Count == 0) continue;

            // The season being played, for the same reason the rollup reads it:
            // this writes next week's lineup inside the current season.
            RuleSet rules;
            try
            {
                rules = await RuleSetResolver.ForScoringAsync(db, league, ct);
            }
            catch (RuleSetUnavailableException ex)
            {
                Console.Error.WriteLine($"  {league.Name}: no lineup written — {ex.Message}");
                continue;
            }

            // lineup.onMissing is deliberately not read here: ScoreZero is
            // listed in RuleSetCapabilities as unenforced, and skipping the
            // carry-forward is only half of it — the rollup would still have to
            // decide what an unwritten week means. Half an implementation is
            // worse than a badge that says so.

            var previousActive = current is null
                ? []
                : await db.RosterAssignments
                    .Where(a => a.PeriodId == current.PeriodId && a.IsActive && spotIds.Contains(a.RosterSpotId))
                    .Select(a => a.RosterSpotId)
                    .ToListAsync(ct);

            var totals = await db.RosterSpotTotals
                .Where(v => v.LeagueId == league.LeagueId)
                .ToDictionaryAsync(v => v.RosterSpotId, v => v.ActivePoints, ct);

            // Per team: the slot limits are a team's, and one roster's
            // carry-forward must never be topped up with another's players.
            foreach (var team in spots.GroupBy(s => s.TeamId))
            {
                var candidates = team
                    .Select(s => new LineupCandidate(
                        s.RosterSpotId.ToString(), s.PlayerId ?? 0, s.PositionGroup,
                        totals.GetValueOrDefault(s.RosterSpotId), s.OpenedUtc))
                    .ToList();

                var active = LineupRules.CarryForward(
                    candidates,
                    [.. previousActive.Where(id => team.Any(s => s.RosterSpotId == id)).Select(id => id.ToString())],
                    new LineupSlots(
                        rules.Lineup.Slots.Forwards, rules.Lineup.Slots.Defense, rules.Lineup.Slots.Goalies));

                foreach (var spot in team.Where(s => missing.Contains(s)))
                {
                    db.RosterAssignments.Add(new RosterAssignment
                    {
                        RosterSpotId = spot.RosterSpotId,
                        PeriodId = next.PeriodId,
                        // The Équipe slot is active whatever the carry-forward
                        // says: one franchise, one seat, no decision to make.
                        IsActive = spot.IsFranchise || active.Contains(spot.RosterSpotId.ToString()),
                        // A spot opening mid-week owns only part of it.
                        EffectiveFrom = spot.StartDate > next.StartDate ? spot.StartDate : next.StartDate,
                        EffectiveTo = next.EndDate,
                        ScoredUtc = DateTime.UtcNow,
                    });
                    written++;
                }
            }

            // Attributed to the job, not to a GM. This is the flag the lineup
            // screen reads to say "we picked this for you" — and the one the
            // PUT overwrites the moment he picks for himself.
            foreach (var team in missing.Select(s => s.TeamId).Distinct())
            {
                var lineup = await db.TeamPeriodLineups
                    .FirstOrDefaultAsync(l => l.TeamId == team && l.PeriodId == next.PeriodId, ct);
                if (lineup is null)
                    db.TeamPeriodLineups.Add(new TeamPeriodLineup
                    {
                        TeamId = team, PeriodId = next.PeriodId,
                        SetBy = LineupSetBy.Auto, SubmittedUtc = DateTime.UtcNow,
                    });
            }
        }

        if (written > 0) await db.SaveChangesAsync(ct);
        Console.WriteLine(written == 0
            ? "  Next week's lineups are already in place."
            : $"  {written} lineup row(s) carried into next week.");
        return written;
    }
}
