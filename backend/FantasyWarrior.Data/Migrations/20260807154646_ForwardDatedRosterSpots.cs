using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// Teaches <c>vStandings</c> what day it is, and folds
    /// <c>vTeamCommitments</c> into it.
    ///
    /// <b>Why now.</b> Accepting a trade used to leave the rosters alone and
    /// wait for the nightly job, so "spot never closed" and "spot held today"
    /// were the same question and <c>EndDate IS NULL</c> answered both. A trade
    /// now applies at acceptance, dated to the next week boundary, which splits
    /// them apart for exactly the two spots a trade creates: the man leaving
    /// Sunday still counts today and is already gone from the commitment, and
    /// the man arriving Monday is the reverse.
    ///
    /// So the view needs a date. It reads the same cursor every other reader
    /// does — <c>SimulationState</c>, one row, <c>AsOfDate + 1</c> being the
    /// simulated game day exactly as <c>SimulationClockService</c> derives it —
    /// and falls back to the real Eastern date when no replay is running. This
    /// is the first date logic in any view in the schema; it is here rather
    /// than in C# because the cap is read from three places that would
    /// otherwise each have to remember to bound it.
    ///
    /// <c>vTeamCommitments</c> carried a delta to add to the standings cap.
    /// That delta is now in the spots' own dates, so the engaged figures are
    /// the same aggregate one filter apart, in the same statement, where they
    /// cannot drift from the total they used to be added to.
    ///
    /// The <c>Scoring</c> CTE is untouched: it sums assignments, which only
    /// exist for weeks that have been reached, and a forward-dated spot has
    /// none of them yet.
    /// </summary>
    public partial class ForwardDatedRosterSpots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vTeamCommitments];");
            migrationBuilder.Sql("DROP VIEW [vStandings];");
            migrationBuilder.Sql(StandingsWithToday);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vStandings];");
            migrationBuilder.Sql(StandingsOnOpenSpots);
            migrationBuilder.Sql(TeamCommitments);
        }

        /// <summary>
        /// The simulated game day, or the real Eastern one. Cross-joined into
        /// the roster aggregate below rather than repeated in each predicate,
        /// so a single row is resolved once per query.
        /// </summary>
        private const string TodayCte = """
            [Today] AS (
                SELECT ISNULL(
                    (SELECT DATEADD(day, 1, s.[AsOfDate]) FROM [SimulationState] s WHERE s.[Enabled] = 1),
                    CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Eastern Standard Time' AS date)) AS [D]
            )
            """;

        private const string StandingsWithToday = $"""
            CREATE VIEW [vStandings] AS
            WITH {TodayCte},
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
                    -- Held today: what the league table shows and what the cap
                    -- gauge is charged for. A player promised away is still
                    -- here until the day he leaves.
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
                    -- Engaged: the roster once every accepted trade has landed.
                    -- "Never closed" is exactly that question now, which is why
                    -- this half needs no date at all.
                    COUNT(CASE WHEN sp.[EndDate] IS NULL THEN 1 END)                AS [EngagedPlayerCount],
                    SUM(CASE WHEN sp.[EndDate] IS NULL
                             THEN ISNULL(c.[CapHit], l.[DefaultCapHit]) END)        AS [EngagedCapTotal],
                    SUM(CASE WHEN sp.[EndDate] IS NULL AND c.[CapHit] IS NULL
                             THEN 1 ELSE 0 END)                                     AS [EngagedUnknownContracts]
                FROM [RosterSpots] sp
                CROSS JOIN [Today] td
                JOIN [Teams]   t ON t.[TeamId]   = sp.[TeamId]
                JOIN [Leagues] l ON l.[LeagueId] = t.[LeagueId]
                -- The contract for the season the league is actually playing. A
                -- player with none on file is charged the league's default
                -- rather than nothing, and counted in UnknownContracts so a
                -- screen can say how much of the total is assumed.
                LEFT JOIN [PlayerContracts] c
                       ON c.[PlayerId] = sp.[PlayerId] AND c.[Season] = l.[Season]
                -- Every spot either side of the two aggregates needs; a spot
                -- that closed in the past is in neither. An Équipe spot is not
                -- a player and costs no cap, so it is out of both.
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
            """;

        /// <summary>The pre-forward-dating definition, verbatim from TeamSlot.</summary>
        private const string StandingsOnOpenSpots = """
            CREATE VIEW [vStandings] AS
            WITH [Scoring] AS (
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
                    COUNT(*)                                             AS [PlayerCount],
                    SUM(ISNULL(c.[CapHit], l.[DefaultCapHit]))           AS [CapTotal],
                    SUM(CASE WHEN c.[CapHit] IS NULL THEN 1 ELSE 0 END)  AS [UnknownContracts]
                FROM [RosterSpots] sp
                JOIN [Teams]   t ON t.[TeamId]   = sp.[TeamId]
                JOIN [Leagues] l ON l.[LeagueId] = t.[LeagueId]
                LEFT JOIN [PlayerContracts] c
                       ON c.[PlayerId] = sp.[PlayerId] AND c.[Season] = l.[Season]
                WHERE sp.[EndDate] IS NULL AND sp.[PositionGroup] <> 'T'
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
                ISNULL(r.[UnknownContracts], 0)                      AS [UnknownContracts]
            FROM [Teams] t
            LEFT JOIN [Scoring] s ON s.[TeamId] = t.[TeamId]
            LEFT JOIN [Roster]  r ON r.[TeamId] = t.[TeamId];
            """;

        /// <summary>The delta view this replaces, verbatim from TeamCommitmentsView.</summary>
        private const string TeamCommitments = """
            CREATE VIEW [vTeamCommitments] AS
            SELECT
                t.[TeamId],
                t.[LeagueId],
                SUM(CASE WHEN a.[ToTeamId] = t.[TeamId] THEN ISNULL(c.[CapHit], 0)
                                                        ELSE -ISNULL(c.[CapHit], 0) END) AS [CapDelta],
                SUM(CASE WHEN a.[ToTeamId] = t.[TeamId] THEN 1 ELSE -1 END) AS [CountDelta]
            FROM [Teams] t
            JOIN [Leagues] l
              ON l.[LeagueId] = t.[LeagueId]
            JOIN [Trades] tr
              ON tr.[LeagueId] = t.[LeagueId]
             AND tr.[Status] = 3          -- TradeStatus.Accepted
            JOIN [TradeAssets] a
              ON a.[TradeId] = tr.[TradeId]
             AND a.[AssetType] = 0        -- TradeAssetType.Player
             AND (a.[FromTeamId] = t.[TeamId] OR a.[ToTeamId] = t.[TeamId])
            LEFT JOIN [PlayerContracts] c
                   ON c.[PlayerId] = a.[PlayerId] AND c.[Season] = l.[Season]
            GROUP BY t.[TeamId], t.[LeagueId];
            """;
    }
}
