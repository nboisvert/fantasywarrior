using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    RegularSeasonStart = table.Column<DateOnly>(type: "date", nullable: false),
                    RegularSeasonEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    PlayoffStart = table.Column<DateOnly>(type: "date", nullable: true),
                    PlayoffEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    ScheduleImportedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Season);
                });

            migrationBuilder.InsertData(
                table: "Seasons",
                columns: new[] { "Season", "PlayoffEnd", "PlayoffStart", "RegularSeasonEnd", "RegularSeasonStart", "ScheduleImportedUtc" },
                values: new object[] { "20252026", null, null, new DateOnly(2026, 4, 16), new DateOnly(2025, 10, 7), null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Seasons");
        }
    }
}
