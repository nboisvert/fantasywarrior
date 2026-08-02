using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerCareerStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CareerStatsSyncedUtc",
                table: "Players",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PlayerCareerSeasonStats",
                columns: table => new
                {
                    PlayerCareerSeasonStatId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    GameType = table.Column<byte>(type: "tinyint", nullable: false),
                    LeagueAbbrev = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    TeamName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GamesPlayed = table.Column<int>(type: "int", nullable: false),
                    Goals = table.Column<int>(type: "int", nullable: true),
                    Assists = table.Column<int>(type: "int", nullable: true),
                    Points = table.Column<int>(type: "int", nullable: true),
                    Pim = table.Column<int>(type: "int", nullable: true),
                    PlusMinus = table.Column<int>(type: "int", nullable: true),
                    Wins = table.Column<int>(type: "int", nullable: true),
                    Losses = table.Column<int>(type: "int", nullable: true),
                    OtLosses = table.Column<int>(type: "int", nullable: true),
                    GoalsAgainst = table.Column<int>(type: "int", nullable: true),
                    GoalsAgainstAvg = table.Column<double>(type: "float", nullable: true),
                    SavePctg = table.Column<double>(type: "float", nullable: true),
                    Shutouts = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerCareerSeasonStats", x => x.PlayerCareerSeasonStatId);
                    table.ForeignKey(
                        name: "FK_PlayerCareerSeasonStats_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Players_CareerStatsSyncedUtc",
                table: "Players",
                column: "CareerStatsSyncedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCareerSeasonStats_PlayerId_Season",
                table: "PlayerCareerSeasonStats",
                columns: new[] { "PlayerId", "Season" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCareerSeasonStats_PlayerId_Season_GameType_LeagueAbbrev_TeamName",
                table: "PlayerCareerSeasonStats",
                columns: new[] { "PlayerId", "Season", "GameType", "LeagueAbbrev", "TeamName" },
                unique: true,
                filter: "[TeamName] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerCareerSeasonStats");

            migrationBuilder.DropIndex(
                name: "IX_Players_CareerStatsSyncedUtc",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CareerStatsSyncedUtc",
                table: "Players");
        }
    }
}
