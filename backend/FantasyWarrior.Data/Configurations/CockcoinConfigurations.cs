using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class CockcoinAwardConfiguration : IEntityTypeConfiguration<CockcoinAward>
{
    public void Configure(EntityTypeBuilder<CockcoinAward> b)
    {
        b.ToTable("CockcoinAwards");
        b.HasKey(x => x.CockcoinAwardId);
        b.Property(x => x.Reason).HasMaxLength(40).IsUnicode(false).IsRequired();

        // The balance view's whole query; every lookup is "this user's awards".
        b.HasIndex(x => x.UserId);
        // The pending-reward query: this user's unacknowledged done-deal awards.
        b.HasIndex(x => new { x.UserId, x.Reason, x.AcknowledgedUtc });

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
