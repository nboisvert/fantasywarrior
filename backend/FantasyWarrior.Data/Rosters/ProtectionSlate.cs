using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Rosters;

/// <summary>What an autofill did, or would do.</summary>
/// <param name="Free">Out of reach for nothing — auto-protected on NHL experience.</param>
/// <param name="Exposed">Takeable: neither free nor protected.</param>
public sealed record ProtectionSlateResult(
    int Slots, int Teams, int Protected, int Free, int Exposed);

/// <summary>
/// The database half of the protection autofill: read every held spot, price
/// last season under the league's own scale, and write the slate
/// <see cref="ProtectionAutofill"/> chooses.
///
/// <b>Shared on purpose.</b> The commissioner's button
/// (<c>POST .../protections/autofill</c>) and <c>clone-league --drafting</c>
/// both need it, and they must agree: a sandbox whose protections were computed
/// by a second implementation would be exercising a draft nobody else's league
/// will ever run. The pure decision stays in <see cref="ProtectionAutofill"/>;
/// only the reading and the writing are here.
/// </summary>
public static class ProtectionSlate
{
    /// <param name="lastSeason">
    /// The season the ranking reads — the one that just ended, not the one being
    /// prepared. The caller derives it, because only the caller knows whether it
    /// holds a <c>LeagueSeason</c> row or a bare string.
    /// </param>
    /// <param name="asOf">
    /// The simulated day, when a replay is running. The database holds the whole
    /// 2025-26 schedule; unbounded, a replay sitting in December would rank
    /// players on games nobody has played.
    /// </param>
    /// <param name="protection">
    /// The <b>prepared</b> season's protection rules: how many slots a GM has,
    /// and the bars that put a player out of reach for free. They govern the
    /// draft about to run, so they come from the season being prepared — not
    /// from the one whose points are being ranked.
    /// </param>
    /// <param name="rankingScale">
    /// The scale <paramref name="lastSeason"/> was actually scored under. Points
    /// are ranked under the rules they were earned under, which is the whole
    /// reason a scale change no longer restates anything. On raw NHL points a
    /// goalie's season is zero and no goalie would ever be protected.
    /// </param>
    /// <param name="write">False to price the slate without touching a row.</param>
    public static async Task<ProtectionSlateResult> AutofillAsync(
        FantasyWarriorDbContext db, int leagueId, string lastSeason,
        ProtectionConfig protection, IReadOnlyDictionary<string, double> rankingScale,
        DateOnly? asOf, bool write, CancellationToken ct = default)
    {
        var slots = protection.Slots ?? 0;

        // The same filter the steal pool uses (DraftContextLoader), so what gets
        // protected is exactly what would otherwise be takeable. PlayerId != null
        // drops the franchise slots.
        var held = await db.RosterSpots
            .AsNoTracking()
            .Where(s => s.LeagueId == leagueId && s.PlayerId != null)
            .Where(RosterWindow.Committed())
            .Select(s => new
            {
                s.RosterSpotId,
                s.TeamId,
                PlayerId = s.PlayerId!.Value,
                s.Player!.PositionGroup,
                s.Player.CareerNhlGames,
            })
            .ToListAsync(ct);

        var totals = await SeasonTotalsQuery.ForAsync(
            db, lastSeason, held.Select(h => h.PlayerId).Distinct().ToList(), asOf, ct);

        var candidates = held
            .Select(h => new ProtectionCandidate(
                h.RosterSpotId, h.TeamId, h.PlayerId, h.PositionGroup, h.CareerNhlGames,
                // Absent from the totals means he did not play: a real zero, not
                // a gap. StatLine.Empty scores 0 under any scale.
                Points: totals.TryGetValue(h.PlayerId, out var t)
                    ? StatColumns.ToStatLine(t).Score(rankingScale)
                    : 0d))
            .ToList();

        var chosen = ProtectionAutofill.Choose(candidates, slots, protection.Auto);
        var free = candidates.Count(c => !ProtectionAutofill.NeedsASlot(c, protection.Auto));

        var result = new ProtectionSlateResult(
            Slots: slots,
            Teams: held.Select(h => h.TeamId).Distinct().Count(),
            Protected: chosen.Count,
            Free: free,
            Exposed: candidates.Count - free - chosen.Count);

        if (!write) return result;

        // Clear and rewrite rather than diff: the slate is one summer's worth of
        // state, and a run that only added would leave yesterday's choices behind
        // when the slot count changes.
        await db.RosterSpots
            .Where(s => s.LeagueId == leagueId
                        && s.ProtectionStatus != RosterProtectionStatus.Unprotected)
            .ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.ProtectionStatus, RosterProtectionStatus.Unprotected), ct);

        await db.RosterSpots
            .Where(s => chosen.Contains(s.RosterSpotId))
            .ExecuteUpdateAsync(u =>
                u.SetProperty(s => s.ProtectionStatus, RosterProtectionStatus.Protected), ct);

        return result;
    }
}
