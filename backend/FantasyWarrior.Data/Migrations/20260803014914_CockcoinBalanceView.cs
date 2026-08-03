using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// One user's cockcoin balance — SUM over CockcoinAwards, same
    /// "recompute on read" philosophy as ScoringViews/PoolerTradeRecordView.
    /// </summary>
    public partial class CockcoinBalanceView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW [vCockcoinBalance] AS
                SELECT [UserId], SUM([Amount]) AS [Balance]
                FROM [CockcoinAwards]
                GROUP BY [UserId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vCockcoinBalance];");
        }
    }
}
