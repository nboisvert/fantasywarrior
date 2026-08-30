using FantasyWarrior.Core.Rules;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class NhlSeasonConfiguration : IEntityTypeConfiguration<NhlSeason>
{
    public void Configure(EntityTypeBuilder<NhlSeason> b)
    {
        b.ToTable("Seasons");
        // The NHL's own identifier, the same argument that keeps Player.PlayerId
        // the NHL's id: a surrogate key here would only add a second name for
        // the value every other table already stores.
        b.HasKey(x => x.Season);
        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).ValueGeneratedNever();

        // Deliberately no foreign key from Games, Periods, PlayerGameStats,
        // PlayerContracts, Leagues or LeagueSeasons: the season string is
        // already the join value on all of them, and a constraint on 51k stat
        // rows would guarantee nothing new while forcing an insert order on
        // every sync job. See NhlSeason.

        // The season already played and banked. Seeded rather than left to a
        // manual `season-init` so a fresh database is immediately consistent
        // with the 51k game lines it is about to receive: these are the dates
        // the imported Games already describe, so declared and observed agree
        // and period-init's behaviour is unchanged. Playoff dates stay null —
        // nothing scores playoffs, and inventing them would be recording a
        // guess as a published fact.
        b.HasData(new NhlSeason
        {
            Season = "20252026",
            RegularSeasonStart = new DateOnly(2025, 10, 7),
            RegularSeasonEnd = new DateOnly(2026, 4, 16),
        });
    }
}

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

public sealed class LeagueSeasonConfiguration : IEntityTypeConfiguration<LeagueSeason>
{
    public void Configure(EntityTypeBuilder<LeagueSeason> b)
    {
        b.ToTable("LeagueSeasons");
        b.HasKey(x => x.LeagueSeasonId);
        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.Phase).HasConversion<byte>();

        // The rules, as the one JSON document RuleSetJson defines. A converter
        // rather than EF's owned-JSON mapping because the scale and the
        // per-position overrides are dictionaries keyed by data, which owned
        // entities cannot express — and because the serializer settings are the
        // storage format and must stay in one place (RuleSetJson).
        //
        // The comparer is not optional. RuleSet is a mutable graph, so EF's
        // default reference equality would compare a tracked entity to itself
        // and conclude nothing changed: every rules edit would be silently
        // dropped at SaveChanges. Comparing and snapshotting through the same
        // serializer is what makes a change detectable, and it is cheap at a
        // handful of rows per league.
        b.Property(x => x.Rules)
            .HasColumnType("nvarchar(max)")
            .IsRequired()
            // Provider-side, not a model default: EF treats a model default as
            // the sentinel meaning "unset" and would skip writing the column on
            // insert. This only has to give the existing rows a value when the
            // column is added — '{}' reads back as a new league's defaults, and
            // `rules-backfill` replaces it with each league's real rules.
            .HasDefaultValueSql("'{}'")
            .HasConversion(
                rules => RuleSetJson.Serialize(rules),
                json => RuleSetJson.Deserialize(json),
                new ValueComparer<RuleSet>(
                    (left, right) => RuleSetJson.Serialize(left!) == RuleSetJson.Serialize(right!),
                    rules => RuleSetJson.Serialize(rules).GetHashCode(),
                    rules => RuleSetJson.Deserialize(RuleSetJson.Serialize(rules))));

        b.HasIndex(x => new { x.LeagueId, x.Season }).IsUnique();
        b.HasIndex(x => new { x.LeagueId, x.Number }).IsUnique();

        // The database version of "exactly one row per league is ever not
        // Complete" (LeagueSeasonPhase = 5) — a real constraint rather than a
        // sentence in a doc, the same reasoning as
        // UX_RosterSpots_OneOpenFranchisePerTeam.
        b.HasIndex(x => x.LeagueId, "UX_LeagueSeasons_OneActivePerLeague")
            .IsUnique()
            .HasFilter("[Phase] <> 5");

        b.HasOne(x => x.League)
            .WithMany(l => l!.Seasons)
            .HasForeignKey(x => x.LeagueId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.ChampionTeam)
            .WithMany()
            .HasForeignKey(x => x.ChampionTeamId)
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
