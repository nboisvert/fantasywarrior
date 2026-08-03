using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// What a team's accepted-but-unexecuted trades already commit it to.
    ///
    /// Same "recompute on read" philosophy as ScoringViews and
    /// vCockcoinBalance: a commitment is an aggregate over an honest event log
    /// — the trades themselves — never a flag someone has to remember to set on
    /// accept and clear on execution.
    /// </summary>
    public partial class TeamCommitmentsView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Deltas, not totals: the baseline stays vStandings and a caller
            // adds the two. Folding this into the standings would be wrong —
            // the league table must keep showing what is true today, not what
            // has been promised for next week.
            //
            // Players only (AssetType 0). A draft pick carries no salary and
            // occupies no roster spot, so it moves neither number.
            //
            // A missing contract counts as 0 here exactly as it does in
            // vStandings. These two figures get summed, so they have to treat
            // an unknown salary identically or the sum means nothing. The API
            // reports the number of unknowns separately rather than pretending
            // the total is complete.
            migrationBuilder.Sql("""
                CREATE VIEW [vTeamCommitments] AS
                SELECT
                    t.[TeamId],
                    t.[LeagueId],
                    SUM(CASE WHEN a.[ToTeamId] = t.[TeamId] THEN ISNULL(c.[CapHit], 0)
                                                            ELSE -ISNULL(c.[CapHit], 0) END) AS [CapDelta],
                    SUM(CASE WHEN a.[ToTeamId] = t.[TeamId] THEN 1 ELSE -1 END) AS [CountDelta]
                FROM [Teams] t
                JOIN [Leagues] l
                  ON l.[LeagueId] = t.[LeagueId]
                JOIN [Trades] tr
                  ON tr.[LeagueId] = t.[LeagueId]
                 AND tr.[Status] = 3          -- TradeStatus.Accepted
                JOIN [TradeAssets] a
                  ON a.[TradeId] = tr.[TradeId]
                 AND a.[AssetType] = 0        -- TradeAssetType.Player
                 AND (a.[FromTeamId] = t.[TeamId] OR a.[ToTeamId] = t.[TeamId])
                -- The contract for the season the league is actually playing,
                -- matching vStandings' own join.
                LEFT JOIN [PlayerContracts] c
                       ON c.[PlayerId] = a.[PlayerId] AND c.[Season] = l.[Season]
                GROUP BY t.[TeamId], t.[LeagueId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vTeamCommitments];");
        }
    }
}
