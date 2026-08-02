using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Applies a roster change to one team: closes the outgoing players' spots and
/// opens spots for the incoming ones. Generalised to N-out/M-in so a two-sided
/// trade calls it once per team.
///
/// **One transaction.** The Firestore version issued a query per outgoing
/// player, an add per incoming one, and a final team update — any of which
/// could be the last thing to succeed, leaving a player owned by nobody or by
/// two teams at once. Here the whole change commits or none of it does, and the
/// filtered unique index on (LeagueId, PlayerId) makes the double-ownership
/// case impossible even under a race.
///
/// There is deliberately no score arithmetic. Under the weekly banked model a
/// week's points belong permanently to whoever fielded the player, so a trade
/// cannot move history and there is nothing to compensate for — the entire
/// adjustment ledger this used to maintain is gone.
/// </summary>
public static class RosterChange
{
    public static async Task ApplyAsync(
        FantasyWarriorDbContext db,
        int leagueId,
        int teamId,
        IReadOnlyCollection<long> playersOut,
        IReadOnlyCollection<long> playersIn,
        RosterSpotStartReason startReason,
        RosterSpotEndReason endReason,
        DateOnly effectiveDate,
        int? tradeId = null,
        int? draftPickId = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (playersOut.Count > 0)
        {
            var closing = await db.RosterSpots
                .Where(s => s.TeamId == teamId && s.EndDate == null && playersOut.Contains(s.PlayerId))
                .ToListAsync(ct);
            foreach (var spot in closing)
            {
                // The end date is the day *before* the new stint starts, so the
                // two never both own the same day — which would give one player
                // two owners for one scoring window.
                spot.EndDate = effectiveDate.AddDays(-1);
                spot.EndReason = endReason;
                spot.EndTradeId = endReason == RosterSpotEndReason.Trade ? tradeId : null;
                spot.ClosedUtc = now;
            }
        }

        if (playersIn.Count > 0)
        {
            var positions = await db.Players
                .Where(p => playersIn.Contains(p.PlayerId))
                .ToDictionaryAsync(p => p.PlayerId, p => p.Position, ct);

            foreach (var playerId in playersIn)
                db.RosterSpots.Add(new RosterSpot
                {
                    LeagueId = leagueId,
                    TeamId = teamId,
                    PlayerId = playerId,
                    // Frozen at open: a player's slot eligibility stays stable
                    // for the life of the spot even if the NHL relists him.
                    PositionGroup = PositionGroups.CodeFrom(positions.GetValueOrDefault(playerId, "C")),
                    StartDate = effectiveDate,
                    StartReason = startReason,
                    StartTradeId = startReason == RosterSpotStartReason.Trade ? tradeId : null,
                    StartDraftPickId = startReason == RosterSpotStartReason.Draft ? draftPickId : null,
                    OpenedUtc = now,
                });
        }
    }
}
