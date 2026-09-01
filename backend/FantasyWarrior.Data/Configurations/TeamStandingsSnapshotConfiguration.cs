using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class TeamStandingsSnapshotConfiguration : IEntityTypeConfiguration<TeamStandingsSnapshot>
{
    public void Configure(EntityTypeBuilder<TeamStandingsSnapshot> b)
    {
        b.ToTable("TeamStandingsSnapshots");
        b.HasKey(x => x.TeamStandingsSnapshotId);

        // Upsert key: a rerun of the nightly job for the same night updates
        // this row rather than duplicating it.
        //
        // Two indexes on the same (TeamId, AsOfDate) pair — each needs its own
        // explicit name, or the second HasIndex call silently redefines the
        // first instead of adding a second one (data-model.md's RosterSpots
        // warning about this exact trap).
        b.HasIndex(x => new { x.TeamId, x.AsOfDate }, "UX_TeamStandingsSnapshots_TeamId_AsOfDate").IsUnique();

        // "This team's two most recent snapshots" is the only other query.
        b.HasIndex(x => new { x.TeamId, x.AsOfDate }, "IX_TeamStandingsSnapshots_TeamId_AsOfDateDesc")
            .IsDescending(false, true)
            .IncludeProperties(x => new { x.Rank, x.LastNightPoints });

        b.HasOne(x => x.Team).WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Cascade);
    }
}
