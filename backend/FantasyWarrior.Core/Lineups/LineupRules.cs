using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Lineups;

/// <summary>How many players may be active at each position.</summary>
public sealed record LineupSlots(int Forwards, int Defense, int Goalies)
{
    public int For(string positionGroup) => positionGroup switch
    {
        "D" => Defense,
        "G" => Goalies,
        _ => Forwards,
    };

    public int Total => Forwards + Defense + Goalies;
}

/// <summary>
/// One roster spot as the lineup logic sees it. Deliberately not the Firestore
/// entity: keeping this a plain record is what lets every rule below be pure
/// and tested without mocking.
/// </summary>
public sealed record LineupCandidate(
    string SpotId,
    long PlayerId,
    string PositionGroup,
    double SeasonPointsToDate = 0,
    DateTime ActivatedUtc = default);

/// <summary>
/// The rules governing a weekly lineup. All pure.
///
/// This is the only place in the app where a rule is actually enforced rather
/// than merely displayed — roster size and the salary cap are still advisory.
/// </summary>
public static class LineupRules
{
    /// <summary>
    /// Why a proposed lineup is illegal; empty means it is fine. Returns every
    /// problem rather than the first, so the UI can show them all at once.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        IReadOnlyCollection<LineupCandidate> roster,
        IReadOnlyCollection<string> activeSpotIds,
        LineupSlots slots)
    {
        var errors = new List<string>();

        var duplicates = activeSpotIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            errors.Add($"Duplicate entries in the lineup: {string.Join(", ", duplicates)}.");

        var bySpot = roster.ToDictionary(c => c.SpotId);
        var unknown = activeSpotIds.Distinct().Where(id => !bySpot.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            errors.Add($"Not on this roster: {string.Join(", ", unknown)}.");

        foreach (var group in activeSpotIds.Distinct()
                     .Where(bySpot.ContainsKey)
                     .GroupBy(id => bySpot[id].PositionGroup))
        {
            var limit = slots.For(group.Key);
            if (group.Count() > limit)
                errors.Add($"Too many active at {group.Key}: {group.Count()} of {limit}.");
        }

        return errors;
    }

    /// <summary>
    /// How many active slots are used per position group — what the UI shows
    /// as "4/4 F · 2/3 D · 1/1 G".
    /// </summary>
    public static IReadOnlyDictionary<string, int> Used(
        IReadOnlyCollection<LineupCandidate> roster, IReadOnlyCollection<string> activeSpotIds)
    {
        var bySpot = roster.ToDictionary(c => c.SpotId);
        var used = new Dictionary<string, int> { ["F"] = 0, ["D"] = 0, ["G"] = 0 };
        foreach (var id in activeSpotIds.Distinct().Where(bySpot.ContainsKey))
            used[bySpot[id].PositionGroup] = used.GetValueOrDefault(bySpot[id].PositionGroup) + 1;
        return used;
    }

    /// <summary>
    /// Forces a possibly-illegal active set to be legal, deterministically.
    ///
    /// Needed because an illegal lineup can arrive without anyone cheating —
    /// a commissioner shrinking the slots mid-season is enough. Trimming drops
    /// the most recently activated player first: he is the one who made the
    /// lineup illegal, which is causally right, and it is stable where a
    /// playerId tiebreak would be arbitrary.
    /// </summary>
    public static IReadOnlySet<string> LegalActiveSet(
        IReadOnlyCollection<LineupCandidate> roster,
        IReadOnlyCollection<string> activeSpotIds,
        LineupSlots slots)
    {
        var bySpot = roster.ToDictionary(c => c.SpotId);
        var kept = new HashSet<string>();

        foreach (var group in activeSpotIds.Distinct()
                     .Where(bySpot.ContainsKey)
                     .GroupBy(id => bySpot[id].PositionGroup))
        {
            var ordered = group
                .OrderBy(id => bySpot[id].ActivatedUtc)
                .ThenBy(id => bySpot[id].PlayerId)
                .Take(slots.For(group.Key));
            foreach (var id in ordered) kept.Add(id);
        }
        return kept;
    }

    /// <summary>
    /// Last week's active set, minus anyone no longer on the roster, then
    /// topped up to full strength.
    ///
    /// A GM who forgets to set a lineup keeps the one he chose, rather than
    /// scoring zero — in a pool between friends, punishing a vacation makes
    /// the standings meaningless. Whoever left is replaced by
    /// <see cref="AutoFill"/> so no slot is silently wasted.
    /// </summary>
    public static IReadOnlySet<string> CarryForward(
        IReadOnlyCollection<LineupCandidate> roster,
        IReadOnlyCollection<string> previousActiveSpotIds,
        LineupSlots slots)
    {
        var stillHeld = roster.Select(c => c.SpotId).ToHashSet();
        var carried = previousActiveSpotIds.Where(stillHeld.Contains).ToList();
        return AutoFill(roster, LegalActiveSet(roster, carried, slots), slots);
    }

    /// <summary>
    /// Tops an active set up to the slot limits with the best available
    /// players, ranked by season points then by playerId for determinism.
    /// </summary>
    public static IReadOnlySet<string> AutoFill(
        IReadOnlyCollection<LineupCandidate> roster,
        IReadOnlySet<string> alreadyActive,
        LineupSlots slots)
    {
        var active = new HashSet<string>(alreadyActive);

        foreach (var group in roster.GroupBy(c => c.PositionGroup))
        {
            var need = slots.For(group.Key) - group.Count(c => active.Contains(c.SpotId));
            if (need <= 0) continue;

            var fill = group
                .Where(c => !active.Contains(c.SpotId))
                .OrderByDescending(c => c.SeasonPointsToDate)
                .ThenBy(c => c.PlayerId)
                .Take(need);
            foreach (var c in fill) active.Add(c.SpotId);
        }
        return active;
    }

    /// <summary>Reads the slot config off a league's rules.</summary>
    public static LineupSlots SlotsFrom(RuleConfig config) => new(
        Forwards: config.TopCount.Forwards ?? 0,
        Defense: config.TopCount.Defense ?? 0,
        Goalies: config.TopCount.Goalies ?? 0);
}
