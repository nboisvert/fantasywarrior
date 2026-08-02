namespace FantasyWarrior.Core.Players;

/// <summary>
/// Which leagues are worth keeping in a player's career-stats table.
///
/// The NHL's own player-landing endpoint carries a player's entire hockey
/// history, right back through peewee tournaments and 14U showcases (real
/// examples seen in production: "QC Int PW", "GTHL U16", "Brick
/// Invitational", "NAPHL 14U") — noise nobody wants on a career-stats page.
///
/// A whitelist, not a denylist, for the same reason <see
/// cref="FantasyWarrior.Core.Scoring.StatKeys"/> is one: minor/youth league
/// names are effectively unbounded, so an unrecognised one should be
/// silently dropped rather than requiring us to enumerate every possible
/// local association. Extend <see cref="All"/> the moment a real player is
/// missing a legitimate league.
/// </summary>
public static class NotableLeagues
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // pro
        "NHL", "AHL", "ECHL",
        // major junior / North American junior-to-pro pipeline
        "OHL", "WHL", "QMJHL", "USHL", "NCAA",
        // European pro
        "KHL", "VHL", "MHL", "SHL", "HockeyAllsvenskan", "Liiga", "NL", "NLA",
        "DEL", "Czech", "Extraliga", "Champions HL",
    };

    public static bool IsNotable(string? leagueAbbrev) =>
        leagueAbbrev is not null && All.Contains(leagueAbbrev);
}
