using FantasyWarrior.Core.Trades;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>What a team is committed to right now, before this trade.</summary>
/// <param name="CapTotal">
/// The cap of the roster this team will have once every accepted trade has
/// landed — <c>vStandings.EngagedCapTotal</c>, not the figure the league table
/// shows.
/// </param>
public sealed record EngagedState(int TeamId, string TeamName, long CapTotal, int PlayerCount);

/// <summary>Assets already promised away by an accepted trade.</summary>
public sealed record EngagedAssets(
    IReadOnlySet<long> PlayerIds,
    IReadOnlySet<int> DraftPickIds,
    IReadOnlySet<string> FranchiseAbbrevs);

/// <summary>
/// The cap and roster checks a trade has to pass, run identically when it is
/// proposed and when it is accepted.
///
/// <b>Why both moments.</b> Rosters move between the two: another trade can
/// execute, or the same player can be promised elsewhere. An offer that was
/// legal on Monday can be illegal by Friday, and accepting is the last moment
/// anyone can be told.
///
/// <b>Why "engaged" and not the standings.</b> An accepted trade is a decision
/// nobody can take back — it lands at the next week boundary. Checking against
/// <c>vStandings</c> alone would let a GM accept a $9M contract in the morning
/// and bust the cap in the afternoon, with both trades looking fine
/// individually. <c>vTeamCommitments</c> carries that difference.
/// </summary>
public static class TradeValidation
{
    /// <summary>
    /// A team's engaged figures, read straight off the standings view.
    ///
    /// This used to add a delta from <c>vTeamCommitments</c> to the standings
    /// total, because roster spots did not move until the nightly job executed
    /// an accepted trade. They move at acceptance now, dated forward, so the
    /// difference is in the spots' own dates and the view computes both figures
    /// from the same rows in the same statement — one filter apart. There is no
    /// longer an arithmetic step here that could disagree with the view it is
    /// adding to.
    /// </summary>
    public static async Task<Dictionary<int, EngagedState>> EngagedStateAsync(
        FantasyWarriorDbContext db, int leagueId, CancellationToken ct = default)
    {
        var standings = await db.Standings
            .Where(s => s.LeagueId == leagueId)
            .Select(s => new { s.TeamId, s.EngagedCapTotal, s.EngagedPlayerCount })
            .ToListAsync(ct);

        var names = await db.Teams
            .Where(t => t.LeagueId == leagueId)
            .ToDictionaryAsync(t => t.TeamId, t => t.Name, ct);

        return standings.ToDictionary(
            s => s.TeamId,
            s => new EngagedState(
                s.TeamId,
                names.GetValueOrDefault(s.TeamId, "That team"),
                s.EngagedCapTotal,
                s.EngagedPlayerCount));
    }

    /// <summary>
    /// Players and picks already moving in an accepted trade. Offering one
    /// again would produce two trades that both claim the same asset, and the
    /// second would blow up inside the nightly job rather than here.
    ///
    /// Pending offers deliberately do <b>not</b> lock anything: shopping the
    /// same player to three GMs is normal, and only one of those can ever be
    /// accepted — the others are refused at that moment by this same check.
    /// </summary>
    public static async Task<EngagedAssets> EngagedAssetsAsync(
        FantasyWarriorDbContext db, int leagueId, CancellationToken ct = default)
    {
        var assets = await db.TradeAssets
            .Where(a => a.Trade!.LeagueId == leagueId && a.Trade.Status == TradeStatus.Accepted)
            .Select(a => new { a.AssetType, a.PlayerId, a.DraftPickId, a.FranchiseAbbrev })
            .ToListAsync(ct);

        return new EngagedAssets(
            assets.Where(a => a.AssetType == TradeAssetType.Player && a.PlayerId != null)
                .Select(a => a.PlayerId!.Value).ToHashSet(),
            assets.Where(a => a.AssetType == TradeAssetType.DraftPick && a.DraftPickId != null)
                .Select(a => a.DraftPickId!.Value).ToHashSet(),
            assets.Where(a => a.AssetType == TradeAssetType.Franchise && a.FranchiseAbbrev != null)
                .Select(a => a.FranchiseAbbrev!).ToHashSet());
    }

    /// <summary>
    /// Player ids arriving at one team via an accepted trade not yet executed —
    /// the mirror-image query to <see cref="EngagedAssetsAsync"/>, scoped to a
    /// single team's receiving side. No <c>RosterSpot</c> exists for these
    /// players yet; it is created only when the nightly job executes the trade.
    /// </summary>
    public static async Task<IReadOnlyList<long>> IncomingPlayerIdsAsync(
        FantasyWarriorDbContext db, int leagueId, int teamId, CancellationToken ct = default) =>
        await db.TradeAssets
            .Where(a => a.Trade!.LeagueId == leagueId && a.Trade.Status == TradeStatus.Accepted
                        && a.AssetType == TradeAssetType.Player && a.ToTeamId == teamId && a.PlayerId != null)
            .Select(a => a.PlayerId!.Value)
            .Distinct()
            .ToListAsync(ct);

    /// <summary>
    /// Every reason this trade would be illegal, for both teams at once; empty
    /// means it is fine.
    ///
    /// Both sides are checked because the same trade moves them in opposite
    /// directions — a 2-for-1 can break the maximum on one roster and the
    /// minimum on the other.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        League league,
        EngagedState proposer,
        EngagedState counterparty,
        IReadOnlyCollection<long> playersFromProposer,
        IReadOnlyCollection<long> playersFromCounterparty,
        IReadOnlyDictionary<long, long> capHits)
    {
        long? HitOf(long playerId) => capHits.TryGetValue(playerId, out var h) ? h : null;

        var fromProposer = playersFromProposer.Select(HitOf).ToList();
        var fromCounterparty = playersFromCounterparty.Select(HitOf).ToList();

        var proposerImpact = TradeRules.Impact(
            proposer.TeamName, proposer.CapTotal, proposer.PlayerCount,
            outgoing: fromProposer, incoming: fromCounterparty,
            defaultCapHit: league.DefaultCapHit);

        var counterpartyImpact = TradeRules.Impact(
            counterparty.TeamName, counterparty.CapTotal, counterparty.PlayerCount,
            outgoing: fromCounterparty, incoming: fromProposer,
            defaultCapHit: league.DefaultCapHit);

        return
        [
            .. TradeRules.Validate(proposerImpact, league.CapAmount, league.RosterMin, league.RosterMax),
            .. TradeRules.Validate(counterpartyImpact, league.CapAmount, league.RosterMin, league.RosterMax),
        ];
    }
}
