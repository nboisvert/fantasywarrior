using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Entities.Views;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data;

/// <summary>
/// The whole database.
///
/// Two families of data live here and they behave very differently:
///
/// <list type="bullet">
/// <item><b>NHL reference data</b> (players, games, game lines, news) — global,
/// re-syncable from the NHL API, and by far the largest table
/// (<see cref="PlayerGameStats"/>, ~51,000 rows a season). Losing it costs a
/// re-import, not history.</item>
/// <item><b>Pool data</b> (leagues, teams, roster spots, assignments, trades) —
/// small, irreplaceable, and the only thing worth backing up.</item>
/// </list>
///
/// Delete behaviour is deliberately conservative: cascade only where ownership
/// is unambiguous and single-path (a game owns its lines, a spot owns its
/// weekly assignments), and NO ACTION everywhere else. Wiping pool data for a
/// reseed is an explicit ordered operation, not an accidental side effect of
/// deleting one row.
/// </summary>
public sealed class FantasyWarriorDbContext(DbContextOptions<FantasyWarriorDbContext> options)
    : DbContext(options)
{
    // --- NHL reference ---
    public DbSet<NhlTeam> NhlTeams => Set<NhlTeam>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerContract> PlayerContracts => Set<PlayerContract>();
    public DbSet<PlayerInjury> PlayerInjuries => Set<PlayerInjury>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<PlayerGameStat> PlayerGameStats => Set<PlayerGameStat>();
    public DbSet<PlayerCareerSeasonStat> PlayerCareerSeasonStats => Set<PlayerCareerSeasonStat>();
    public DbSet<NewsItem> NewsItems => Set<NewsItem>();

    // --- calendar ---
    public DbSet<NhlSeason> Seasons => Set<NhlSeason>();
    public DbSet<Period> Periods => Set<Period>();
    public DbSet<SimulationState> SimulationState => Set<SimulationState>();

    // --- pool ---
    public DbSet<User> Users => Set<User>();
    public DbSet<League> Leagues => Set<League>();
    public DbSet<LeagueMember> LeagueMembers => Set<LeagueMember>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<RosterSpot> RosterSpots => Set<RosterSpot>();
    public DbSet<RosterAssignment> RosterAssignments => Set<RosterAssignment>();
    public DbSet<TeamPeriodLineup> TeamPeriodLineups => Set<TeamPeriodLineup>();
    public DbSet<DraftPick> DraftPicks => Set<DraftPick>();
    public DbSet<DraftSelection> DraftSelections => Set<DraftSelection>();

    public DbSet<LeagueSeason> LeagueSeasons => Set<LeagueSeason>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradeAsset> TradeAssets => Set<TradeAsset>();
    public DbSet<TradeVote> TradeVotes => Set<TradeVote>();
    public DbSet<CockcoinAward> CockcoinAwards => Set<CockcoinAward>();
    public DbSet<Message> Messages => Set<Message>();

    // --- views: everything above the assignment grain is derived, never stored.
    // See Team's remarks for why.

    public DbSet<PlayerSeasonStatsView> PlayerSeasonStats => Set<PlayerSeasonStatsView>();
    public DbSet<RosterSpotTotalsView> RosterSpotTotals => Set<RosterSpotTotalsView>();
    public DbSet<TeamPeriodScoreView> TeamPeriodScores => Set<TeamPeriodScoreView>();
    public DbSet<StandingsView> Standings => Set<StandingsView>();
    public DbSet<PoolerTradeRecordView> PoolerTradeRecords => Set<PoolerTradeRecordView>();
    public DbSet<CockcoinBalanceView> CockcoinBalances => Set<CockcoinBalanceView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FantasyWarriorDbContext).Assembly);
    }
}
