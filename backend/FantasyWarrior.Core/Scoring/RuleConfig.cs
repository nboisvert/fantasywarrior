namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// Per-league scoring configuration, editable by the commissioner.
/// Defaults reflect Nick's buddies pool (2026-07-22): 1 pt per goal, assist
/// and goalie OT loss, 2 per goalie win, shutout disabled.
/// </summary>
public sealed class RuleConfig
{
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
    public Dictionary<string, double> ExtraPointValues { get; set; } = [];
    public TopCount TopCount { get; set; } = new();
    public RosterSize RosterSize { get; set; } = new();

    /// <summary>
    /// Draft rounds generated per season; one pick per team per round, so this
    /// is also "picks per team per year". Null = no draft.
    /// </summary>
    public int? DraftRounds { get; set; }

    /// <summary>
    /// How many roster spots a GM may protect before the off-season steal
    /// draft. Null = not configured. A player who is auto-protected on
    /// experience alone does not spend one of these.
    /// </summary>
    public int? ProtectionSlots { get; set; }

    /// <summary>
    /// What a player with no contract on file counts against the cap, in whole
    /// dollars. $1M by default (Nick, 2026-08-05); 0 restores the old behaviour
    /// of carrying such players for free.
    /// </summary>
    public long DefaultCapHit { get; set; } = 1_000_000;

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

        if (config.ProtectionSlots is < 0)
            errors.Add("Protection slots cannot be negative.");

        // A negative default would pay a team to hold unsigned players, and
        // every cap total in the league would drift further from the truth the
        // more of them it carried.
        if (config.DefaultCapHit < 0)
            errors.Add("The default cap hit cannot be negative.");

        return errors;
    }
}

public sealed class PointValues
{
    public double Goal { get; set; } = 1;
    public double Assist { get; set; } = 1;
    public double GoalieWin { get; set; } = 2;
    public double GoalieOtLoss { get; set; } = 1;
    public double Shutout { get; set; } = 0;
}

/// <summary>
/// How many players count toward the team score, per position group.
/// Null means every player counts.
/// </summary>
public sealed class TopCount
{
    public int? Forwards { get; set; }
    public int? Defense { get; set; }
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
public sealed class RosterSize
{
    public int? Min { get; set; }
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
