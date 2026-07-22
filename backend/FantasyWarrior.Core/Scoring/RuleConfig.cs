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

    [FirestoreProperty("topCount")]
    public TopCount TopCount { get; set; } = new();
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
}
