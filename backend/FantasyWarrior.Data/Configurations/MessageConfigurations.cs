using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("Messages");
        b.HasKey(x => x.MessageId);

        // Unicode: unlike the ASCII-only codes elsewhere in this schema, this is
        // people typing at each other in a bilingual pool. Accents and emoji are
        // the expected content, not an edge case.
        b.Property(x => x.Body).HasMaxLength(1000).IsRequired();

        // Reading one thread: both directions between two people in one league.
        b.HasIndex(x => new { x.LeagueId, x.SenderUserId, x.RecipientUserId, x.SentUtc });

        // The unread badge, which runs on every tab change — a filtered index so
        // it stays the size of what is actually unread rather than of the whole
        // history, which only ever grows.
        b.HasIndex(x => new { x.RecipientUserId, x.LeagueId })
            .HasFilter("[ReadUtc] IS NULL")
            .HasDatabaseName("IX_Messages_Unread");

        b.HasOne(x => x.League).WithMany().HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO ACTION on both, and not only out of the conservative-delete habit
        // this schema follows: two cascading paths from Users to the same table
        // is the "may cause cycles or multiple cascade paths" error, and SQL
        // Server refuses to create the constraint at all.
        b.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderUserId)
            .OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Recipient).WithMany().HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
