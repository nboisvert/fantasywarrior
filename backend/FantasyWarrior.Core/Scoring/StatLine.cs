namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// A bag of counting stats, keyed by <see cref="StatKeys"/>.
///
/// This replaces four competing shapes that all meant "a pile of hockey stats"
/// and had to be kept in sync by hand: the positional <c>PlayerRawTotals</c>
/// record (whose field order was load-bearing — two call sites constructed it
/// positionally, so reordering it silently corrupted data), the flat fields on
/// <c>PlayerSeasonStats</c>, the fourteen inline fields on <c>Assignment</c>,
/// and <c>AssignmentStats.ToFieldMap</c>'s string keys.
///
/// Immutable, and a missing key reads as 0 rather than throwing: a skater has
/// no <c>saves</c> and a goalie no <c>hits</c>, and neither is an error.
/// </summary>
public sealed class StatLine
{
    private readonly IReadOnlyDictionary<string, int> _values;

    public static readonly StatLine Empty = new(new Dictionary<string, int>());

    public StatLine(IReadOnlyDictionary<string, int> values) => _values = values;

    /// <summary>Zero for any stat that was never recorded.</summary>
    public int this[string key] => _values.TryGetValue(key, out var v) ? v : 0;

    /// <summary>Only the non-zero entries — what gets persisted.</summary>
    public IReadOnlyDictionary<string, int> ToMap() =>
        _values.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value);

    /// <summary>
    /// Rebuilds from a Firestore map. Firestore hands back boxed <c>long</c>
    /// for integers, so this accepts <c>object</c> values rather than forcing
    /// every caller to cast.
    /// </summary>
    public static StatLine FromMap(IReadOnlyDictionary<string, object>? map)
    {
        if (map is null || map.Count == 0) return Empty;
        var values = new Dictionary<string, int>();
        foreach (var (key, raw) in map)
            values[key] = raw switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                _ => 0,
            };
        return new StatLine(values);
    }

    public StatLine Plus(StatLine other)
    {
        var values = new Dictionary<string, int>(_values.ToDictionary(kv => kv.Key, kv => kv.Value));
        foreach (var key in other._values.Keys)
            values[key] = this[key] + other[key];
        return new StatLine(values);
    }

    public static StatLine Sum(IEnumerable<StatLine> lines) =>
        lines.Aggregate(Empty, (acc, l) => acc.Plus(l));

    /// <summary>
    /// Fantasy points under a league's scale. Stats the league doesn't score
    /// contribute nothing, which is why the same <see cref="StatLine"/> can
    /// back every league regardless of its rules.
    /// </summary>
    public double Score(IReadOnlyDictionary<string, double> pointValues) =>
        pointValues.Sum(pv => this[pv.Key] * pv.Value);
}
