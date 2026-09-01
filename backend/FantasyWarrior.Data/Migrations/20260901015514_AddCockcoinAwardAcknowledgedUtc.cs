using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCockcoinAwardAcknowledgedUtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedUtc",
                table: "CockcoinAwards",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CockcoinAwards_UserId_Reason_AcknowledgedUtc",
                table: "CockcoinAwards",
                columns: new[] { "UserId", "Reason", "AcknowledgedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CockcoinAwards_UserId_Reason_AcknowledgedUtc",
                table: "CockcoinAwards");

            migrationBuilder.DropColumn(
                name: "AcknowledgedUtc",
                table: "CockcoinAwards");
        }
    }
}
