using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Rosters;

/// <summary>
/// Applies an agreed trade to the rosters, dated to a future week boundary.
///
/// <b>Accepting is executing now.</b> This used to run from the nightly job on
/// whichever night a week happened to bank, which meant that between agreeing
/// and executing there was a window where the app still believed the old
/// rosters — and that window is exactly when a GM sets the lineup for the week
/// the trade lands in. He was offered a player who would be gone and denied the
/// one who would have arrived.
///
/// Nothing about the *effect* moved earlier: <paramref name="effectiveDate"/>
/// is still the start of a week, the spots still change hands on that day, and
/// a player still never owns two teams on the same day. What moved earlier is
/// only when the database is told.
///
/// <b>The lineup rows move with the spots.</b> A spot leaving before the next
/// week loses its assignment for it, and an arriving spot gains one — inactive,
/// because choosing to field him is the GM's call and activating him here would
/// silently bench someone the GM did chose.
/// </summary>
public static class TradeExecution
{
    /// <summary>
    /// Closes the outgoing spots, opens the incoming ones, hands over the
    /// picks, and squares the next week's lineup rows with the result.
    ///
    /// Does not save; the caller owns the transaction, because on one side of
    /// this the API also has validation to commit atomically with it and on the
    /// other the nightly job runs several trades in a row.
    /// </summary>
    /// <param name="nextPeriod">
    /// The week beginning on <paramref name="effectiveDate"/> — the one whose
    /// lineup is still open for editing, and the first the new rosters play.
    /// </param>
    public static async Task ApplyAsync(
        FantasyWarriorDbContext db, Trade trade, DateOnly effectiveDate, Period nextPeriod,
        CancellationToken ct = default)
    {
        foreach (var side in trade.Assets.GroupBy(a => a.FromTeamId))
        {
            var players = side.Where(a => a.AssetType == TradeAssetType.Player)
                .Select(a => a.PlayerId!.Value).ToList();
            // A franchise moves through the same door: it is a roster spot
            // closing on one team and opening on the other, and nothing else.
            // The balance rule upstream guarantees the other side is sending
            // one back, so neither team is ever left with two or none.
            var franchises = side.Where(a => a.AssetType == TradeAssetType.Franchise)
                .Select(a => a.FranchiseAbbrev!).ToList();
            if (players.Count == 0 && franchises.Count == 0) continue;

            var receivingTeam = side.First().ToTeamId;
            await RosterChange.ApplyAsync(
                db, trade.LeagueId, teamId: side.Key,
                playersOut: players, playersIn: [],
                startReason: RosterSpotStartReason.Trade,
                endReason: RosterSpotEndReason.Trade,
                effectiveDate: effectiveDate, tradeId: trade.TradeId,
                franchisesOut: franchises, ct: ct);
            await RosterChange.ApplyAsync(
                db, trade.LeagueId, teamId: receivingTeam,
                playersOut: [], playersIn: players,
                startReason: RosterSpotStartReason.Trade,
                endReason: RosterSpotEndReason.Trade,
                effectiveDate: effectiveDate, tradeId: trade.TradeId,
                franchisesIn: franchises, ct: ct);
        }

        // Picks change hands by pointing at their new owner. The original owner
        // is never touched — that column is what makes "Pittsburgh's 2nd, via
        // Boston" expressible at all.
        foreach (var asset in trade.Assets.Where(a => a.AssetType == TradeAssetType.DraftPick))
        {
            var pick = await db.DraftPicks.FirstOrDefaultAsync(p => p.DraftPickId == asset.DraftPickId, ct);
            if (pick is not null) pick.CurrentTeamId = asset.ToTeamId;
        }

        // The spots RosterChange just touched, found through the trade
        // references it set rather than re-deriving them from the assets: the
        // foreign key *is* the record of which trade moved which spot, and a
        // second derivation could disagree with it.
        //
        // Flushed first so the newly added spots have ids and the closed ones
        // have their end dates — this is a save inside the caller's
        // transaction, not a commit.
        await db.SaveChangesAsync(ct);
        var moved = await db.RosterSpots
            .Where(s => s.StartTradeId == trade.TradeId || s.EndTradeId == trade.TradeId)
            .ToListAsync(ct);

        await SquareNextWeekAsync(db, moved, nextPeriod, effectiveDate, ct);
    }

    /// <summary>
    /// Makes next week's lineup rows agree with who will actually be on the
    /// roster that week: the leavers lose theirs, the arrivals get one.
    ///
    /// Only ever touches the week the trade lands in. The week in progress is
    /// untouched by design — the leaver plays it out, and rewriting a week a GM
    /// has already been scored for would move history a trade is not allowed to
    /// move.
    /// </summary>
    private static async Task SquareNextWeekAsync(
        FantasyWarriorDbContext db, List<RosterSpot> moved, Period nextPeriod, DateOnly effectiveDate,
        CancellationToken ct)
    {
        var leaving = moved.Where(s => s.EndDate != null && s.EndDate < effectiveDate)
            .Select(s => s.RosterSpotId).ToList();
        var arriving = moved.Where(s => s.StartDate == effectiveDate && s.EndDate == null).ToList();

        if (leaving.Count > 0)
        {
            // Usually nothing to delete — the nightly job materializes next
            // week's rows once the current one is under way, so a trade agreed
            // early in the week is ahead of it. Deleting rather than deactivating
            // because the spot does not own that week at all: leaving a benched
            // row behind would put a player the GM no longer has on his screen.
            var stale = await db.RosterAssignments
                .Where(a => a.PeriodId == nextPeriod.PeriodId && leaving.Contains(a.RosterSpotId))
                .ToListAsync(ct);
            db.RosterAssignments.RemoveRange(stale);
        }

        if (arriving.Count == 0) return;

        // A re-run must not double up: the unique index on (RosterSpotId,
        // PeriodId) would refuse it, and this says so before the database has
        // to.
        var arrivingIds = arriving.Select(s => s.RosterSpotId).ToList();
        var already = await db.RosterAssignments
            .Where(a => a.PeriodId == nextPeriod.PeriodId && arrivingIds.Contains(a.RosterSpotId))
            .Select(a => a.RosterSpotId)
            .ToListAsync(ct);

        foreach (var spot in arriving.Where(s => !already.Contains(s.RosterSpotId)))
            db.RosterAssignments.Add(new RosterAssignment
            {
                RosterSpotId = spot.RosterSpotId,
                PeriodId = nextPeriod.PeriodId,
                // The Équipe slot has no active/bench decision — one franchise,
                // one seat — so it arrives active. Everyone else waits for his
                // new GM to pick him.
                IsActive = spot.IsFranchise,
                EffectiveFrom = effectiveDate,
                EffectiveTo = nextPeriod.EndDate,
                ScoredUtc = DateTime.UtcNow,
            });
    }
}
