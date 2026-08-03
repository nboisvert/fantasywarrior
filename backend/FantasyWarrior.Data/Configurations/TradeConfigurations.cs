using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> b)
    {
        b.ToTable("Trades");
        b.HasKey(x => x.TradeId);
        b.Property(x => x.Status).HasConversion<byte>();

        // The Trades screen: this league's trades, newest first.
        b.HasIndex(x => new { x.LeagueId, x.CreatedUtc }).IsDescending(false, true);
        // The nightly job: everything accepted and waiting for a period boundary.
        b.HasIndex(x => x.Status).HasFilter("[Status] = 3");

        b.HasOne(x => x.League)
            .WithMany(l => l!.Trades)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ProposerTeam)
            .WithMany()
            .HasForeignKey(x => x.ProposerTeamId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.CounterpartyTeam)
            .WithMany()
            .HasForeignKey(x => x.CounterpartyTeamId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class TradeAssetConfiguration : IEntityTypeConfiguration<TradeAsset>
{
    public void Configure(EntityTypeBuilder<TradeAsset> b)
    {
        b.ToTable("TradeAssets", t =>
            // A trade asset is a player or a pick, never both and never neither.
            // Without this the polymorphism is a naming convention and a null
            // pair would silently mean "an empty thing changed hands".
            t.HasCheckConstraint(
                "CK_TradeAssets_ExactlyOneAsset",
                "([AssetType] = 0 AND [PlayerId] IS NOT NULL AND [DraftPickId] IS NULL)"
                + " OR ([AssetType] = 1 AND [DraftPickId] IS NOT NULL AND [PlayerId] IS NULL)"));

        b.HasKey(x => x.TradeAssetId);
        b.Property(x => x.AssetType).HasConversion<byte>();

        b.HasIndex(x => x.TradeId);

        b.HasOne(x => x.Trade)
            .WithMany(t => t!.Assets)
            .HasForeignKey(x => x.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.FromTeam).WithMany().HasForeignKey(x => x.FromTeamId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.ToTeam).WithMany().HasForeignKey(x => x.ToTeamId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.DraftPick).WithMany().HasForeignKey(x => x.DraftPickId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class TradeVoteConfiguration : IEntityTypeConfiguration<TradeVote>
{
    public void Configure(EntityTypeBuilder<TradeVote> b)
    {
        b.ToTable("TradeVotes");

        // One vote per member per trade; re-voting is an update, not a second row.
        b.HasKey(x => new { x.TradeId, x.UserId });

        b.HasOne(x => x.Trade)
            .WithMany(t => t!.Votes)
            .HasForeignKey(x => x.TradeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.FavoredTeam).WithMany().HasForeignKey(x => x.FavoredTeamId).OnDelete(DeleteBehavior.NoAction);
    }
}
