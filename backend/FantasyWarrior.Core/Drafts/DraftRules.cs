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
    /// <b><paramref name="rosterMin"/> is deliberately absent, and must stay
    /// absent.</b> Passing the league's real minimum to
    /// <see cref="TradeRules.Validate"/> would refuse exactly the situation the
    /// phase model was designed around: <c>season-lifecycle.md</c> §5 says
    /// <c>PreSeason</c> exists <i>because</i> a team can come out of the draft
    /// under <c>RosterMin</c> — two players lost, one drafted back — and needs a
    /// window to repair itself before lineups matter again. Enforcing the
    /// minimum during the draft would make that window unreachable.
    /// </summary>
    public static IReadOnlyList<string> ValidateSelection(
        string pickerTeamName,
        long pickerCapBefore,
        int pickerCountBefore,
        long? incomingCapHit,
        long defaultCapHit,
        long? capAmount,
        int? rosterMax)
    {
        var impact = TradeRules.Impact(
            teamName: pickerTeamName,
            capBefore: pickerCapBefore,
            countBefore: pickerCountBefore,
            outgoing: [],
            incoming: [incomingCapHit],
            defaultCapHit: defaultCapHit);

        return TradeRules.Validate(impact, capAmount, rosterMin: null, rosterMax: rosterMax);
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
