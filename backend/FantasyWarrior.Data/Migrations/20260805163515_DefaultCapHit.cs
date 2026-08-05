using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// A player with no contract on file stops being free.
    ///
    /// Both cap views counted him at $0, which treats "no contract" as a
    /// temporary data gap. It is not: an unsigned free agent and a drafted
    /// prospect who has yet to sign are permanent, ordinary states, and a
    /// keeper pool holds plenty of both — 17 of the 44 players reconciled into
    /// Les Mordus on 2026-08-05 have no contract for this season and never
    /// will. Carrying them for free understated every total that mattered.
    ///
    /// The figure is a league rule (<c>Leagues.DefaultCapHit</c>, $1M by
    /// default), not a constant, so a league that preferred the old behaviour
    /// sets it to 0.
    ///
    /// **Both views have to move together.** <c>vStandings</c> is the baseline
    /// and <c>vTeamCommitments</c> the delta; callers sum them, so charging an
    /// absent contract differently in the two would make the sum meaningless.
    /// That is the same reason the original pair both used 0.
    ///
    /// <c>vStandings</c> also gains <c>UnknownContracts</c>. Once an assumed
    /// salary is folded into the total it stops being visible, and a cap figure
    /// that is part measurement and part house rule should say so rather than
    /// look authoritative.
    /// </summary>
    public partial class DefaultCapHit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // $1M, not the scaffolder's 0: existing leagues get the new rule
            // rather than a silent no-op to be found and fixed by hand later.
            migrationBuilder.AddColumn<long>(
                name: "DefaultCapHit",
                table: "Leagues",
                type: "bigint",
                nullable: false,
                defaultValue: 1_000_000L);

            Rebuild(migrationBuilder, "l.[DefaultCapHit]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Rebuild(migrationBuilder, "0");

            migrationBuilder.DropColumn(
                name: "DefaultCapHit",
                table: "Leagues");
        }

        /// <summary>
        /// Both cap views, parameterised by what an absent contract is worth —
        /// so Up and Down cannot drift apart, and neither can the two views.
        /// </summary>
        private static void Rebuild(MigrationBuilder migrationBuilder, string absentContract)
        {
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
                        SUM(ISNULL(c.[CapHit], {absentContract}))            AS [CapTotal],
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
                    WHERE sp.[EndDate] IS NULL
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

            migrationBuilder.Sql("DROP VIEW [vTeamCommitments];");
            migrationBuilder.Sql($"""
                CREATE VIEW [vTeamCommitments] AS
                SELECT
                    t.[TeamId],
                    t.[LeagueId],
                    SUM(CASE WHEN a.[ToTeamId] = t.[TeamId] THEN ISNULL(c.[CapHit], {absentContract})
                                                            ELSE -ISNULL(c.[CapHit], {absentContract}) END) AS [CapDelta],
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
                -- The contract for the season the league is actually playing,
                -- matching vStandings' own join.
                LEFT JOIN [PlayerContracts] c
                       ON c.[PlayerId] = a.[PlayerId] AND c.[Season] = l.[Season]
                GROUP BY t.[TeamId], t.[LeagueId];
                """);
        }
    }
}
