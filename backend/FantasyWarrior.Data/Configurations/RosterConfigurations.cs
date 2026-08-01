using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class RosterSpotConfiguration : IEntityTypeConfiguration<RosterSpot>
{
    public void Configure(EntityTypeBuilder<RosterSpot> b)
    {
        b.ToTable("RosterSpots");
        b.HasKey(x => x.RosterSpotId);
        b.Property(x => x.PositionGroup).HasMaxLength(1).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.StartReason).HasConversion<byte>();
        b.Property(x => x.EndReason).HasConversion<byte>();

        // **One owner per player per league**, as a constraint rather than a
        // hope. Under Firestore this was checked by loading every team in the
        // league and scanning its playerIds array on each add — a race between
        // two simultaneous adds could still produce two owners. A filtered
        // unique index makes the second insert fail, full stop.
        b.HasIndex(x => new { x.LeagueId, x.PlayerId })
            .IsUnique()
            .HasFilter("[EndDate] IS NULL")
            .HasDatabaseName("UX_RosterSpots_OneOpenSpotPerPlayerPerLeague");

        // A team's current roster.
        b.HasIndex(x => x.TeamId).HasFilter("[EndDate] IS NULL");

        // Every spot owning any part of a period. Under Firestore this needed a
        // union of two queries and a dedupe, because there is no OR across
        // different operators — and forgetting the second half (spots that
        // closed *during* the week) made every team's score visibly sag each
        // night until the week finalized. Here it is one predicate:
        //   StartDate <= periodEnd AND (EndDate IS NULL OR EndDate >= periodStart)
        b.HasIndex(x => new { x.LeagueId, x.StartDate, x.EndDate });

        b.HasOne(x => x.Team)
            .WithMany(t => t!.RosterSpots)
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        // The league is reachable through the team; this FK exists only to back
        // the uniqueness index above, so it must not add a second cascade path.
        b.HasOne(x => x.League)
            .WithMany(l => l!.RosterSpots)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.StartTrade)
            .WithMany()
            .HasForeignKey(x => x.StartTradeId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.EndTrade)
            .WithMany()
            .HasForeignKey(x => x.EndTradeId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.StartDraftPick)
            .WithMany()
            .HasForeignKey(x => x.StartDraftPickId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class RosterAssignmentConfiguration : IEntityTypeConfiguration<RosterAssignment>
{
    public void Configure(EntityTypeBuilder<RosterAssignment> b)
    {
        b.ToTable("RosterAssignments");
        b.HasKey(x => x.RosterAssignmentId);

        // A spot gets exactly one row per week. This is what makes the nightly
        // job safe to re-run: it upserts on this key rather than accumulating.
        b.HasIndex(x => new { x.RosterSpotId, x.PeriodId }).IsUnique();

        // Standings and weekly rollups both aggregate by period; the active flag
        // is in the key so "sum the active ones" is an index seek.
        b.HasIndex(x => new { x.PeriodId, x.IsActive })
            .IncludeProperties(x => new { x.RosterSpotId, x.FantasyPoints, x.GamesPlayed });

        b.HasOne(x => x.RosterSpot)
            .WithMany(s => s!.Assignments)
            .HasForeignKey(x => x.RosterSpotId)
            .OnDelete(DeleteBehavior.Cascade);

        // Periods are append-only and never deleted — deleting one would
        // restate points teams already own.
        b.HasOne(x => x.Period)
            .WithMany(p => p!.Assignments)
            .HasForeignKey(x => x.PeriodId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class TeamPeriodLineupConfiguration : IEntityTypeConfiguration<TeamPeriodLineup>
{
    public void Configure(EntityTypeBuilder<TeamPeriodLineup> b)
    {
        b.ToTable("TeamPeriodLineups");
        b.HasKey(x => new { x.TeamId, x.PeriodId });
        b.Property(x => x.SetBy).HasMaxLength(30).IsUnicode(false).IsRequired();

        b.HasOne(x => x.Team)
            .WithMany(t => t!.Lineups)
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Period)
            .WithMany()
            .HasForeignKey(x => x.PeriodId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class DraftPickConfiguration : IEntityTypeConfiguration<DraftPick>
{
    public void Configure(EntityTypeBuilder<DraftPick> b)
    {
        b.ToTable("DraftPicks");
        b.HasKey(x => x.DraftPickId);

        // A team has one pick per round per year, before any trading.
        b.HasIndex(x => new { x.LeagueId, x.Year, x.Round, x.OriginalTeamId }).IsUnique();
        // "What does this team hold" — the only other way picks are read.
        b.HasIndex(x => new { x.CurrentTeamId, x.Year, x.Round });

        b.HasOne(x => x.League)
            .WithMany(l => l!.DraftPicks)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.OriginalTeam)
            .WithMany()
            .HasForeignKey(x => x.OriginalTeamId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.CurrentTeam)
            .WithMany()
            .HasForeignKey(x => x.CurrentTeamId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
