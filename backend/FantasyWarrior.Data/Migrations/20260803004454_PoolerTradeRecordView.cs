using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FantasyWarrior.Data.Migrations
{
    /// <summary>
    /// A Pooler's (team's) whole trade-voting history, from either side of
    /// the table. Same "recompute on read" philosophy as the four views in
    /// <c>ScoringViews</c> — no stored/materialized tally to keep in sync.
    /// </summary>
    public partial class PoolerTradeRecordView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW [vPoolerTradeRecord] AS
                WITH [TradeTallies] AS (
                    SELECT
                        t.[TradeId], t.[LeagueId], t.[ProposerTeamId], t.[CounterpartyTeamId],
                        SUM(CASE WHEN v.[FavoredTeamId] = t.[ProposerTeamId] THEN 1 ELSE 0 END) AS [ProposerVotes],
                        SUM(CASE WHEN v.[FavoredTeamId] = t.[CounterpartyTeamId] THEN 1 ELSE 0 END) AS [CounterpartyVotes],
                        SUM(CASE WHEN v.[UserId] IS NOT NULL AND v.[FavoredTeamId] IS NULL THEN 1 ELSE 0 END) AS [FairVotes]
                    FROM [Trades] t
                    LEFT JOIN [TradeVotes] v ON v.[TradeId] = t.[TradeId]
                    WHERE t.[Status] = 4 -- Processed
                    GROUP BY t.[TradeId], t.[LeagueId], t.[ProposerTeamId], t.[CounterpartyTeamId]
                ),
                [TeamSides] AS (
                    -- Every processed trade counted from each side's own team:
                    -- "votes for me" vs "votes for my opponent", so one
                    -- GROUP BY on TeamId below gives a whole career record
                    -- regardless of which side a team was on in a given trade.
                    SELECT [LeagueId], [ProposerTeamId] AS [TeamId],
                           [ProposerVotes] AS [VotesFor], [CounterpartyVotes] AS [VotesAgainst], [FairVotes]
                    FROM [TradeTallies]
                    UNION ALL
                    SELECT [LeagueId], [CounterpartyTeamId] AS [TeamId],
                           [CounterpartyVotes] AS [VotesFor], [ProposerVotes] AS [VotesAgainst], [FairVotes]
                    FROM [TradeTallies]
                ),
                [Outcomes] AS (
                    SELECT
                        [TeamId], [LeagueId], [VotesFor], [VotesAgainst], [FairVotes],
                        CASE WHEN [VotesFor] > [VotesAgainst] AND [VotesFor] > [FairVotes] THEN 1 ELSE 0 END AS [Won],
                        CASE WHEN [VotesAgainst] > [VotesFor] AND [VotesAgainst] > [FairVotes] THEN 1 ELSE 0 END AS [Lost],
                        CASE WHEN [FairVotes] >= [VotesFor] AND [FairVotes] >= [VotesAgainst]
                                  AND [VotesFor] + [VotesAgainst] + [FairVotes] > 0 THEN 1 ELSE 0 END AS [Fair]
                    FROM [TeamSides]
                )
                SELECT
                    [TeamId], [LeagueId],
                    COUNT(*)            AS [TradesTotal],
                    SUM([Won])          AS [TradesWon],
                    SUM([Lost])         AS [TradesLost],
                    SUM([Fair])         AS [TradesFair],
                    SUM([VotesFor])     AS [VotesFor],
                    SUM([VotesAgainst]) AS [VotesAgainst],
                    SUM([FairVotes])    AS [VotesFair],
                    -- 0-100, centered at 50. Only decided trades (a clear
                    -- plurality) move it; fair trades and exact ties are
                    -- neutral, not zero, so a GM who only ever makes fair
                    -- trades sits at 50, not 0. Null until this team has at
                    -- least one decided trade, so "no data" and "dead even"
                    -- never look the same.
                    CASE WHEN SUM([Won]) + SUM([Lost]) = 0 THEN NULL
                         ELSE 50.0 + 50.0 * CAST(SUM([Won]) - SUM([Lost]) AS float) / (SUM([Won]) + SUM([Lost]))
                    END AS [TraderRating]
                FROM [Outcomes]
                GROUP BY [TeamId], [LeagueId];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW [vPoolerTradeRecord];");
        }
    }
}
