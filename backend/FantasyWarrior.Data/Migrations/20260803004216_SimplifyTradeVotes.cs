using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyTradeVotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 5-value votes (lean/clear) don't map onto the new 3-value scale
            // (favor proposer / fair / favor counterparty) without guessing,
            // and there's only a handful of dev-league votes on file — a clean
            // slate rather than a lossy reinterpretation (2026-08-02, per Nick).
            migrationBuilder.Sql("DELETE FROM [TradeVotes];");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TradeVotes_MagnitudeMatchesVerdict",
                table: "TradeVotes");

            migrationBuilder.DropColumn(
                name: "Magnitude",
                table: "TradeVotes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Magnitude",
                table: "TradeVotes",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TradeVotes_MagnitudeMatchesVerdict",
                table: "TradeVotes",
                sql: "([FavoredTeamId] IS NULL AND [Magnitude] = 0) OR ([FavoredTeamId] IS NOT NULL AND [Magnitude] IN (1, 2))");
        }
    }
}
