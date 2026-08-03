namespace FantasyWarrior.Core.Trades;

/// <summary>
/// What a trade would do to one team's cap and roster size.
/// </summary>
/// <param name="CapBefore">
/// The <b>engaged</b> figure, not the standings one: it already includes trades
/// this team has accepted but that have not executed yet. Validating against
/// the standings would let a GM accept a $9M contract in the morning and bust
/// the cap in the afternoon, with each trade looking fine on its own.
/// </param>
/// <param name="UnknownContracts">
/// How many of the players moving have no contract on file. They count as $0 in
/// the totals — the same way the standings count them — so this is the only
/// honest signal that a cap figure may be understated.
/// </param>
public sealed record TradeImpact(
    string TeamName,
    long CapBefore,
    long CapAfter,
    int CountBefore,
    int CountAfter,
    int UnknownContracts)
{
    public long CapDelta => CapAfter - CapBefore;
    public int CountDelta => CountAfter - CountBefore;
}

/// <summary>
/// Whether a trade is legal. All pure.
///
/// The rules themselves are arithmetic; what makes this worth its own module is
/// that a trade is <b>two-sided</b>. Every check has to run for both teams,
/// because an offer that fixes my cap can blow up the other GM's, and a 2-for-1
/// pushes one roster up while pushing the other down — so the same trade can
/// break the maximum on one side and the minimum on the other.
///
/// Callers run <see cref="Impact"/> once per team and <see cref="Validate"/>
/// once per impact, then concatenate. Every violation is returned rather than
/// the first, so the UI can show them all at once — same convention as
/// <c>LineupRules.Validate</c>.
/// </summary>
public static class TradeRules
{
    /// <summary>
    /// Applies a trade to one team's figures. Cap hits are nullable because
    /// "no contract on file" is a real state, and it is counted rather than
    /// quietly dropped.
    ///
    /// Draft picks are simply absent from both collections: they carry no
    /// salary and occupy no roster spot, so they move neither number.
    /// </summary>
    public static TradeImpact Impact(
        string teamName,
        long capBefore,
        int countBefore,
        IReadOnlyCollection<long?> outgoing,
        IReadOnlyCollection<long?> incoming)
    {
        var leaving = outgoing.Sum(c => c ?? 0);
        var arriving = incoming.Sum(c => c ?? 0);

        return new TradeImpact(
            TeamName: teamName,
            CapBefore: capBefore,
            CapAfter: capBefore - leaving + arriving,
            CountBefore: countBefore,
            CountAfter: countBefore - outgoing.Count + incoming.Count,
            UnknownContracts: outgoing.Count(c => c is null) + incoming.Count(c => c is null));
    }

    /// <summary>
    /// Why this side of the trade is illegal; empty means it is fine. A null
    /// limit means the league does not have that rule, not a limit of zero.
    ///
    /// Every message names the team, because the offending side is very often
    /// the *other* one and "over the cap" without a name is unactionable.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        TradeImpact impact, long? capAmount, int? rosterMin, int? rosterMax)
    {
        var errors = new List<string>();

        if (capAmount is { } cap && impact.CapAfter > cap)
            errors.Add(
                $"{impact.TeamName} would be {Money(impact.CapAfter - cap)} over the {Money(cap)} cap.");

        if (rosterMax is { } max && impact.CountAfter > max)
            errors.Add(
                $"{impact.TeamName} would have {impact.CountAfter} players, over the maximum of {max}.");

        if (rosterMin is { } min && impact.CountAfter < min)
            errors.Add(
                $"{impact.TeamName} would have {impact.CountAfter} players, under the minimum of {min}.");

        return errors;
    }

    private static string Money(long amount) => $"${amount:N0}";
}
