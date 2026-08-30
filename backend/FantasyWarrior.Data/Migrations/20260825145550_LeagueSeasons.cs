using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// The table half of "season" — see <c>offseason.md</c>. A league's
    /// own count of its seasons ("saison 3", "saison 4"), the phase each one
    /// walks through, and where its champion gets written. <c>Leagues.Season</c>
    /// is untouched: it stays the plain value naming whose points currently
    /// count.
    ///
    /// <b>Backfilled, not left empty.</b> Every existing league gets one row,
    /// <c>InSeason</c>, matching its current <c>Season</c> — the season already
    /// being played did not stop being real because this table did not exist
    /// yet. Les Mordus is seeded at <c>Number = 3</c>: its own source PDF is
    /// titled "Classement Mordus pool a vie **saison 3**", so that is not a
    /// guess. Every other league gets <c>Number = 1</c> for lack of a better
    /// answer — there is nowhere yet to record a league's true prior count.
    ///
    /// <b>No foreign key from <c>Leagues.Season</c> to this table, and that is
    /// deliberate, not an oversight.</b> It was the first thing tried, and it
    /// does not work: creating a brand new league inserts the <c>Leagues</c>
    /// row first (it is the row that gives out the <c>LeagueId</c> every
    /// <c>LeagueSeason</c> row needs), so a composite FK on
    /// <c>(LeagueId, Season)</c> would refuse the very insert that has to
    /// happen before any matching <c>LeagueSeason</c> row could exist — a
    /// chicken-and-egg SQL Server has no deferred-constraint escape hatch for.
    /// The match stays a value the application keeps honest, the same way
    /// <c>Team.FranchiseAbbrev</c> and <c>NhlTeam.Abbrev</c> already do.
    /// </summary>
    public partial class LeagueSeasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProtectionSlots",
                table: "Leagues",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeagueSeasons",
                columns: table => new
                {
                    LeagueSeasonId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Phase = table.Column<byte>(type: "tinyint", nullable: false),
                    ChampionTeamId = table.Column<int>(type: "int", nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueSeasons", x => x.LeagueSeasonId);
                    table.ForeignKey(
                        name: "FK_LeagueSeasons_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueSeasons_Teams_ChampionTeamId",
                        column: x => x.ChampionTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueSeasons_ChampionTeamId",
                table: "LeagueSeasons",
                column: "ChampionTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_LeagueSeasons_LeagueId_Number",
                table: "LeagueSeasons",
                columns: new[] { "LeagueId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeagueSeasons_LeagueId_Season",
                table: "LeagueSeasons",
                columns: new[] { "LeagueId", "Season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LeagueSeasons_OneActivePerLeague",
                table: "LeagueSeasons",
                column: "LeagueId",
                unique: true,
                filter: "[Phase] <> 5");

            // One row per existing league, InSeason, matching what it already
            // plays today. Phase = 4 (InSeason).
            migrationBuilder.Sql("""
                INSERT INTO [LeagueSeasons] ([LeagueId], [Season], [Number], [Phase], [StartedUtc])
                SELECT
                    l.[LeagueId],
                    l.[Season],
                    CASE WHEN l.[JoinCode] = 'TKW6UR' THEN 3 ELSE 1 END,
                    4,
                    l.[CreatedUtc]
                FROM [Leagues] l;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueSeasons");

            migrationBuilder.DropColumn(
                name: "ProtectionSlots",
                table: "Leagues");
        }
    }
}
