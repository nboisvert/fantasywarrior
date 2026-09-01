using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStandingsGoalsAssistsAndSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeamStandingsSnapshots",
                columns: table => new
                {
                    TeamStandingsSnapshotId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    LastNightPoints = table.Column<double>(type: "float", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamStandingsSnapshots", x => x.TeamStandingsSnapshotId);
                    table.ForeignKey(
                        name: "FK_TeamStandingsSnapshots_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeamStandingsSnapshots_TeamId_AsOfDateDesc",
                table: "TeamStandingsSnapshots",
                columns: new[] { "TeamId", "AsOfDate" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "Rank", "LastNightPoints" });

            migrationBuilder.CreateIndex(
                name: "UX_TeamStandingsSnapshots_TeamId_AsOfDate",
                table: "TeamStandingsSnapshots",
                columns: new[] { "TeamId", "AsOfDate" },
                unique: true);

            // vStandings: add raw skater totals (Goals/Assists) alongside the
            // existing fantasy-scoring aggregates, for the Standings screen's
            // new Fantasy column group. Same drop+recreate pattern the
            // previous vStandings migration itself used.
            migrationBuilder.Sql("DROP VIEW [vStandings];");
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
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [RosterGamesPlayed],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Goals]         END), 0) AS [RosterGoals],
                        ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Assists]       END), 0) AS [RosterAssists]
                    FROM [RosterAssignments] a
                    JOIN [RosterSpots] sp ON sp.[RosterSpotId] = a.[RosterSpotId]
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
                                 THEN ISNULL(c.[CapHit], ls.[DefaultCapHit]) END)       AS [CapTotal],
                        SUM(CASE WHEN sp.[StartDate] <= td.[D]
                                  AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                  AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [UnknownContracts],
                        COUNT(CASE WHEN sp.[EndDate] IS NULL THEN 1 END)                AS [EngagedPlayerCount],
                        SUM(CASE WHEN sp.[EndDate] IS NULL
                                 THEN ISNULL(c.[CapHit], ls.[DefaultCapHit]) END)       AS [EngagedCapTotal],
                        SUM(CASE WHEN sp.[EndDate] IS NULL AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [EngagedUnknownContracts]
                    FROM [RosterSpots] sp
                    CROSS JOIN [Today] td
                    JOIN [Teams]   t ON t.[TeamId]   = sp.[TeamId]
                    JOIN [Leagues] l ON l.[LeagueId] = t.[LeagueId]
                    JOIN (
                        SELECT
                            s.[LeagueId],
                            s.[Season],
                            CAST(JSON_VALUE(s.[Rules], '$.cap.defaultCapHit') AS bigint) AS [DefaultCapHit]
                        FROM [LeagueSeasons] s
                        WHERE JSON_VALUE(s.[Rules], '$.cap.defaultCapHit') IS NOT NULL
                    ) ls ON ls.[LeagueId] = l.[LeagueId] AND ls.[Season] = l.[Season]
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
                    ISNULL(s.[RosterGoals], 0)                           AS [RosterGoals],
                    ISNULL(s.[RosterAssists], 0)                         AS [RosterAssists],
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
            // Restores vStandings to exactly DropLegacyLeagueRules.Up()'s
            // version — the one truly live before this migration, not the
            // older Leagues.DefaultCapHit-based one two migrations back.
            migrationBuilder.Sql("DROP VIEW [vStandings];");
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
                                 THEN ISNULL(c.[CapHit], ls.[DefaultCapHit]) END)       AS [CapTotal],
                        SUM(CASE WHEN sp.[StartDate] <= td.[D]
                                  AND (sp.[EndDate] IS NULL OR sp.[EndDate] >= td.[D])
                                  AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [UnknownContracts],
                        COUNT(CASE WHEN sp.[EndDate] IS NULL THEN 1 END)                AS [EngagedPlayerCount],
                        SUM(CASE WHEN sp.[EndDate] IS NULL
                                 THEN ISNULL(c.[CapHit], ls.[DefaultCapHit]) END)       AS [EngagedCapTotal],
                        SUM(CASE WHEN sp.[EndDate] IS NULL AND c.[CapHit] IS NULL
                                 THEN 1 ELSE 0 END)                                     AS [EngagedUnknownContracts]
                    FROM [RosterSpots] sp
                    CROSS JOIN [Today] td
                    JOIN [Teams]   t ON t.[TeamId]   = sp.[TeamId]
                    JOIN [Leagues] l ON l.[LeagueId] = t.[LeagueId]
                    JOIN (
                        SELECT
                            s.[LeagueId],
                            s.[Season],
                            CAST(JSON_VALUE(s.[Rules], '$.cap.defaultCapHit') AS bigint) AS [DefaultCapHit]
                        FROM [LeagueSeasons] s
                        WHERE JSON_VALUE(s.[Rules], '$.cap.defaultCapHit') IS NOT NULL
                    ) ls ON ls.[LeagueId] = l.[LeagueId] AND ls.[Season] = l.[Season]
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

            migrationBuilder.DropTable(
                name: "TeamStandingsSnapshots");
        }
    }
}
