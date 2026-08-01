using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// Per-league scoring configuration, editable by the commissioner.
/// Defaults reflect Nick's buddies pool (2026-07-22): 1 pt per goal, assist
/// and goalie OT loss, 2 per goalie win, shutout disabled.
/// </summary>
[FirestoreData]
public sealed class RuleConfig
{
    [FirestoreProperty("pointValues")]
    public PointValues PointValues { get; set; } = new();

    /// <summary>
    /// Extra scored stats, keyed by <see cref="StatKeys"/> — anything beyond
    /// the five <see cref="PointValues"/> carries as named properties.
    ///
    /// This is what makes a different scoring model a config change rather
    /// than a schema migration: a commissioner can score blocked shots, hits
    /// or games played without a line of code. The five fixed properties stay
    /// because every existing league document already has them and rewriting
    /// them all would buy nothing.
    /// </summary>
    [FirestoreProperty("extraPointValues")]
    public Dictionary<string, double> ExtraPointValues { get; set; } = [];

    [FirestoreProperty("topCount")]
    public TopCount TopCount { get; set; } = new();

    [FirestoreProperty("rosterSize")]
    public RosterSize RosterSize { get; set; } = new();

    /// <summary>
    /// The whole scale as one <see cref="StatKeys"/>-keyed map — the only form
    /// the scoring engine consumes, so callers never have to know which half a
    /// value came from.
    /// </summary>
    public Dictionary<string, double> ScoringScale()
    {
        var scale = new Dictionary<string, double>
        {
            [StatKeys.Goals] = PointValues.Goal,
            [StatKeys.Assists] = PointValues.Assist,
            [StatKeys.Wins] = PointValues.GoalieWin,
            [StatKeys.OtLosses] = PointValues.GoalieOtLoss,
            [StatKeys.Shutouts] = PointValues.Shutout,
        };
        foreach (var (key, value) in ExtraPointValues) scale[key] = value;
        return scale;
    }
}

/// <summary>Validation for the open-ended half of the scale.</summary>
public static class RuleConfigValidation
{
    /// <summary>
    /// Reasons a config would be rejected; empty means it is fine.
    ///
    /// An unrecognised stat key is the dangerous case: it would score zero
    /// forever, silently, and look like a scoring bug rather than a typo.
    /// </summary>
    public static IReadOnlyList<string> Validate(RuleConfig config)
    {
        var errors = new List<string>();

        foreach (var key in config.ExtraPointValues.Keys.Where(k => !StatKeys.IsKnown(k)))
            errors.Add($"Unknown stat \"{key}\". Known stats: {string.Join(", ", StatKeys.All)}.");

        foreach (var (name, slots) in new[]
                 {
                     ("forwards", config.TopCount.Forwards),
                     ("defense", config.TopCount.Defense),
                     ("goalies", config.TopCount.Goalies),
                 })
            if (slots is < 0) errors.Add($"Active {name} slots cannot be negative.");

        if (config.RosterSize.Min is { } min && config.RosterSize.Max is { } max && min > max)
            errors.Add($"Roster minimum ({min}) cannot exceed the maximum ({max}).");

        return errors;
    }
}

[FirestoreData]
public sealed class PointValues
{
    [FirestoreProperty("goal")]
    public double Goal { get; set; } = 1;

    [FirestoreProperty("assist")]
    public double Assist { get; set; } = 1;

    [FirestoreProperty("goalieWin")]
    public double GoalieWin { get; set; } = 2;

    [FirestoreProperty("goalieOtLoss")]
    public double GoalieOtLoss { get; set; } = 1;

    [FirestoreProperty("shutout")]
    public double Shutout { get; set; } = 0;
}

/// <summary>
/// How many players count toward the team score, per position group.
/// Null means every player counts.
/// </summary>
[FirestoreData]
public sealed class TopCount
{
    [FirestoreProperty("forwards")]
    public int? Forwards { get; set; }

    [FirestoreProperty("defense")]
    public int? Defense { get; set; }

    [FirestoreProperty("goalies")]
    public int? Goalies { get; set; }

    public int? For(PositionGroup group) => group switch
    {
        PositionGroup.Forward => Forwards,
        PositionGroup.Defense => Defense,
        _ => Goalies,
    };
}

/// <summary>
/// Roster size bounds (null = no limit). Not enforced anywhere yet — saved
/// and displayed only, per Nick (2026-07-27); enforcement is a future round.
/// </summary>
[FirestoreData]
public sealed class RosterSize
{
    [FirestoreProperty("min")]
    public int? Min { get; set; }

    [FirestoreProperty("max")]
    public int? Max { get; set; }
}

public enum PositionGroup
{
    Forward,
    Defense,
    Goalie,
}

public static class PositionGroups
{
    /// <summary>Maps an NHL position code (C, L, R, D, G) to its scoring group.</summary>
    public static PositionGroup From(string position) => position switch
    {
        "D" => PositionGroup.Defense,
        "G" => PositionGroup.Goalie,
        _ => PositionGroup.Forward,
    };

    /// <summary>The persisted single-letter form: F, D or G.</summary>
    public static string Code(PositionGroup group) => group switch
    {
        PositionGroup.Defense => "D",
        PositionGroup.Goalie => "G",
        _ => "F",
    };

    /// <summary>NHL position code straight to its persisted group letter.</summary>
    public static string CodeFrom(string position) => Code(From(position));

    public static PositionGroup FromCode(string code) => code switch
    {
        "D" => PositionGroup.Defense,
        "G" => PositionGroup.Goalie,
        _ => PositionGroup.Forward,
    };
}
