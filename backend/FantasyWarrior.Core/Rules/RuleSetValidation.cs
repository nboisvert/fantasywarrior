using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Rules;

/// <summary>
/// Why a <see cref="RuleSet"/> could not be saved. Pure.
///
/// <b>Only contradictions live here</b> — a rule that cannot mean anything, or
/// that means the opposite of another rule in the same document. "The code does
/// not honour this yet" is a different judgement entirely and belongs to
/// <see cref="RuleSetCapabilities"/>: a commissioner may record a rule the app
/// has not caught up with, but never one the app could not act on if it had.
///
/// Every violation is returned rather than the first, so a rules panel can show
/// them all at once — the same convention as <c>LineupRules.Validate</c> and
/// <c>TradeRules.Validate</c>.
/// </summary>
public static class RuleSetValidation
{
    /// <summary>The three persisted position-group letters a map may be keyed by.</summary>
    private static readonly string[] Groups = ["F", "D", "G"];

    private static readonly string[] FranchiseKeys =
        [StatKeys.TeamWins, StatKeys.TeamLosses, StatKeys.TeamOtLosses];

    /// <summary>Reasons this configuration would be rejected; empty means it is fine.</summary>
    public static IReadOnlyList<string> Validate(RuleSet rules)
    {
        var errors = new List<string>();

        if (rules.Version < 1)
            errors.Add($"Rule set version {rules.Version} is not a version.");

        ValidateCap(rules, errors);
        ValidateRoster(rules, errors);
        ValidateLineup(rules, errors);
        ValidateScoring(rules, errors);
        ValidateTrades(rules, errors);
        ValidateProtection(rules, errors);
        ValidateDraft(rules, errors);
        ValidateFreeAgency(rules, errors);

        return errors;
    }

    private static void ValidateCap(RuleSet rules, List<string> errors)
    {
        if (rules.Cap.Max is < 0) errors.Add("The salary cap cannot be negative.");
        if (rules.Cap.Min is < 0) errors.Add("The cap floor cannot be negative.");

        if (rules.Cap.Min is { } min && rules.Cap.Max is { } max && min > max)
            errors.Add($"The cap floor ({Money(min)}) cannot exceed the ceiling ({Money(max)}).");

        // A negative default would pay a team to hold unsigned players, and
        // every cap total in the league would drift further from the truth the
        // more of them it carried.
        if (rules.Cap.DefaultCapHit < 0)
            errors.Add("The default cap hit cannot be negative.");
    }

    private static void ValidateRoster(RuleSet rules, List<string> errors)
    {
        if (rules.Roster.Min is < 0) errors.Add("The roster minimum cannot be negative.");
        if (rules.Roster.Max is < 0) errors.Add("The roster maximum cannot be negative.");

        if (rules.Roster.Min is { } min && rules.Roster.Max is { } max && min > max)
            errors.Add($"Roster minimum ({min}) cannot exceed the maximum ({max}).");

        foreach (var group in Groups)
        {
            var bounds = rules.Roster.ByPosition.For(group);
            if (bounds.Min is < 0) errors.Add($"The {Name(group)} roster minimum cannot be negative.");
            if (bounds.Max is < 0) errors.Add($"The {Name(group)} roster maximum cannot be negative.");
            if (bounds.Min is { } gMin && bounds.Max is { } gMax && gMin > gMax)
                errors.Add($"The {Name(group)} roster minimum ({gMin}) cannot exceed its maximum ({gMax}).");
        }

        // Per-group bounds that cannot coexist with the overall ones: a roster
        // obeying every group would still break the total, so no legal roster
        // exists and every trade would be refused with no way to say why.
        var groupMinimums = Groups.Sum(g => rules.Roster.ByPosition.For(g).Min ?? 0);
        if (rules.Roster.Max is { } rosterMax && groupMinimums > rosterMax)
            errors.Add(
                $"The per-position minimums add up to {groupMinimums}, over the roster maximum of {rosterMax}.");

        if (Groups.All(g => rules.Roster.ByPosition.For(g).Max is not null))
        {
            var groupMaximums = Groups.Sum(g => rules.Roster.ByPosition.For(g).Max ?? 0);
            if (rules.Roster.Min is { } rosterMin && groupMaximums < rosterMin)
                errors.Add(
                    $"The per-position maximums add up to {groupMaximums}, under the roster minimum of {rosterMin}.");
        }
    }

    private static void ValidateLineup(RuleSet rules, List<string> errors)
    {
        foreach (var (name, slots) in new[]
                 {
                     ("forwards", rules.Lineup.Slots.Forwards),
                     ("defense", rules.Lineup.Slots.Defense),
                     ("goalies", rules.Lineup.Slots.Goalies),
                 })
            if (slots < 0) errors.Add($"Active {name} slots cannot be negative.");

        // The Équipe slot is not counted here: it costs no roster room and is
        // never benched, so it takes no lineup slot either.
        if (rules.Roster.Max is { } max && rules.Lineup.Slots.Total > max)
            errors.Add(
                $"The lineup needs {rules.Lineup.Slots.Total} players but the roster maximum is {max}, "
                + "so no team could ever field a full one.");
    }

    private static void ValidateScoring(RuleSet rules, List<string> errors)
    {
        foreach (var key in rules.Scoring.Values.Keys.Where(k => !StatKeys.IsKnown(k)))
            errors.Add(Unknown(key));

        foreach (var (group, overrides) in rules.Scoring.ByPosition)
        {
            if (!Groups.Contains(group))
                errors.Add(
                    $"\"{group}\" is not a position group. Scoring overrides are keyed by F, D or G.");

            foreach (var key in overrides.Keys.Where(k => !StatKeys.IsKnown(k)))
                errors.Add(Unknown(key));
        }

        // Without an Équipe slot nothing can ever earn these, so a value here is
        // a rule that pays for an event the league cannot produce.
        if (!rules.Roster.FranchiseSlot)
        {
            var priced = FranchiseKeys
                .Where(k => rules.Scoring.Values.TryGetValue(k, out var v) && v != 0)
                .ToList();
            if (priced.Count > 0)
                errors.Add(
                    $"{string.Join(", ", priced)} pay for a franchise's record, but this league has no "
                    + "Équipe slot. Turn the franchise slot on, or set them to zero.");
        }
    }

    private static void ValidateTrades(RuleSet rules, List<string> errors)
    {
        if (rules.Trades.PickYearsAhead < 1)
            errors.Add("Draft picks must exist at least one season ahead.");
    }

    private static void ValidateProtection(RuleSet rules, List<string> errors)
    {
        if (rules.Protection.Slots is < 0)
            errors.Add("Protection slots cannot be negative.");

        if (rules.Protection.SlotsByPosition is { } byPosition)
        {
            foreach (var group in Groups)
                if (byPosition.For(group) < 0)
                    errors.Add($"{Name(group)} protection slots cannot be negative.");

            if (rules.Protection.Slots is { } total && byPosition.Total > total)
                errors.Add(
                    $"The per-position protection slots add up to {byPosition.Total}, over the "
                    + $"league's {total}.");
        }

        if (rules.Protection.Auto.SkaterMaxCareerGames < 0)
            errors.Add("The skater auto-protection bar cannot be negative.");
        if (rules.Protection.Auto.GoalieMaxCareerGames < 0)
            errors.Add("The goalie auto-protection bar cannot be negative.");
    }

    private static void ValidateDraft(RuleSet rules, List<string> errors)
    {
        if (rules.Draft.Steal.Rounds < 0) errors.Add("Steal rounds cannot be negative.");
        if (rules.Draft.Steal.MaxLossesPerTeam is < 0)
            errors.Add("The maximum losses per team cannot be negative.");
        if (rules.Draft.RookieRounds is < 0) errors.Add("Rookie draft rounds cannot be negative.");

        // The two say opposite things about the same players: one puts them in
        // dedicated steal rounds, the other in the ordinary pool.
        if (rules.Draft.UnprotectedDisposition is UnprotectedDisposition.OpenPool
            && rules.Draft.Steal.Rounds > 0)
            errors.Add(
                "Unprotected players go into the open draft pool, so there cannot also be "
                + $"{rules.Draft.Steal.Rounds} steal round(s).");
    }

    private static void ValidateFreeAgency(RuleSet rules, List<string> errors)
    {
        if (rules.FreeAgency.MovesPerPeriod is < 0)
            errors.Add("Free-agency moves per week cannot be negative.");

        var windows = rules.FreeAgency.Windows;

        if (rules.FreeAgency.Mode is FreeAgencyMode.Windows && windows.Count == 0)
            errors.Add("Free agency runs in windows, but none are defined.");

        foreach (var window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.Name))
                errors.Add("Every free-agency window needs a name.");
            if (window.End < window.Start)
                errors.Add(
                    $"Free-agency window \"{window.Name}\" ends ({window.End:yyyy-MM-dd}) before it "
                    + $"starts ({window.Start:yyyy-MM-dd}).");
        }

        // Overlapping windows are not merely redundant: "moves per period" would
        // be counted against two windows at once, with no rule saying which.
        var ordered = windows.Where(w => w.End >= w.Start).OrderBy(w => w.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
            if (ordered[i].Start <= ordered[i - 1].End)
                errors.Add(
                    $"Free-agency windows \"{ordered[i - 1].Name}\" and \"{ordered[i].Name}\" overlap.");
    }

    private static string Unknown(string key) =>
        $"Unknown stat \"{key}\". Known stats: {string.Join(", ", StatKeys.All)}.";

    private static string Name(string group) => group switch
    {
        "D" => "defense",
        "G" => "goalie",
        _ => "forward",
    };

    private static string Money(long amount) => $"${amount:N0}";
}
