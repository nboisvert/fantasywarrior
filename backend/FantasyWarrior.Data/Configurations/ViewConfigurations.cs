using FantasyWarrior.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FantasyWarrior.Data.Configurations;

/// <summary>
/// The views are mapped keyless so LINQ can compose filters onto them
/// (<c>.Where(x =&gt; x.LeagueId == id)</c> becomes part of the same query plan)
/// while EF never tries to track, insert or migrate them. Their definitions
/// live in the migration that creates them.
/// </summary>
public sealed class PlayerSeasonStatsViewConfiguration : IEntityTypeConfiguration<PlayerSeasonStatsView>
{
    public void Configure(EntityTypeBuilder<PlayerSeasonStatsView> b) =>
        b.HasNoKey().ToView(ViewNames.PlayerSeasonStats);
}

public sealed class RosterSpotTotalsViewConfiguration : IEntityTypeConfiguration<RosterSpotTotalsView>
{
    public void Configure(EntityTypeBuilder<RosterSpotTotalsView> b) =>
        b.HasNoKey().ToView(ViewNames.RosterSpotTotals);
}

public sealed class TeamPeriodScoreViewConfiguration : IEntityTypeConfiguration<TeamPeriodScoreView>
{
    public void Configure(EntityTypeBuilder<TeamPeriodScoreView> b) =>
        b.HasNoKey().ToView(ViewNames.TeamPeriodScores);
}

public sealed class StandingsViewConfiguration : IEntityTypeConfiguration<StandingsView>
{
    public void Configure(EntityTypeBuilder<StandingsView> b) =>
        b.HasNoKey().ToView(ViewNames.Standings);
}

public sealed class PoolerTradeRecordViewConfiguration : IEntityTypeConfiguration<PoolerTradeRecordView>
{
    public void Configure(EntityTypeBuilder<PoolerTradeRecordView> b) =>
        b.HasNoKey().ToView(ViewNames.PoolerTradeRecord);
}

public static class ViewNames
{
    public const string PlayerSeasonStats = "vPlayerSeasonStats";
    public const string RosterSpotTotals = "vRosterSpotTotals";
    public const string TeamPeriodScores = "vTeamPeriodScores";
    public const string Standings = "vStandings";
    public const string PoolerTradeRecord = "vPoolerTradeRecord";
}
