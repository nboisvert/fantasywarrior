using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// Removes the old home of a league's rules: ten columns on <c>Leagues</c>
    /// and the <c>LeagueScoringRules</c> table. They now live as one document
    /// per season on <c>LeagueSeasons.Rules</c>, and nothing has read them since
    /// the consumers were rewired.
    ///
    /// <b>It refuses to run against a database that has not been converted.</b>
    /// See the guard below — after this, the columns the conversion reads are
    /// gone, so a season still holding the <c>'{}'</c> default could never be
    /// converted again.
    ///
    /// <c>vStandings</c> is dropped and recreated in the same migration, because
    /// it reads what a player with no contract costs and that number moved. In
    /// two migrations it would be a view over a column that no longer exists.
    /// </summary>
    public partial class DropLegacyLeagueRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // **The one-way door, guarded.** After this migration the columns
            // these rules came from are gone, so a LeagueSeason still holding
            // the '{}' default can never be converted again — and every
            // consumer refuses on an unwritten document, which would leave the
            // pool unable to trade, score or draft with no way back but a
            // restore. Refusing here turns a silent trap into a stopped deploy
            // that names its own fix.
            //
            // A fresh database passes trivially: seed-mordus and league
            // creation both write the document as they build the season.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM [LeagueSeasons]
                    WHERE ISNULL(TRY_CAST(JSON_VALUE([Rules], '$.version') AS int), 0) < 1)
                    THROW 50000,
                        'Some LeagueSeasons still have no rules document. Run `rules-backfill` from the previous release before applying this migration: after it, the columns it reads are gone.',
                        1;
                """);

            // The view reads Leagues.DefaultCapHit, so it has to go before the
            // column does and come back reading the new home. Both in this one
            // migration: between two, it would be a view over a column that no
            // longer exists.
            migrationBuilder.Sql("DROP VIEW [vStandings];");

            migrationBuilder.DropTable(
                name: "LeagueScoringRules");

            migrationBuilder.DropColumn(
                name: "ActiveDefense",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "ActiveForwards",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "ActiveGoalies",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "CapAmount",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "DefaultCapHit",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "DraftRounds",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "MaxLossesPerTeam",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "ProtectionSlots",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "RosterMax",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "RosterMin",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "StealRounds",
                table: "Leagues");

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
                    -- A spot that outlives a season boundary must not carry last
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
                    -- What a player with no contract costs is a league RULE, and
                    -- rules live on the season being scored. An INNER join, so a
                    -- league whose rules were never written produces no Roster
                    -- row at all: the cap panel reads zero, which is visibly
                    -- wrong. A LEFT join would make the default NULL, SUM would
                    -- skip it, and the total would be quietly understated by
                    -- exactly the unsigned players -- the harder failure to see.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vStandings];");

            migrationBuilder.AddColumn<int>(
                name: "ActiveDefense",
                table: "Leagues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ActiveForwards",
                table: "Leagues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ActiveGoalies",
                table: "Leagues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "CapAmount",
                table: "Leagues",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DefaultCapHit",
                table: "Leagues",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "DraftRounds",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLossesPerTeam",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProtectionSlots",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RosterMax",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RosterMin",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StealRounds",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeagueScoringRules",
                columns: table => new
                {
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    StatKey = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PointValue = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueScoringRules", x => new { x.LeagueId, x.StatKey });
                    table.ForeignKey(
                        name: "FK_LeagueScoringRules_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                });

            // The view as it stood before, reading Leagues.DefaultCapHit — the
            // column the block above has just restored.
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
