namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// The canonical names of every stat the system can count or score.
///
/// These strings are persisted — they are the keys of <see cref="StatLine"/>
/// maps in Firestore and of a league's <c>pointValues</c> config. Renaming one
/// is a data migration, not a refactor.
///
/// Having them as data rather than as properties on a fixed class is what lets
/// a commissioner score a stat the code never anticipated: adding "1 point per
/// blocked shot" is a config edit, not a schema change on every league. It also
/// means <see cref="All"/> is the validation whitelist — an unrecognised key in
/// a rule config would otherwise score zero forever, silently.
/// </summary>
public static class StatKeys
{
    public const string GamesPlayed = "gamesPlayed";
    public const string Goals = "goals";
    public const string Assists = "assists";
    public const string PlusMinus = "plusMinus";
    public const string Pim = "pim";
    public const string Shots = "shots";
    public const string Hits = "hits";
    public const string BlockedShots = "blockedShots";

    // --- goalies ---
    public const string Wins = "wins";
    public const string OtLosses = "otLosses";
    public const string Shutouts = "shutouts";
    public const string GoalsAgainst = "goalsAgainst";
    public const string Saves = "saves";
    public const string ShotsAgainst = "shotsAgainst";

    public static readonly IReadOnlyList<string> All =
    [
        GamesPlayed, Goals, Assists, PlusMinus, Pim, Shots, Hits, BlockedShots,
        Wins, OtLosses, Shutouts, GoalsAgainst, Saves, ShotsAgainst,
    ];

    public static bool IsKnown(string key) => All.Contains(key);
}
