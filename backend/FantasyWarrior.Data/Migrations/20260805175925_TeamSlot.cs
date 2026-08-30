using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// The Équipe slot becomes a real roster spot.
    ///
    /// Every GM in Les Mordus owns one NHL franchise for life — the PDF's `E`
    /// line. It lived on <c>Teams.FranchiseAbbrev</c>, carried identity and
    /// scored nothing: the last unimplemented rule in the pool. Nick set it on
    /// 2026-08-05 — a franchise banks its own record, 2 per win and 1 per
    /// overtime loss here, priced per league like every other stat.
    ///
    /// <b>This reverses the modelling decision taken earlier the same day</b>
    /// which kept the franchise off <c>RosterSpots</c> to
    /// avoid a polymorphic spot "contaminating the whole roster, lineup and
    /// transaction model". The reversal is what that shape is worth: a
    /// franchise behaves like a player in every way the system cares about — it
    /// opens a spot, produces one assignment per week, banks points, and can be
    /// traded. A parallel model would have meant a second scoring engine, a
    /// second trade path and a second grid. This is one nullable column and one
    /// CHECK constraint.
    ///
    /// Three unique indexes carry the rules that would otherwise be prose:
    /// one owner per franchise per league, one Équipe slot per team (which is
    /// exactly why the slot has no active/bench control), and the existing
    /// one-owner-per-player index re-filtered on <c>PlayerId IS NOT NULL</c> —
    /// without which the *second* team in a league to open a franchise spot
    /// would collide with the first, since SQL Server treats two NULLs as equal
    /// in a unique index.
    ///
    /// <c>vStandings</c> moves with it: an Équipe spot is not a player and
    /// costs no cap, so it is excluded from <c>PlayerCount</c>,
    /// <c>CapTotal</c> and <c>UnknownContracts</c> — otherwise every franchise
    /// would be billed the league's <c>DefaultCapHit</c> for the contract it
    /// does not have, and the roster-size rule would count fifteen players
    /// where there are fourteen. Its points still count: the <c>Scoring</c> CTE
    /// is untouched. <c>vTeamCommitments</c> needs no change — it is already
    /// scoped to <c>AssetType = 0</c>, so a franchise asset never enters it.
    /// </summary>
    public partial class TeamSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TradeAssets_ExactlyOneAsset",
                table: "TradeAssets");

            migrationBuilder.DropIndex(
                name: "UX_RosterSpots_OneOpenSpotPerPlayerPerLeague",
                table: "RosterSpots");

            migrationBuilder.AddColumn<string>(
                name: "FranchiseAbbrev",
                table: "TradeAssets",
                type: "char(3)",
                unicode: false,
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "PlayerId",
                table: "RosterSpots",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "FranchiseAbbrev",
                table: "RosterSpots",
                type: "char(3)",
                unicode: false,
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamLosses",
                table: "RosterAssignments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamOtLosses",
                table: "RosterAssignments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TeamWins",
                table: "RosterAssignments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TradeAssets_FranchiseAbbrev",
                table: "TradeAssets",
                column: "FranchiseAbbrev");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TradeAssets_ExactlyOneAsset",
                table: "TradeAssets",
                sql: "([AssetType] = 0 AND [PlayerId] IS NOT NULL AND [DraftPickId] IS NULL AND [FranchiseAbbrev] IS NULL) OR ([AssetType] = 1 AND [DraftPickId] IS NOT NULL AND [PlayerId] IS NULL AND [FranchiseAbbrev] IS NULL) OR ([AssetType] = 2 AND [FranchiseAbbrev] IS NOT NULL AND [PlayerId] IS NULL AND [DraftPickId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_FranchiseAbbrev",
                table: "RosterSpots",
                column: "FranchiseAbbrev");

            migrationBuilder.CreateIndex(
                name: "UX_RosterSpots_OneOpenFranchisePerLeague",
                table: "RosterSpots",
                columns: new[] { "LeagueId", "FranchiseAbbrev" },
                unique: true,
                filter: "[EndDate] IS NULL AND [FranchiseAbbrev] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RosterSpots_OneOpenFranchisePerTeam",
                table: "RosterSpots",
                column: "TeamId",
                unique: true,
                filter: "[EndDate] IS NULL AND [PositionGroup] = 'T'");

            migrationBuilder.CreateIndex(
                name: "UX_RosterSpots_OneOpenSpotPerPlayerPerLeague",
                table: "RosterSpots",
                columns: new[] { "LeagueId", "PlayerId" },
                unique: true,
                filter: "[EndDate] IS NULL AND [PlayerId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RosterSpots_PlayerOrFranchise",
                table: "RosterSpots",
                sql: "([PositionGroup] = 'T' AND [FranchiseAbbrev] IS NOT NULL AND [PlayerId] IS NULL) OR ([PositionGroup] <> 'T' AND [PlayerId] IS NOT NULL AND [FranchiseAbbrev] IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_RosterSpots_NhlTeams_FranchiseAbbrev",
                table: "RosterSpots",
                column: "FranchiseAbbrev",
                principalTable: "NhlTeams",
                principalColumn: "Abbrev");

            migrationBuilder.AddForeignKey(
                name: "FK_TradeAssets_NhlTeams_FranchiseAbbrev",
                table: "TradeAssets",
                column: "FranchiseAbbrev",
                principalTable: "NhlTeams",
                principalColumn: "Abbrev");

            RebuildViews(migrationBuilder, franchisesCountAsPlayers: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // First, so no view is left referring to a column that is about to
            // be dropped. Rebuilt in the "franchises are ordinary spots" shape,
            // which is what the pre-migration schema meant: there were none.
            RebuildViews(migrationBuilder, franchisesCountAsPlayers: true);

            migrationBuilder.DropForeignKey(
                name: "FK_RosterSpots_NhlTeams_FranchiseAbbrev",
                table: "RosterSpots");

            migrationBuilder.DropForeignKey(
                name: "FK_TradeAssets_NhlTeams_FranchiseAbbrev",
                table: "TradeAssets");

            migrationBuilder.DropIndex(
                name: "IX_TradeAssets_FranchiseAbbrev",
                table: "TradeAssets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TradeAssets_ExactlyOneAsset",
                table: "TradeAssets");

            migrationBuilder.DropIndex(
                name: "IX_RosterSpots_FranchiseAbbrev",
                table: "RosterSpots");

            migrationBuilder.DropIndex(
                name: "UX_RosterSpots_OneOpenFranchisePerLeague",
                table: "RosterSpots");

            migrationBuilder.DropIndex(
                name: "UX_RosterSpots_OneOpenFranchisePerTeam",
                table: "RosterSpots");

            migrationBuilder.DropIndex(
                name: "UX_RosterSpots_OneOpenSpotPerPlayerPerLeague",
                table: "RosterSpots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RosterSpots_PlayerOrFranchise",
                table: "RosterSpots");

            migrationBuilder.DropColumn(
                name: "FranchiseAbbrev",
                table: "TradeAssets");

            migrationBuilder.DropColumn(
                name: "FranchiseAbbrev",
                table: "RosterSpots");

            migrationBuilder.DropColumn(
                name: "TeamLosses",
                table: "RosterAssignments");

            migrationBuilder.DropColumn(
                name: "TeamOtLosses",
                table: "RosterAssignments");

            migrationBuilder.DropColumn(
                name: "TeamWins",
                table: "RosterAssignments");

            migrationBuilder.AlterColumn<long>(
                name: "PlayerId",
                table: "RosterSpots",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TradeAssets_ExactlyOneAsset",
                table: "TradeAssets",
                sql: "([AssetType] = 0 AND [PlayerId] IS NOT NULL AND [DraftPickId] IS NULL) OR ([AssetType] = 1 AND [DraftPickId] IS NOT NULL AND [PlayerId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_RosterSpots_OneOpenSpotPerPlayerPerLeague",
                table: "RosterSpots",
                columns: new[] { "LeagueId", "PlayerId" },
                unique: true,
                filter: "[EndDate] IS NULL");
        }

        /// <summary>
        /// The two views that read <c>RosterSpots</c> row by row, parameterised
        /// by whether an Équipe spot counts as a player — so Up and Down cannot
        /// drift apart. Same technique as the DefaultCapHit migration that
        /// precedes this one.
        ///
        /// <c>vTeamPeriodScores</c> and <c>vTeamCommitments</c> are deliberately
        /// untouched: the first sums assignments and should include the
        /// franchise's points, the second is already scoped to player assets.
        /// </summary>
        private static void RebuildViews(MigrationBuilder migrationBuilder, bool franchisesCountAsPlayers)
        {
            // "AND it is not a franchise", or nothing at all on the way down.
            var playersOnly = franchisesCountAsPlayers ? "" : " AND sp.[PositionGroup] <> 'T'";
            var franchiseColumn = franchisesCountAsPlayers ? "" : "sp.[FranchiseAbbrev],";
            var franchiseGroupBy = franchisesCountAsPlayers ? "" : ", sp.[FranchiseAbbrev]";
            var teamRecord = franchisesCountAsPlayers
                ? ""
                : """
                      ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamWins]     END), 0) AS [ActiveTeamWins],
                      ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamLosses]   END), 0) AS [ActiveTeamLosses],
                      ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[TeamOtLosses] END), 0) AS [ActiveTeamOtLosses],
                  """;

            migrationBuilder.Sql("DROP VIEW [vRosterSpotTotals];");
            migrationBuilder.Sql($"""
                CREATE VIEW [vRosterSpotTotals] AS
                SELECT
                    sp.[RosterSpotId],
                    sp.[LeagueId],
                    sp.[TeamId],
                    sp.[PlayerId],
                    {franchiseColumn}
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [ActivePoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchPoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [ActiveGamesPlayed],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Goals]         END), 0) AS [ActiveGoals],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Assists]       END), 0) AS [ActiveAssists],
                {teamRecord}
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 AND a.[IsFinalized] = 1
                                    THEN a.[FantasyPoints] END), 0)                       AS [FinalizedActivePoints]
                FROM [RosterSpots] sp
                LEFT JOIN [RosterAssignments] a ON a.[RosterSpotId] = sp.[RosterSpotId]
                GROUP BY sp.[RosterSpotId], sp.[LeagueId], sp.[TeamId], sp.[PlayerId]{franchiseGroupBy};
                """);

            migrationBuilder.Sql("DROP VIEW [vStandings];");
            migrationBuilder.Sql($"""
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
                    -- The contract for the season the league is actually
                    -- playing. A player with none on file is charged the
                    -- league's default rather than nothing, and counted in
                    -- UnknownContracts so a screen can say how much of the
                    -- total is assumed.
                    LEFT JOIN [PlayerContracts] c
                           ON c.[PlayerId] = sp.[PlayerId] AND c.[Season] = l.[Season]
                    -- An Équipe spot is not a player and costs no cap. Without
                    -- this it would be billed the league default for the
                    -- contract a franchise cannot have, and counted against the
                    -- roster maximum.
                    WHERE sp.[EndDate] IS NULL{playersOnly}
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
                """);
        }
    }
}
