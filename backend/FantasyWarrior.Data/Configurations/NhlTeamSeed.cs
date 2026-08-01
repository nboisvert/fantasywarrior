using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Data.Configurations;

/// <summary>
/// The 32 NHL franchises, as of the 2025-26 season.
///
/// Seeded into the migration rather than fetched at runtime: every game, player
/// and roster row carries a team abbreviation as a foreign key, so the table has
/// to be populated before anything else can be inserted. It also genuinely is
/// static — the last change was Utah, and a future one is a two-line migration.
///
/// Logo URLs follow the NHL's own stable asset path.
/// </summary>
public static class NhlTeamSeed
{
    public static IReadOnlyList<NhlTeam> All { get; } =
    [
        // Eastern — Atlantic
        Team("BOS", "Boston Bruins", "Eastern", "Atlantic"),
        Team("BUF", "Buffalo Sabres", "Eastern", "Atlantic"),
        Team("DET", "Detroit Red Wings", "Eastern", "Atlantic"),
        Team("FLA", "Florida Panthers", "Eastern", "Atlantic"),
        Team("MTL", "Montreal Canadiens", "Eastern", "Atlantic"),
        Team("OTT", "Ottawa Senators", "Eastern", "Atlantic"),
        Team("TBL", "Tampa Bay Lightning", "Eastern", "Atlantic"),
        Team("TOR", "Toronto Maple Leafs", "Eastern", "Atlantic"),

        // Eastern — Metropolitan
        Team("CAR", "Carolina Hurricanes", "Eastern", "Metropolitan"),
        Team("CBJ", "Columbus Blue Jackets", "Eastern", "Metropolitan"),
        Team("NJD", "New Jersey Devils", "Eastern", "Metropolitan"),
        Team("NYI", "New York Islanders", "Eastern", "Metropolitan"),
        Team("NYR", "New York Rangers", "Eastern", "Metropolitan"),
        Team("PHI", "Philadelphia Flyers", "Eastern", "Metropolitan"),
        Team("PIT", "Pittsburgh Penguins", "Eastern", "Metropolitan"),
        Team("WSH", "Washington Capitals", "Eastern", "Metropolitan"),

        // Western — Central
        Team("CHI", "Chicago Blackhawks", "Western", "Central"),
        Team("COL", "Colorado Avalanche", "Western", "Central"),
        Team("DAL", "Dallas Stars", "Western", "Central"),
        Team("MIN", "Minnesota Wild", "Western", "Central"),
        Team("NSH", "Nashville Predators", "Western", "Central"),
        Team("STL", "St. Louis Blues", "Western", "Central"),
        Team("UTA", "Utah Mammoth", "Western", "Central"),
        Team("WPG", "Winnipeg Jets", "Western", "Central"),

        // Western — Pacific
        Team("ANA", "Anaheim Ducks", "Western", "Pacific"),
        Team("CGY", "Calgary Flames", "Western", "Pacific"),
        Team("EDM", "Edmonton Oilers", "Western", "Pacific"),
        Team("LAK", "Los Angeles Kings", "Western", "Pacific"),
        Team("SEA", "Seattle Kraken", "Western", "Pacific"),
        Team("SJS", "San Jose Sharks", "Western", "Pacific"),
        Team("VAN", "Vancouver Canucks", "Western", "Pacific"),
        Team("VGK", "Vegas Golden Knights", "Western", "Pacific"),
    ];

    private static NhlTeam Team(string abbrev, string name, string conference, string division) => new()
    {
        Abbrev = abbrev,
        Name = name,
        ConferenceName = conference,
        DivisionName = division,
        LogoUrl = $"https://assets.nhle.com/logos/nhl/svg/{abbrev}_light.svg",
    };
}
