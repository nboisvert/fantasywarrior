using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class DraftSelections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxLossesPerTeam",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StealRounds",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DraftSelections",
                columns: table => new
                {
                    DraftSelectionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueSeasonId = table.Column<int>(type: "int", nullable: false),
                    OverallIndex = table.Column<int>(type: "int", nullable: false),
                    Segment = table.Column<byte>(type: "tinyint", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<long>(type: "bigint", nullable: true),
                    StolenFromTeamId = table.Column<int>(type: "int", nullable: true),
                    DraftPickId = table.Column<int>(type: "int", nullable: true),
                    MadeUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftSelections", x => x.DraftSelectionId);
                    table.CheckConstraint("CK_DraftSelections_PassTakesNobody", "[PlayerId] IS NOT NULL OR [StolenFromTeamId] IS NULL");
                    table.CheckConstraint("CK_DraftSelections_SegmentShape", "([Segment] = 0 AND [DraftPickId] IS NULL) OR ([Segment] = 1 AND [StolenFromTeamId] IS NULL AND [DraftPickId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DraftSelections_DraftPicks_DraftPickId",
                        column: x => x.DraftPickId,
                        principalTable: "DraftPicks",
                        principalColumn: "DraftPickId");
                    table.ForeignKey(
                        name: "FK_DraftSelections_LeagueSeasons_LeagueSeasonId",
                        column: x => x.LeagueSeasonId,
                        principalTable: "LeagueSeasons",
                        principalColumn: "LeagueSeasonId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DraftSelections_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId");
                    table.ForeignKey(
                        name: "FK_DraftSelections_Teams_StolenFromTeamId",
                        column: x => x.StolenFromTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                    table.ForeignKey(
                        name: "FK_DraftSelections_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftSelections_Losses",
                table: "DraftSelections",
                columns: new[] { "LeagueSeasonId", "StolenFromTeamId" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftSelections_PlayerId",
                table: "DraftSelections",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftSelections_StolenFromTeamId",
                table: "DraftSelections",
                column: "StolenFromTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftSelections_TeamId",
                table: "DraftSelections",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "UX_DraftSelections_OnePerPick",
                table: "DraftSelections",
                column: "DraftPickId",
                unique: true,
                filter: "[DraftPickId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DraftSelections_OnePerPlayer",
                table: "DraftSelections",
                columns: new[] { "LeagueSeasonId", "PlayerId" },
                unique: true,
                filter: "[PlayerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_DraftSelections_OneSelectionPerTurn",
                table: "DraftSelections",
                columns: new[] { "LeagueSeasonId", "OverallIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DraftSelections");

            migrationBuilder.DropColumn(
                name: "MaxLossesPerTeam",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "StealRounds",
                table: "Leagues");
        }
    }
}
