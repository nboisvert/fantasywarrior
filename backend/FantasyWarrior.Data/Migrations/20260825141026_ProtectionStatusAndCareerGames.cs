using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// The two columns the off-season draft is built on, and the backfill that
    /// makes the second one honest from day one.
    ///
    /// <b><c>RosterSpots.ProtectionStatus</c></b> — whether the GM has spent one
    /// of his protection slots on this spot. <c>NOT NULL DEFAULT 0</c>, so every
    /// existing spot lands on Unprotected with nothing to write. It is a column
    /// rather than a row per draft because the value is worth exactly one
    /// summer: it expires when the season it guarded begins (Nick), so there is
    /// no history to keep.
    ///
    /// <b><c>Players.CareerNhlGames</c></b> — regular-season NHL games, career to
    /// date. Nullable on purpose: it means "career-sync has never reached this
    /// player", and a veteran whose sync failed must never read as a rookie.
    /// From here on career-sync owns it, writing it in the same save as the rows
    /// it sums, which is what makes drift impossible.
    ///
    /// <b>The backfill runs in two statements, and the order is the point.</b>
    /// The aggregate alone would leave a pure junior prospect at NULL — he has no
    /// NHL career row to join to — which reads as "never looked" when in fact we
    /// looked and the answer is zero. Zeroing every synced player first, then
    /// raising the ones with NHL rows, keeps NULL meaning the one thing it is
    /// supposed to mean.
    /// </summary>
    public partial class ProtectionStatusAndCareerGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "ProtectionStatus",
                table: "RosterSpots",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "CareerNhlGames",
                table: "Players",
                type: "int",
                nullable: true);

            // 1. Every player career-sync has already visited has a known
            //    answer, and zero is a perfectly good one.
            migrationBuilder.Sql("""
                UPDATE [Players]
                SET    [CareerNhlGames] = 0
                WHERE  [CareerStatsSyncedUtc] IS NOT NULL;
                """);

            // 2. Then the real total for those who have NHL rows. Junior, KHL and
            //    NCAA lines are in the same table and must not count — a 200-game
            //    junior career is exactly the profile the protection rule shields.
            //    Playoffs cannot appear: career-sync only ever writes regular
            //    season.
            migrationBuilder.Sql("""
                UPDATE p
                SET    p.[CareerNhlGames] = x.[GP]
                FROM   [Players] p
                JOIN   (SELECT [PlayerId], SUM([GamesPlayed]) AS [GP]
                        FROM   [PlayerCareerSeasonStats]
                        WHERE  [LeagueAbbrev] = 'NHL'
                        GROUP BY [PlayerId]) x ON x.[PlayerId] = p.[PlayerId]
                WHERE  p.[CareerStatsSyncedUtc] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProtectionStatus",
                table: "RosterSpots");

            migrationBuilder.DropColumn(
                name: "CareerNhlGames",
                table: "Players");
        }
    }
}
