using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// Scopes <c>vStandings</c> and <c>vRosterSpotTotals</c> to the league's
    /// current season. Neither ever filtered by season at all — a latent bug
    /// found while designing the off-season draft (<c>season-lifecycle.md</c>
    /// §8), harmless only because no league has ever reached a second season. A
    /// <c>RosterSpot</c> survives a season boundary in a keeper league, so once
    /// one exists, its <c>RosterAssignments</c> span two seasons' worth of
    /// periods, and both views would otherwise sum them together — a GM's
    /// weekly point total would not reset for the new season, it would keep
    /// last year's score baked in forever.
    ///
    /// <b>Both views keep aggregating from the same rows; only the filter
    /// changes.</b> That is the whole point of scoping rather than deleting
    /// (<c>season-lifecycle.md</c> §6): the rollover moves a filter, and a
    /// future lifetime/career feature can still read the very same
    /// <c>RosterAssignments</c> rows unfiltered by season — it would simply not
    /// go through these two views, which now deliberately answer only "this
    /// season".
    ///
    /// The join is a <c>LEFT JOIN</c> chain, not an inner one and not a bare
    /// <c>WHERE</c>: a roster spot with no assignments yet (a fresh free-agent
    /// pickup, or a spot just opened by the steal draft) must still produce a
    /// row of zeroes rather than vanish from the view, exactly as it did
    /// before this migration.
    /// </summary>
    public partial class ScopeStandingsAndRosterTotalsBySeason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vRosterSpotTotals];");
            migrationBuilder.Sql("DROP VIEW [vStandings];");

            migrationBuilder.Sql("""
                CREATE VIEW [vRosterSpotTotals] AS
                SELECT
                    sp.[RosterSpotId],
                    sp.[LeagueId],
                    sp.[TeamId],
                    sp.[PlayerId],
                    sp.[FranchiseAbbrev],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [ActivePoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchPoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [ActiveGamesPlayed],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Goals]         END), 0) AS [ActiveGoals],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Assists]       END), 0) AS [ActiveAssists],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamWins]     END), 0) AS [ActiveTeamWins],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamLosses]   END), 0) AS [ActiveTeamLosses],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamOtLosses] END), 0) AS [ActiveTeamOtLosses],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 AND a.[IsFinalized] = 1
                                    THEN a.[FantasyPoints] END), 0)                       AS [FinalizedActivePoints]
                FROM [RosterSpots] sp
                JOIN [Leagues] l ON l.[LeagueId] = sp.[LeagueId]
                LEFT JOIN [RosterAssignments] a ON a.[RosterSpotId] = sp.[RosterSpotId]
                LEFT JOIN [Periods] p ON p.[PeriodId] = a.[PeriodId]
                -- A spot with no assignments yet (a is NULL, so p is NULL too)
                -- must still produce a row of zeroes, not disappear.
                WHERE a.[RosterAssignmentId] IS NULL OR p.[Season] = l.[Season]
                GROUP BY sp.[RosterSpotId], sp.[LeagueId], sp.[TeamId], sp.[PlayerId], sp.[FranchiseAbbrev];
                """);

            migrationBuilder.Sql("""
                CREATE VIEW [vStandings] AS
                WITH [Today] AS (
                    SELECT ISNULL(
                        (SELECT DATEADD(day, 1, s.[AsOfDate]) FROM [SimulationState] s WHERE s.[Enabled] = 1),
                        CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Eastern Standard Time' AS date)) AS [D]
                ),
                [Scoring] AS (
                    SELECT
                        sp.[TeamId],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [Score],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 AND a.[IsFinalized] = 1
                                        THEN a.[FantasyPoints] END), 0)                       AS [FinalizedScore],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchScore],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [RosterGamesPlayed]
                    FROM [RosterAssignments] a
                    JOIN [RosterSpots] sp ON sp.[RosterSpotId] = a.[RosterSpotId]
                    -- The only line this migration adds to vStandings: a spot
                    -- that outlives a season boundary must not carry last
                    -- season's banked points into this season's score.
                    JOIN [Periods] p ON p.[PeriodId] = a.[PeriodId]
                    JOIN [Leagues] l2 ON l2.[LeagueId] = sp.[LeagueId] AND l2.[Season] = p.[Season]
                    GROUP BY sp.[TeamId]
                ),
                [Roster] AS (
                    SELECT
                        sp.[TeamId],
                        COUNT(CASE WHEN sp.[StartDate] <= td.[D]
                                    AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                   THEN 1 END)                                          AS [PlayerCount],
                        SUM(CASE WHEN sp.[StartDate] <= td.[D]
                                  AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                 THEN ISNULL(c.[CapHit], l.[DefaultCapHit]) END)        AS [CapTotal],
                        SUM(CASE WHEN sp.[StartDate] <= td.[D]
                                  AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                  AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [UnknownContracts],
                        COUNT(CASE WHEN sp.[EndDate] IS NULL THEN 1 END)                AS [EngagedPlayerCount],
                        SUM(CASE WHEN sp.[EndDate] IS NULL
                                 THEN ISNULL(c.[CapHit], l.[DefaultCapHit]) END)        AS [EngagedCapTotal],
                        SUM(CASE WHEN sp.[EndDate] IS NULL AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [EngagedUnknownContracts]
                    FROM [RosterSpots] sp
                    CROSS JOIN [Today] td
                    JOIN [Teams]   t ON t.[TeamId]   = sp.[TeamId]
                    JOIN [Leagues] l ON l.[LeagueId] = t.[LeagueId]
                    LEFT JOIN [PlayerContracts] c
                           ON c.[PlayerId] = sp.[PlayerId] AND c.[Season] = l.[Season]
                    WHERE (sp.[EndDate] IS NULL
                           OR (sp.[StartDate] <= td.[D] AND sp.[EndDate] >= td.[D]))
                      AND sp.[PositionGroup] <> 'T'
                    GROUP BY sp.[TeamId]
                )
                SELECT
                    t.[TeamId],
                    t.[LeagueId],
                    ISNULL(s.[Score], 0)                                 AS [Score],
                    ISNULL(s.[FinalizedScore], 0)                        AS [FinalizedScore],
                    ISNULL(s.[Score], 0) - ISNULL(s.[FinalizedScore], 0) AS [LivePoints],
                    ISNULL(s.[BenchScore], 0)                            AS [BenchScore],
                    ISNULL(s.[RosterGamesPlayed], 0)                     AS [RosterGamesPlayed],
                    ISNULL(r.[PlayerCount], 0)                           AS [PlayerCount],
                    ISNULL(r.[CapTotal], 0)                              AS [CapTotal],
                    ISNULL(r.[UnknownContracts], 0)                      AS [UnknownContracts],
                    ISNULL(r.[EngagedPlayerCount], 0)                    AS [EngagedPlayerCount],
                    ISNULL(r.[EngagedCapTotal], 0)                       AS [EngagedCapTotal],
                    ISNULL(r.[EngagedUnknownContracts], 0)               AS [EngagedUnknownContracts]
                FROM [Teams] t
                LEFT JOIN [Scoring] s ON s.[TeamId] = t.[TeamId]
                LEFT JOIN [Roster]  r ON r.[TeamId] = t.[TeamId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vRosterSpotTotals];");
            migrationBuilder.Sql("DROP VIEW [vStandings];");

            migrationBuilder.Sql("""
                CREATE VIEW [vRosterSpotTotals] AS
                SELECT
                    sp.[RosterSpotId],
                    sp.[LeagueId],
                    sp.[TeamId],
                    sp.[PlayerId],
                    sp.[FranchiseAbbrev],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [ActivePoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchPoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [ActiveGamesPlayed],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Goals]         END), 0) AS [ActiveGoals],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Assists]       END), 0) AS [ActiveAssists],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamWins]     END), 0) AS [ActiveTeamWins],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamLosses]   END), 0) AS [ActiveTeamLosses],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamOtLosses] END), 0) AS [ActiveTeamOtLosses],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 AND a.[IsFinalized] = 1
                                    THEN a.[FantasyPoints] END), 0)                       AS [FinalizedActivePoints]
                FROM [RosterSpots] sp
                LEFT JOIN [RosterAssignments] a ON a.[RosterSpotId] = sp.[RosterSpotId]
                GROUP BY sp.[RosterSpotId], sp.[LeagueId], sp.[TeamId], sp.[PlayerId], sp.[FranchiseAbbrev];
                """);

            migrationBuilder.Sql("""
                CREATE VIEW [vStandings] AS
                WITH [Today] AS (
                    SELECT ISNULL(
                        (SELECT DATEADD(day, 1, s.[AsOfDate]) FROM [SimulationState] s WHERE s.[Enabled] = 1),
                        CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Eastern Standard Time' AS date)) AS [D]
                ),
                [Scoring] AS (
                    SELECT
                        sp.[TeamId],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [Score],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 AND a.[IsFinalized] = 1
                                        THEN a.[FantasyPoints] END), 0)                       AS [FinalizedScore],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchScore],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [RosterGamesPlayed]
                    FROM [RosterAssignments] a
                    JOIN [RosterSpots] sp ON sp.[RosterSpotId] = a.[RosterSpotId]
                    GROUP BY sp.[TeamId]
                ),
                [Roster] AS (
                    SELECT
                        sp.[TeamId],
                        COUNT(CASE WHEN sp.[StartDate] <= td.[D]
                                    AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                   THEN 1 END)                                          AS [PlayerCount],
                        SUM(CASE WHEN sp.[StartDate] <= td.[D]
                                  AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                 THEN ISNULL(c.[CapHit], l.[DefaultCapHit]) END)        AS [CapTotal],
                        SUM(CASE WHEN sp.[StartDate] <= td.[D]
                                  AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                  AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [UnknownContracts],
                        COUNT(CASE WHEN sp.[EndDate] IS NULL THEN 1 END)                AS [EngagedPlayerCount],
                        SUM(CASE WHEN sp.[EndDate] IS NULL
                                 THEN ISNULL(c.[CapHit], l.[DefaultCapHit]) END)        AS [EngagedCapTotal],
                        SUM(CASE WHEN sp.[EndDate] IS NULL AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [EngagedUnknownContracts]
                    FROM [RosterSpots] sp
                    CROSS JOIN [Today] td
                    JOIN [Teams]   t ON t.[TeamId]   = sp.[TeamId]
                    JOIN [Leagues] l ON l.[LeagueId] = t.[LeagueId]
                    LEFT JOIN [PlayerContracts] c
                           ON c.[PlayerId] = sp.[PlayerId] AND c.[Season] = l.[Season]
                    WHERE (sp.[EndDate] IS NULL
                           OR (sp.[StartDate] <= td.[D] AND sp.[EndDate] >= td.[D]))
                      AND sp.[PositionGroup] <> 'T'
                    GROUP BY sp.[TeamId]
                )
                SELECT
                    t.[TeamId],
                    t.[LeagueId],
                    ISNULL(s.[Score], 0)                                 AS [Score],
                    ISNULL(s.[FinalizedScore], 0)                        AS [FinalizedScore],
                    ISNULL(s.[Score], 0) - ISNULL(s.[FinalizedScore], 0) AS [LivePoints],
                    ISNULL(s.[BenchScore], 0)                            AS [BenchScore],
                    ISNULL(s.[RosterGamesPlayed], 0)                     AS [RosterGamesPlayed],
                    ISNULL(r.[PlayerCount], 0)                           AS [PlayerCount],
                    ISNULL(r.[CapTotal], 0)                              AS [CapTotal],
                    ISNULL(r.[UnknownContracts], 0)                      AS [UnknownContracts],
                    ISNULL(r.[EngagedPlayerCount], 0)                    AS [EngagedPlayerCount],
                    ISNULL(r.[EngagedCapTotal], 0)                       AS [EngagedCapTotal],
                    ISNULL(r.[EngagedUnknownContracts], 0)               AS [EngagedUnknownContracts]
                FROM [Teams] t
                LEFT JOIN [Scoring] s ON s.[TeamId] = t.[TeamId]
                LEFT JOIN [Roster]  r ON r.[TeamId] = t.[TeamId];
                """);
        }
    }
}
