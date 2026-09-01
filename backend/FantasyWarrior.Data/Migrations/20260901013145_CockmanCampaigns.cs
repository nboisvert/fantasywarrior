using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class CockmanCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CockmanCampaigns",
                columns: table => new
                {
                    CockmanCampaignId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    HasQuestion = table.Column<bool>(type: "bit", nullable: false),
                    ChoiceKeys = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RewardAmount = table.Column<int>(type: "int", nullable: true),
                    StartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CockmanCampaigns", x => x.CockmanCampaignId);
                    table.CheckConstraint("CK_CockmanCampaigns_RewardRequiresQuestion", "[RewardAmount] IS NULL OR [HasQuestion] = 1");
                });

            migrationBuilder.CreateTable(
                name: "CockmanCampaignViews",
                columns: table => new
                {
                    CockmanCampaignId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ViewedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChosenAnswer = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    RewardedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CockmanCampaignViews", x => new { x.CockmanCampaignId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CockmanCampaignViews_CockmanCampaigns_CockmanCampaignId",
                        column: x => x.CockmanCampaignId,
                        principalTable: "CockmanCampaigns",
                        principalColumn: "CockmanCampaignId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CockmanCampaignViews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CockmanCampaigns",
                columns: new[] { "CockmanCampaignId", "ChoiceKeys", "CreatedUtc", "EndUtc", "HasQuestion", "Key", "RewardAmount", "StartUtc" },
                values: new object[] { 1, null, new DateTime(2026, 8, 31, 0, 0, 0, 0, DateTimeKind.Utc), null, false, "welcome", null, new DateTime(2026, 8, 31, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "IX_CockmanCampaigns_Key",
                table: "CockmanCampaigns",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CockmanCampaignViews_UserId",
                table: "CockmanCampaignViews",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CockmanCampaignViews");

            migrationBuilder.DropTable(
                name: "CockmanCampaigns");
        }
    }
}
