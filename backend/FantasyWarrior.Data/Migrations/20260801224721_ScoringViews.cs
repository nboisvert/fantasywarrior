using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// The four aggregate views.
    ///
    /// Every number the app displays above the assignment grain is defined here
    /// and nowhere else. That is the bet this schema makes: one honest row per
    /// (roster spot, week), everything above it derived in a single statement,
    /// so two totals can never disagree. The Firestore model stored those
    /// totals instead and kept them in step by hand — which is where its
    /// scoring bugs came from.
    /// </summary>
    public partial class ScoringViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Regular season only. Playoffs are excluded from pool scoring
            // everywhere, by rule and not by accident — see scoring-model.md §4.
            migrationBuilder.Sql("""
                CREATE VIEW [vPlayerSeasonStats] AS
                SELECT
                    s.[PlayerId],
                    s.[Season],
                    COUNT(*)                                AS [GamesPlayed],
                    ISNULL(SUM(s.[Goals]), 0)               AS [Goals],
                    ISNULL(SUM(s.[Assists]), 0)             AS [Assists],
                    ISNULL(SUM(s.[PlusMinus]), 0)           AS [PlusMinus],
                    ISNULL(SUM(s.[Pim]), 0)                 AS [Pim],
                    ISNULL(SUM(s.[Shots]), 0)               AS [Shots],
                    ISNULL(SUM(s.[Hits]), 0)                AS [Hits],
                    ISNULL(SUM(s.[BlockedShots]), 0)        AS [BlockedShots],
                    SUM(CASE WHEN s.[Decision] = 'W' THEN 1 ELSE 0 END) AS [Wins],
                    SUM(CASE WHEN s.[OtLoss]  = 1   THEN 1 ELSE 0 END)  AS [OtLosses],
                    SUM(CASE WHEN s.[Shutout] = 1   THEN 1 ELSE 0 END)  AS [Shutouts],
                    ISNULL(SUM(s.[GoalsAgainst]), 0)        AS [GoalsAgainst],
                    ISNULL(SUM(s.[Saves]), 0)               AS [Saves],
                    ISNULL(SUM(s.[ShotsAgainst]), 0)        AS [ShotsAgainst]
                FROM [PlayerGameStats] s
                WHERE s.[GameType] = 2
                GROUP BY s.[PlayerId], s.[Season];
                """);

            // What a player has been worth to the team holding him — or that
            // held him, since closed spots keep their history forever.
            migrationBuilder.Sql("""
                CREATE VIEW [vRosterSpotTotals] AS
                SELECT
                    sp.[RosterSpotId],
                    sp.[LeagueId],
                    sp.[TeamId],
                    sp.[PlayerId],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [ActivePoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchPoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [ActiveGamesPlayed],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Goals]         END), 0) AS [ActiveGoals],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[Assists]       END), 0) AS [ActiveAssists],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 AND a.[IsFinalized] = 1
                                    THEN a.[FantasyPoints] END), 0)                       AS [FinalizedActivePoints]
                FROM [RosterSpots] sp
                LEFT JOIN [RosterAssignments] a ON a.[RosterSpotId] = sp.[RosterSpotId]
                GROUP BY sp.[RosterSpotId], sp.[LeagueId], sp.[TeamId], sp.[PlayerId];
                """);

            // One row per team per week. A week the team did not exist for has
            // no row; a break week has rows worth zero — which is the
            // distinction the UI needs to say "pause" instead of "0 points".
            migrationBuilder.Sql("""
                CREATE VIEW [vTeamPeriodScores] AS
                SELECT
                    sp.[TeamId],
                    p.[PeriodId],
                    p.[Number]  AS [PeriodNumber],
                    p.[Season],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [ActivePoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchPoints],
                    CAST(MAX(CASE WHEN a.[IsFinalized] = 1 THEN 1 ELSE 0 END) AS bit)     AS [IsFinalized]
                FROM [RosterAssignments] a
                JOIN [RosterSpots] sp ON sp.[RosterSpotId] = a.[RosterSpotId]
                JOIN [Periods]     p  ON p.[PeriodId]      = a.[PeriodId]
                GROUP BY sp.[TeamId], p.[PeriodId], p.[Number], p.[Season];
                """);

            // Points and cap are computed over different sets and must not be
            // conflated: points come from *every* spot the team has ever held
            // (banked history does not leave when a player does), while the cap
            // hit is only what is on the roster right now. Two CTEs, for exactly
            // that reason.
            migrationBuilder.Sql("""
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
                        COUNT(*)                    AS [PlayerCount],
                        ISNULL(SUM(c.[CapHit]), 0)  AS [CapTotal]
                    FROM [RosterSpots] sp
                    JOIN [Teams]   t ON t.[TeamId]   = sp.[TeamId]
                    JOIN [Leagues] l ON l.[LeagueId] = t.[LeagueId]
                    -- The contract for the season the league is actually
                    -- playing. A player with none on file contributes nothing
                    -- rather than dropping his row from the count.
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
                    ISNULL(r.[CapTotal], 0)                              AS [CapTotal]
                FROM [Teams] t
                LEFT JOIN [Scoring] s ON s.[TeamId] = t.[TeamId]
                LEFT JOIN [Roster]  r ON r.[TeamId] = t.[TeamId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vStandings];");
            migrationBuilder.Sql("DROP VIEW [vTeamPeriodScores];");
            migrationBuilder.Sql("DROP VIEW [vRosterSpotTotals];");
            migrationBuilder.Sql("DROP VIEW [vPlayerSeasonStats];");
        }
    }
}
