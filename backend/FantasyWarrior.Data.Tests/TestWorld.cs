using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// Builds a small but complete pool in the database: a league, teams, players,
/// a couple of scoring weeks.
///
/// Every test gets its own league (with its own join code), so they can share
/// one database without ordering constraints or cleanup between them — which
/// matters because creating a database per test would dominate the run time.
/// </summary>
public sealed class TestWorld(FantasyWarriorDbContext db)
{
    private static int _counter;

    public League League { get; private set; } = null!;

    public List<Team> Teams { get; } = [];

    public List<Period> Periods { get; } = [];

    public async Task<TestWorld> CreateAsync(
        int teams = 2, int periods = 2, string season = "20252026")
    {
        var n = Interlocked.Increment(ref _counter);
        var now = DateTime.UtcNow;

        var commissioner = new User
        {
            Username = $"gm{n}a",
            DisplayName = $"GM {n}A",
            CreatedUtc = now,
        };
        db.Users.Add(commissioner);
        await db.SaveChangesAsync();

        League = new League
        {
            Name = $"Test League {n}",
            Season = season,
            JoinCode = JoinCodes.New(),
            CommissionerUserId = commissioner.UserId,
            CapAmount = 100_000_000,
            ActiveForwards = 2,
            ActiveDefense = 1,
            ActiveGoalies = 1,
            CreatedUtc = now,
        };
        db.Leagues.Add(League);
        await db.SaveChangesAsync();

        for (var i = 0; i < teams; i++)
        {
            var owner = i == 0
                ? commissioner
                : new User { Username = $"gm{n}{(char)('a' + i)}", DisplayName = $"GM {n}", CreatedUtc = now };
            if (i > 0)
            {
                db.Users.Add(owner);
                await db.SaveChangesAsync();
            }

            var team = new Team
            {
                LeagueId = League.LeagueId,
                OwnerUserId = owner.UserId,
                Name = $"Team {n}-{i}",
                CreatedUtc = now,
            };
            db.Teams.Add(team);
            db.LeagueMembers.Add(new LeagueMember
            {
                LeagueId = League.LeagueId,
                UserId = owner.UserId,
                JoinedUtc = now,
            });
            Teams.Add(team);
        }
        await db.SaveChangesAsync();

        // Weeks run Monday to Sunday; the first Monday of the 2025-26 season is
        // 2025-10-06.
        var start = new DateOnly(2025, 10, 6);
        for (var i = 1; i <= periods; i++)
        {
            var period = new Period
            {
                Season = season,
                // Offset by the counter so parallel worlds don't collide on the
                // (Season, Number) unique index.
                Number = n * 100 + i,
                StartDate = start.AddDays((i - 1) * 7),
                EndDate = start.AddDays((i - 1) * 7 + 6),
                LockUtc = start.AddDays((i - 1) * 7).ToDateTime(TimeOnly.MinValue),
                GameCount = 40,
                CreatedUtc = now,
            };
            db.Periods.Add(period);
            Periods.Add(period);
        }
        await db.SaveChangesAsync();
        return this;
    }

    /// <summary>A player, with an optional contract for the league's season.</summary>
    public async Task<Player> AddPlayerAsync(string position = "C", long? capHit = null, string? season = null)
    {
        var id = 8_000_000 + Interlocked.Increment(ref _counter);
        var player = new Player
        {
            PlayerId = id,
            FirstName = "Test",
            LastName = $"Player{id}",
            Position = position,
            TeamAbbrev = "MTL",
            Status = PlayerStatus.Nhl,
            LastSyncedUtc = DateTime.UtcNow,
        };
        db.Players.Add(player);

        if (capHit is not null)
            db.PlayerContracts.Add(new PlayerContract
            {
                PlayerId = id,
                Season = season ?? League.Season,
                CapHit = capHit.Value,
                Source = ContractSource.Estimated,
                ImportedUtc = DateTime.UtcNow,
            });

        await db.SaveChangesAsync();
        return player;
    }

    public async Task<RosterSpot> AddSpotAsync(
        Team team, Player player, DateOnly? start = null, DateOnly? end = null)
    {
        var spot = new RosterSpot
        {
            LeagueId = League.LeagueId,
            TeamId = team.TeamId,
            PlayerId = player.PlayerId,
            PositionGroup = player.Position switch { "D" => "D", "G" => "G", _ => "F" },
            StartDate = start ?? Periods[0].StartDate,
            EndDate = end,
            StartReason = RosterSpotStartReason.Draft,
            EndReason = end is null ? null : RosterSpotEndReason.Release,
            OpenedUtc = DateTime.UtcNow,
            ClosedUtc = end is null ? null : DateTime.UtcNow,
        };
        db.RosterSpots.Add(spot);
        await db.SaveChangesAsync();
        return spot;
    }

    public async Task<RosterAssignment> AddAssignmentAsync(
        RosterSpot spot, Period period, bool active, double points,
        int gamesPlayed = 1, int goals = 0, int assists = 0, bool finalized = false)
    {
        var assignment = new RosterAssignment
        {
            RosterSpotId = spot.RosterSpotId,
            PeriodId = period.PeriodId,
            IsActive = active,
            EffectiveFrom = period.StartDate,
            EffectiveTo = period.EndDate,
            GamesPlayed = gamesPlayed,
            Goals = goals,
            Assists = assists,
            FantasyPoints = points,
            IsFinalized = finalized,
            ScoredUtc = DateTime.UtcNow,
        };
        db.RosterAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return assignment;
    }

    public async Task AddGameLineAsync(
        Player player, DateOnly date, byte gameType = Entities.GameType.RegularSeason,
        int goals = 0, int assists = 0, string? decision = null,
        bool shutout = false, bool otLoss = false, string? season = null)
    {
        var gameId = 2_000_000_000L + Interlocked.Increment(ref _counter);
        db.Games.Add(new Game
        {
            GameId = gameId,
            Season = season ?? League.Season,
            GameType = gameType,
            GameDate = date,
            HomeTeamAbbrev = "MTL",
            AwayTeamAbbrev = "TOR",
            SyncedUtc = DateTime.UtcNow,
        });
        db.PlayerGameStats.Add(new PlayerGameStat
        {
            GameId = gameId,
            PlayerId = player.PlayerId,
            GameDate = date,
            Season = season ?? League.Season,
            GameType = gameType,
            TeamAbbrev = "MTL",
            OpponentAbbrev = "TOR",
            Position = player.Position,
            IsGoalie = player.Position == "G",
            IsHome = true,
            Goals = goals,
            Assists = assists,
            Points = goals + assists,
            Decision = decision,
            Shutout = shutout,
            OtLoss = otLoss,
            SyncedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
