using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamPeriodGamesAndLastNightGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastNightGamesPlayed",
                table: "TeamStandingsSnapshots",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // vTeamPeriodScores: add active games played alongside the existing
            // points aggregates, for the Standings screen's "This Week" GP
            // column. Only migration that has ever touched this view (checked:
            // no later one redefines it), so this is a safe base to diff from.
            migrationBuilder.Sql("DROP VIEW [vTeamPeriodScores];");
            migrationBuilder.Sql("""
                CREATE VIEW [vTeamPeriodScores] AS
                SELECT
                    sp.[TeamId],
                    p.[PeriodId],
                    p.[Number]  AS [PeriodNumber],
                    p.[Season],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[FantasyPoints] END), 0) AS [ActivePoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 0 THEN a.[FantasyPoints] END), 0) AS [BenchPoints],
                    ISNULL(SUM(CASE WHEN a.[IsActive] = 1 THEN a.[GamesPlayed]   END), 0) AS [ActiveGamesPlayed],
                    CAST(MAX(CASE WHEN a.[IsFinalized] = 1 THEN 1 ELSE 0 END) AS bit)     AS [IsFinalized]
                FROM [RosterAssignments] a
                JOIN [RosterSpots] sp ON sp.[RosterSpotId] = a.[RosterSpotId]
                JOIN [Periods]     p  ON p.[PeriodId]      = a.[PeriodId]
                GROUP BY sp.[TeamId], p.[PeriodId], p.[Number], p.[Season];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vTeamPeriodScores];");
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

            migrationBuilder.DropColumn(
                name: "LastNightGamesPlayed",
                table: "TeamStandingsSnapshots");
        }
    }
}
