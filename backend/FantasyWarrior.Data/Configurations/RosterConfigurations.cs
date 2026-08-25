using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class RosterSpotConfiguration : IEntityTypeConfiguration<RosterSpot>
{
    public void Configure(EntityTypeBuilder<RosterSpot> b)
    {
        b.ToTable("RosterSpots", t =>
            // A spot holds a player or a franchise, never both and never
            // neither, and the column that is set agrees with PositionGroup.
            // Without this the polymorphism is a naming convention, and a spot
            // with neither would be an owner of nothing that still produces a
            // scoring row every week. Same shape as
            // CK_TradeAssets_ExactlyOneAsset, deliberately.
            t.HasCheckConstraint(
                "CK_RosterSpots_PlayerOrFranchise",
                "([PositionGroup] = 'T' AND [FranchiseAbbrev] IS NOT NULL AND [PlayerId] IS NULL)"
                + " OR ([PositionGroup] <> 'T' AND [PlayerId] IS NOT NULL AND [FranchiseAbbrev] IS NULL)"));

        b.HasKey(x => x.RosterSpotId);
        b.Property(x => x.PositionGroup).HasMaxLength(1).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.FranchiseAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(x => x.StartReason).HasConversion<byte>();
        b.Property(x => x.EndReason).HasConversion<byte>();
        b.Property(x => x.ProtectionStatus).HasConversion<byte>();

        // **One owner per player per league**, as a constraint rather than a
        // hope. Under Firestore this was checked by loading every team in the
        // league and scanning its playerIds array on each add — a race between
        // two simultaneous adds could still produce two owners. A filtered
        // unique index makes the second insert fail, full stop.
        //
        // The PlayerId IS NOT NULL half of the filter is what lets the Équipe
        // slot exist at all: SQL Server treats two NULLs as equal in a unique
        // index, so without it the *second* team in a league to open a
        // franchise spot would collide with the first.
        b.HasIndex(x => new { x.LeagueId, x.PlayerId })
            .IsUnique()
            .HasFilter("[EndDate] IS NULL AND [PlayerId] IS NOT NULL")
            .HasDatabaseName("UX_RosterSpots_OneOpenSpotPerPlayerPerLeague");

        // The same guarantee for franchises: two GMs cannot both own the
        // Canadiens.
        b.HasIndex(x => new { x.LeagueId, x.FranchiseAbbrev })
            .IsUnique()
            .HasFilter("[EndDate] IS NULL AND [FranchiseAbbrev] IS NOT NULL")
            .HasDatabaseName("UX_RosterSpots_OneOpenFranchisePerLeague");

        // **One Équipe slot per team.** This is the constraint the whole
        // feature leans on: one franchise and one seat means there is no
        // active/bench decision to make, which is why that control does not
        // exist on the Team screen. It is also what makes a one-sided franchise
        // trade impossible rather than merely refused.
        //
        // Declared through the named overload because the index below is on the
        // same column: `HasIndex(x => x.TeamId)` twice does not make two
        // indexes, it redefines one, and the second call silently turned "a
        // team's current roster" into "a team may hold one open spot".
        b.HasIndex([nameof(RosterSpot.TeamId)], "UX_RosterSpots_OneOpenFranchisePerTeam")
            .IsUnique()
            .HasFilter("[EndDate] IS NULL AND [PositionGroup] = 'T'");

        // A team's current roster.
        b.HasIndex([nameof(RosterSpot.TeamId)], "IX_RosterSpots_TeamId").HasFilter("[EndDate] IS NULL");

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

        b.HasOne(x => x.Franchise)
            .WithMany()
            .HasForeignKey(x => x.FranchiseAbbrev)
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

public sealed class DraftSelectionConfiguration : IEntityTypeConfiguration<DraftSelection>
{
    public void Configure(EntityTypeBuilder<DraftSelection> b)
    {
        b.ToTable("DraftSelections", t =>
        {
            // A steal takes from a team and spends no entitlement; a rookie pick
            // spends one and takes from nobody. Written as a constraint rather
            // than a convention for the same reason
            // CK_RosterSpots_PlayerOrFranchise is: the shape is the meaning, and
            // a row that violated it would be a draft nobody could read back.
            t.HasCheckConstraint(
                "CK_DraftSelections_SegmentShape",
                "([Segment] = 0 AND [DraftPickId] IS NULL) " +
                "OR ([Segment] = 1 AND [StolenFromTeamId] IS NULL AND [DraftPickId] IS NOT NULL)");

            // A passed turn takes nobody from nobody.
            t.HasCheckConstraint(
                "CK_DraftSelections_PassTakesNobody",
                "[PlayerId] IS NOT NULL OR [StolenFromTeamId] IS NULL");
        });

        b.HasKey(x => x.DraftSelectionId);

        b.Property(x => x.Segment).HasConversion<byte>();

        // THE guard. Without an entitlement row to claim, the steal segment has
        // nothing else that can stop two GMs from both believing they are on
        // turn 7. The second INSERT violates this and the whole transaction
        // rolls back — a 409, never a silent double pick.
        b.HasIndex(x => new { x.LeagueSeasonId, x.OverallIndex })
            .IsUnique()
            .HasDatabaseName("UX_DraftSelections_OneSelectionPerTurn");

        // One entitlement, one selection. This is what makes DraftPick.UsedUtc
        // and this table unable to disagree, and it replaces a conditional
        // update that a future caller could forget to write.
        b.HasIndex(x => x.DraftPickId)
            .IsUnique()
            .HasFilter("[DraftPickId] IS NOT NULL")
            .HasDatabaseName("UX_DraftSelections_OnePerPick");

        // Nobody is drafted twice in one draft.
        b.HasIndex(x => new { x.LeagueSeasonId, x.PlayerId })
            .IsUnique()
            .HasFilter("[PlayerId] IS NOT NULL")
            .HasDatabaseName("UX_DraftSelections_OnePerPlayer");

        // The losses quota, recomputed on every turn — see DraftPool.
        b.HasIndex(x => new { x.LeagueSeasonId, x.StolenFromTeamId })
            .HasDatabaseName("IX_DraftSelections_Losses");

        b.HasOne(x => x.LeagueSeason)
            .WithMany()
            .HasForeignKey(x => x.LeagueSeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.StolenFromTeam)
            .WithMany()
            .HasForeignKey(x => x.StolenFromTeamId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.DraftPick)
            .WithMany()
            .HasForeignKey(x => x.DraftPickId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
