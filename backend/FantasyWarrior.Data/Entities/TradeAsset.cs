namespace FantasyWarrior.Data.Entities;

public enum TradeAssetType : byte
{
    Player = 0,
    DraftPick = 1,
}

/// <summary>
/// One thing changing hands in a trade: a player or a draft pick.
///
/// A table rather than the two player-id lists the Firestore model carried,
/// because picks are tradable and a list of longs cannot say which kind of
/// asset it holds.
///
/// <see cref="FromTeamId"/>/<see cref="ToTeamId"/> sit on the *asset* rather
/// than being inferred from the trade's two teams. That costs nothing today and
/// means a three-way trade needs no schema change later — the two-team case is
/// simply the one where every asset moves between the same pair.
///
/// A CHECK constraint enforces that exactly one of
/// <see cref="PlayerId"/>/<see cref="DraftPickId"/> is set, and that it agrees
/// with <see cref="AssetType"/>.
/// </summary>
public sealed class TradeAsset
{
    public int TradeAssetId { get; set; }

    public int TradeId { get; set; }

    public int FromTeamId { get; set; }

    public int ToTeamId { get; set; }

    public TradeAssetType AssetType { get; set; }

    public long? PlayerId { get; set; }

    public int? DraftPickId { get; set; }

    public Trade? Trade { get; set; }

    public Team? FromTeam { get; set; }

    public Team? ToTeam { get; set; }

    public Player? Player { get; set; }

    public DraftPick? DraftPick { get; set; }
}
