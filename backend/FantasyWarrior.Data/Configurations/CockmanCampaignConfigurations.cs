using System.Text.Json;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class CockmanCampaignConfiguration : IEntityTypeConfiguration<CockmanCampaign>
{
    public void Configure(EntityTypeBuilder<CockmanCampaign> b)
    {
        b.ToTable("CockmanCampaigns", t =>
            // A reward only ever answers a question — a message-only campaign
            // (like the welcome one) has nothing for it to reward.
            t.HasCheckConstraint(
                "CK_CockmanCampaigns_RewardRequiresQuestion",
                "[RewardAmount] IS NULL OR [HasQuestion] = 1"));

        b.HasKey(x => x.CockmanCampaignId);
        b.Property(x => x.Key).HasMaxLength(40).IsUnicode(false).IsRequired();
        b.HasIndex(x => x.Key).IsUnique();

        // JSON column, same reasoning as LeagueSeason.Rules: a converter
        // rather than owned-JSON mapping, and the ValueComparer is mandatory
        // — without it EF compares the tracked list to itself by reference,
        // concludes nothing changed, and silently drops an edit at SaveChanges.
        b.Property(x => x.ChoiceKeys)
            .HasColumnType("nvarchar(max)")
            .HasConversion(
                keys => keys == null ? null : JsonSerializer.Serialize(keys, (JsonSerializerOptions?)null),
                json => json == null ? null : JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null),
                new ValueComparer<IReadOnlyList<string>?>(
                    (left, right) => (left == null && right == null)
                        || (left != null && right != null && left.SequenceEqual(right)),
                    keys => keys == null ? 0 : keys.Aggregate(0, (hash, k) => HashCode.Combine(hash, k.GetHashCode())),
                    keys => keys == null ? null : keys.ToList()));

        b.HasData(CockmanCampaignSeed.All);
    }
}

public sealed class CockmanCampaignViewConfiguration : IEntityTypeConfiguration<CockmanCampaignView>
{
    public void Configure(EntityTypeBuilder<CockmanCampaignView> b)
    {
        b.ToTable("CockmanCampaignViews");

        // A row's mere existence is "seen" — this key is also the backstop
        // against a second one landing if the app-level idempotency check in
        // CockmanCampaignEndpoints is ever bypassed.
        b.HasKey(x => new { x.CockmanCampaignId, x.UserId });
        b.Property(x => x.ChosenAnswer).HasMaxLength(40).IsUnicode(false);

        b.HasOne(x => x.Campaign)
            .WithMany(c => c!.Views)
            .HasForeignKey(x => x.CockmanCampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
