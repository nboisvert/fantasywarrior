using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class PeriodConfiguration : IEntityTypeConfiguration<Period>
{
    public void Configure(EntityTypeBuilder<Period> b)
    {
        b.ToTable("Periods");
        b.HasKey(x => x.PeriodId);
        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();

        b.HasIndex(x => new { x.Season, x.Number }).IsUnique();
        // "Which week is it" — the last period whose start has passed.
        b.HasIndex(x => new { x.Season, x.StartDate });
    }
}

public sealed class SimulationStateConfiguration : IEntityTypeConfiguration<SimulationState>
{
    public void Configure(EntityTypeBuilder<SimulationState> b)
    {
        b.ToTable("SimulationState", t =>
            // One cursor, or the whole point of having a single source of truth
            // for "what day is it" is lost.
            t.HasCheckConstraint("CK_SimulationState_SingleRow", "[SimulationStateId] = 1"));
        b.HasKey(x => x.SimulationStateId);
        b.Property(x => x.SimulationStateId).ValueGeneratedNever();
        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();
    }
}

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.UserId);
        b.Property(x => x.Username).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(30).IsRequired();
        b.Property(x => x.ExternalAuthId).HasMaxLength(128).IsUnicode(false);

        b.HasIndex(x => x.Username).IsUnique();
        b.HasIndex(x => x.ExternalAuthId).IsUnique().HasFilter("[ExternalAuthId] IS NOT NULL");
    }
}

public sealed class LeagueConfiguration : IEntityTypeConfiguration<League>
{
    public void Configure(EntityTypeBuilder<League> b)
    {
        b.ToTable("Leagues");
        b.HasKey(x => x.LeagueId);
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.JoinCode).HasMaxLength(12).IsUnicode(false).IsRequired();

        // The API's public league id, and what the frontend keeps in
        // localStorage — so it is looked up on essentially every request.
        b.HasIndex(x => x.JoinCode).IsUnique();

        b.HasOne(x => x.Commissioner)
            .WithMany()
            .HasForeignKey(x => x.CommissionerUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LeagueMemberConfiguration : IEntityTypeConfiguration<LeagueMember>
{
    public void Configure(EntityTypeBuilder<LeagueMember> b)
    {
        b.ToTable("LeagueMembers");
        b.HasKey(x => new { x.LeagueId, x.UserId });

        // "My leagues" — the first query every session makes.
        b.HasIndex(x => x.UserId);

        b.HasOne(x => x.League)
            .WithMany(l => l!.Members)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.User)
            .WithMany(u => u!.Memberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class LeagueScoringRuleConfiguration : IEntityTypeConfiguration<LeagueScoringRule>
{
    public void Configure(EntityTypeBuilder<LeagueScoringRule> b)
    {
        b.ToTable("LeagueScoringRules");
        b.HasKey(x => new { x.LeagueId, x.StatKey });
        b.Property(x => x.StatKey).HasMaxLength(20).IsUnicode(false).IsRequired();

        b.HasOne(x => x.League)
            .WithMany(l => l!.ScoringRules)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> b)
    {
        b.ToTable("Teams");
        b.HasKey(x => x.TeamId);
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.FranchiseAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false);

        // One team per user per league.
        b.HasIndex(x => new { x.LeagueId, x.OwnerUserId }).IsUnique();

        b.HasOne(x => x.League)
            .WithMany(l => l!.Teams)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Owner)
            .WithMany(u => u!.Teams)
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.NoAction);

        b.HasOne(x => x.Franchise)
            .WithMany()
            .HasForeignKey(x => x.FranchiseAbbrev)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
