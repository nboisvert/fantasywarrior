using FantasyWarrior.Core.Trades;

namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// Whether one selection is legal for the team making it, and for the team
/// losing a player. Pure.
///
/// The cap and roster arithmetic is not re-implemented here — it delegates to
/// <see cref="TradeRules"/>, which already owns it and is already tested. A
/// draft selection is an add on one side and a drop on the other, which is a
/// trade's shape with one asset; a second copy of that arithmetic is a second
/// copy that will disagree.
/// </summary>
public static class DraftRules
{
    /// <summary>
    /// Why the picking team may not take this player; empty means it is fine.
    ///
    /// <b>Neither <c>RosterMin</c> nor <c>RosterMax</c> is enforced here, and
    /// both must stay absent</b> (Nick, 2026-08-29 — the max joined the min).
    /// Roster-size bounds are trade rules; a draft selection is off-season by
    /// construction — this only ever runs while a <c>LeagueSeason</c> is
    /// <c>Drafting</c>, which is never <c>InSeason</c> — and
    /// <c>season-lifecycle.md</c> §5 says <c>PreSeason</c> exists precisely so a
    /// roster coming out of the draft can be out of bounds in either direction
    /// and still have a window to trade itself back into shape before lineups
    /// matter again. A team already sitting at <c>RosterMax</c> before its steal
    /// turn — a real state, not a hypothetical one — could otherwise never take
    /// anyone, with no way inside the draft to shed a player first.
    ///
    /// <b>The salary cap is a different rule and keeps applying.</b> Nick's ask
    /// was roster *size*, not the cap; <paramref name="capAmount"/> is
    /// unchanged and still refused over. Trades are unaffected either way —
    /// this method, not <see cref="TradeRules.Validate"/> itself, is what a
    /// draft calls, so nothing here loosens what a trade enforces.
    /// </summary>
    public static IReadOnlyList<string> ValidateSelection(
        string pickerTeamName,
        long pickerCapBefore,
        int pickerCountBefore,
        long? incomingCapHit,
        long defaultCapHit,
        long? capAmount)
    {
        var impact = TradeRules.Impact(
            teamName: pickerTeamName,
            capBefore: pickerCapBefore,
            countBefore: pickerCountBefore,
            outgoing: [],
            incoming: [incomingCapHit],
            defaultCapHit: defaultCapHit);

        return TradeRules.Validate(impact, capAmount, rosterMin: null, rosterMax: null);
    }

    /// <summary>
    /// Why this team may not lose another player; empty means it may.
    ///
    /// A null limit is "the league has no such rule", not a limit of zero —
    /// the same convention <see cref="TradeRules.Validate"/> uses for the cap.
    /// </summary>
    public static IReadOnlyList<string> ValidateLoss(
        string victimTeamName, int victimLossesSoFar, int? maxLossesPerTeam)
    {
        if (maxLossesPerTeam is not { } max) return [];
        if (victimLossesSoFar < max) return [];

        return [$"{victimTeamName} has already lost {max} players in this draft."];
    }
}
