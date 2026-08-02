using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

public sealed class NhlTeamConfiguration : IEntityTypeConfiguration<NhlTeam>
{
    public void Configure(EntityTypeBuilder<NhlTeam> b)
    {
        b.ToTable("NhlTeams");
        b.HasKey(x => x.Abbrev);
        b.Property(x => x.Abbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();
        b.Property(x => x.ConferenceName).HasMaxLength(20);
        b.Property(x => x.DivisionName).HasMaxLength(20);
        b.Property(x => x.LogoUrl).HasMaxLength(300);

        // Every game, player and franchise slot references this table, so it
        // must exist before anything else can be inserted.
        b.HasData(NhlTeamSeed.All);
    }
}

public sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> b)
    {
        b.ToTable("Players");
        b.HasKey(x => x.PlayerId);
        // The NHL's id, not ours — never generated.
        b.Property(x => x.PlayerId).ValueGeneratedNever();

        b.Property(x => x.FirstName).HasMaxLength(60).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(60).IsRequired();
        b.Property(x => x.Position).HasMaxLength(1).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.TeamAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(x => x.Status).HasMaxLength(12).IsUnicode(false).IsRequired();
        b.Property(x => x.ShootsCatches).HasMaxLength(1).IsFixedLength().IsUnicode(false);
        b.Property(x => x.BirthCountry).HasMaxLength(3).IsUnicode(false);
        b.Property(x => x.HeadshotUrl).HasMaxLength(300);
        b.Property(x => x.DraftTeamAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        b.Property(x => x.CapWagesSlug).HasMaxLength(80).IsUnicode(false);

        // Derived by the database so it can never disagree with Position, and
        // stored so it can be indexed. Mirrors PositionGroups.From exactly.
        b.Property(x => x.PositionGroup)
            .HasMaxLength(1)
            .IsFixedLength()
            .IsUnicode(false)
            .HasComputedColumnSql(
                "CASE WHEN [Position] = 'D' THEN 'D' WHEN [Position] = 'G' THEN 'G' ELSE 'F' END",
                stored: true);

        b.HasIndex(x => new { x.LastName, x.FirstName });
        b.HasIndex(x => x.TeamAbbrev);
        b.HasIndex(x => x.Status);
        // draft-sync scans for players it has never looked up. Filtered, because
        // once the backfill is done this matches almost nothing.
        b.HasIndex(x => x.DraftChecked).HasFilter("[DraftChecked] = 0");
        // career-sync scans for the stalest rows on a rolling basis — unlike
        // DraftChecked this never settles down to a small set, so unfiltered.
        b.HasIndex(x => x.CareerStatsSyncedUtc);

        b.HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamAbbrev)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PlayerContractConfiguration : IEntityTypeConfiguration<PlayerContract>
{
    public void Configure(EntityTypeBuilder<PlayerContract> b)
    {
        b.ToTable("PlayerContracts");
        b.HasKey(x => x.PlayerContractId);
        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.ClauseType).HasMaxLength(20).IsUnicode(false);
        b.Property(x => x.Source).HasMaxLength(12).IsUnicode(false).IsRequired();

        // One contract row per player-season: an import re-run updates rather
        // than accumulating duplicates.
        b.HasIndex(x => new { x.PlayerId, x.Season }).IsUnique();

        b.HasOne(x => x.Player)
            .WithMany(p => p!.Contracts)
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PlayerInjuryConfiguration : IEntityTypeConfiguration<PlayerInjury>
{
    public void Configure(EntityTypeBuilder<PlayerInjury> b)
    {
        b.ToTable("PlayerInjuries");
        b.HasKey(x => x.PlayerInjuryId);
        b.Property(x => x.Status).HasMaxLength(40).IsRequired();
        b.Property(x => x.InjuryType).HasMaxLength(80);
        b.Property(x => x.Source).HasMaxLength(20).IsUnicode(false).IsRequired();

        // "Who is hurt right now" is the only query this table has.
        b.HasIndex(x => x.PlayerId).HasFilter("[ResolvedUtc] IS NULL");

        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> b)
    {
        b.ToTable("Games");
        b.HasKey(x => x.GameId);
        b.Property(x => x.GameId).ValueGeneratedNever();
        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.HomeTeamAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.AwayTeamAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.LastPeriodType).HasMaxLength(3).IsUnicode(false);

        b.HasIndex(x => x.GameDate);
        // period-init derives the weekly calendar from this: every regular-season
        // game day in a season, in order.
        b.HasIndex(x => new { x.Season, x.GameType, x.GameDate });

        b.HasOne<NhlTeam>().WithMany().HasForeignKey(x => x.HomeTeamAbbrev).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<NhlTeam>().WithMany().HasForeignKey(x => x.AwayTeamAbbrev).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PlayerGameStatConfiguration : IEntityTypeConfiguration<PlayerGameStat>
{
    public void Configure(EntityTypeBuilder<PlayerGameStat> b)
    {
        b.ToTable("PlayerGameStats");
        // A player appears once per game; no surrogate key earns its keep here.
        b.HasKey(x => new { x.GameId, x.PlayerId });

        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.TeamAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.OpponentAbbrev).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.Position).HasMaxLength(1).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.Toi).HasMaxLength(8).IsUnicode(false);
        b.Property(x => x.Decision).HasMaxLength(1).IsFixedLength().IsUnicode(false);

        // THE query: every line in one scoring week, scored for every league at
        // once. Covering, so the week's rollup never touches the base table.
        b.HasIndex(x => x.GameDate)
            .IncludeProperties(x => new
            {
                x.PlayerId, x.Season, x.GameType, x.IsGoalie,
                x.Goals, x.Assists, x.PlusMinus, x.Pim, x.Shots, x.Hits, x.BlockedShots,
                x.Decision, x.OtLoss, x.Shutout, x.GoalsAgainst, x.Saves, x.ShotsAgainst,
            });

        // The player card and season aggregates: one player, whole season.
        b.HasIndex(x => new { x.PlayerId, x.GameDate });

        b.HasOne(x => x.Game)
            .WithMany(g => g!.PlayerLines)
            .HasForeignKey(x => x.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        // NoAction, not Cascade: Games already cascades into this table, and a
        // second cascade path into the same table is rejected by SQL Server.
        // A player with game lines should not be deletable anyway.
        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PlayerCareerSeasonStatConfiguration : IEntityTypeConfiguration<PlayerCareerSeasonStat>
{
    public void Configure(EntityTypeBuilder<PlayerCareerSeasonStat> b)
    {
        b.ToTable("PlayerCareerSeasonStats");
        b.HasKey(x => x.PlayerCareerSeasonStatId);

        b.Property(x => x.Season).HasMaxLength(8).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.LeagueAbbrev).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(x => x.TeamName).HasMaxLength(80);

        // career-sync's full-replace-per-player upsert relies on this being
        // unique: a mid-season trade gives two rows for the same
        // season/league/game-type with different teams.
        b.HasIndex(x => new { x.PlayerId, x.Season, x.GameType, x.LeagueAbbrev, x.TeamName }).IsUnique();

        // The Player Card's Career tab: one player, every season, most recent first.
        b.HasIndex(x => new { x.PlayerId, x.Season });

        // NoAction, not Cascade: a player with career rows should not be
        // deletable anyway (same reasoning as PlayerGameStat).
        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class NewsItemConfiguration : IEntityTypeConfiguration<NewsItem>
{
    public void Configure(EntityTypeBuilder<NewsItem> b)
    {
        b.ToTable("NewsItems");
        b.HasKey(x => x.NewsItemId);
        b.Property(x => x.Source).HasMaxLength(20).IsUnicode(false).IsRequired();
        b.Property(x => x.ExternalKey).HasMaxLength(200).IsUnicode(false).IsRequired();
        b.Property(x => x.Headline).HasMaxLength(500).IsRequired();
        b.Property(x => x.Url).HasMaxLength(500);
        b.Property(x => x.PlayerName).HasMaxLength(120);

        // Makes the nightly sync an idempotent upsert instead of a duplicator.
        b.HasIndex(x => new { x.Source, x.ExternalKey }).IsUnique();
        b.HasIndex(x => x.PublishedUtc).IsDescending();

        b.HasOne(x => x.Player)
            .WithMany()
            .HasForeignKey(x => x.PlayerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
