using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.Nhl;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Imports finished NHL games and their per-player boxscore lines into
/// <see cref="Game"/> and <see cref="PlayerGameStat"/>. Idempotent: safe to
/// re-run over any date range, for a nightly catch-up or a whole-season
/// backfill.
///
/// This is the largest table in the system — about 51,000 rows for one season —
/// and the one the Firestore cost model was built around. Here it is just a
/// table, so a full-season backfill is a normal operation rather than a
/// deliberate spend of an entire day's read quota.
///
/// The NHL fetch and every parsing rule are reused verbatim from the Firestore
/// job, including the shutout approximation and the "MM:SS" time-on-ice parser,
/// which have their own tests.
/// </summary>
public sealed class StatsSyncJob(NhlApiClient nhl, FantasyWarriorDbContext db)
{
    private static readonly string[] FinishedStates = ["OFF", "FINAL"];

    public async Task<(int Games, int PlayerLines)> RunAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Console.WriteLine($"=== stats-sync  {from:yyyy-MM-dd} -> {to:yyyy-MM-dd} ===");

        // Team abbreviations are foreign keys; a code we have no franchise row
        // for would fail the whole save rather than the one game.
        var knownTeams = (await db.NhlTeams.Select(t => t.Abbrev).ToListAsync(ct)).ToHashSet();
        var knownPlayers = (await db.Players.Select(p => p.PlayerId).ToListAsync(ct)).ToHashSet();

        int totalGames = 0, totalLines = 0, callUps = 0;
        foreach (var date in EnumerateDates(from, to))
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var games = (await nhl.GetDailyScoresAsync(dateStr, ct))
                .Where(g => FinishedStates.Contains(g.GameState) && g.GameDate == dateStr)
                .ToList();
            if (games.Count == 0)
            {
                Console.WriteLine($"  {dateStr}: no finished games");
                continue;
            }

            // One round-trip for the day's existing rows, so a re-run updates
            // instead of colliding on the primary key.
            var gameIds = games.Select(g => g.Id).ToList();
            var existingGames = await db.Games
                .Where(g => gameIds.Contains(g.GameId))
                .ToDictionaryAsync(g => g.GameId, ct);
            var existingLines = await db.PlayerGameStats
                .Where(l => gameIds.Contains(l.GameId))
                .ToDictionaryAsync(l => (l.GameId, l.PlayerId), ct);

            var now = DateTime.UtcNow;
            var dayLines = 0;
            foreach (var game in games)
            {
                if (!knownTeams.Contains(game.HomeTeam.Abbrev) || !knownTeams.Contains(game.AwayTeam.Abbrev))
                {
                    Console.WriteLine($"  ! game {game.Id}: unknown franchise "
                        + $"({game.AwayTeam.Abbrev} @ {game.HomeTeam.Abbrev}), skipped");
                    continue;
                }

                var box = await nhl.GetBoxscoreAsync(game.Id, ct);
                if (box is null)
                {
                    Console.WriteLine($"  ! no boxscore for game {game.Id}, skipped");
                    continue;
                }

                if (existingGames.TryGetValue(game.Id, out var gameRow))
                {
                    ApplyGame(gameRow, game, now);
                }
                else
                {
                    gameRow = new Game
                    {
                        GameId = game.Id,
                        Season = game.Season.ToString(),
                        HomeTeamAbbrev = game.HomeTeam.Abbrev,
                        AwayTeamAbbrev = game.AwayTeam.Abbrev,
                    };
                    ApplyGame(gameRow, game, now);
                    db.Games.Add(gameRow);
                }

                foreach (var (built, source) in BuildPlayerLines(game, box, now))
                {
                    // A game line references a player, so someone who dressed
                    // without being on any synced roster -- a call-up, an
                    // emergency backup -- would fail the whole day's save. The
                    // boxscore is the authority on who actually played, so we
                    // create the player from it rather than dropping his stats.
                    if (!knownPlayers.Contains(built.PlayerId))
                    {
                        db.Players.Add(StubPlayer(built, source, now));
                        knownPlayers.Add(built.PlayerId);
                        callUps++;
                    }

                    if (existingLines.TryGetValue((built.GameId, built.PlayerId), out var lineRow))
                        Copy(built, lineRow);
                    else
                        db.PlayerGameStats.Add(built);
                    dayLines++;
                }
                totalGames++;
            }

            await db.SaveChangesAsync(ct);
            // Days are saved one at a time and the tracker cleared: a full
            // season is ~51,000 rows, and holding them all in the change
            // tracker would make every SaveChanges slower than the last.
            db.ChangeTracker.Clear();
            totalLines += dayLines;
            Console.WriteLine($"  {dateStr}: {games.Count} games, {dayLines} player lines");
        }

        Console.WriteLine($"\n{totalGames} games, {totalLines} player lines"
            + (callUps > 0 ? $", {callUps} player(s) created from boxscores." : "."));
        return (totalGames, totalLines);
    }

    /// <summary>
    /// A minimal player built from a boxscore line, for someone who played
    /// before any roster sync knew about him. Marked <c>prospect</c>, not
    /// <c>nhl</c>: he is real enough to own stats, but nothing here confirms he
    /// holds an NHL roster spot, and the next player-sync will correct it with
    /// the full record.
    /// </summary>
    private static Player StubPlayer(PlayerGameStat line, BoxPlayerDto source, DateTime now)
    {
        var full = source.Name?.Default ?? $"Player {line.PlayerId}";
        var space = full.LastIndexOf(' ');
        return new Player
        {
            PlayerId = line.PlayerId,
            FirstName = space > 0 ? full[..space] : "",
            LastName = space > 0 ? full[(space + 1)..] : full,
            Position = line.Position,
            TeamAbbrev = line.TeamAbbrev,
            Status = PlayerStatus.Prospect,
            LastSyncedUtc = now,
        };
    }

    public static IEnumerable<DateOnly> EnumerateDates(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }

    private static void ApplyGame(Game row, ScoreGameDto game, DateTime now)
    {
        row.Season = game.Season.ToString();
        row.GameType = (byte)game.GameType;
        row.GameDate = DateOnly.Parse(game.GameDate);
        row.HomeTeamAbbrev = game.HomeTeam.Abbrev;
        row.AwayTeamAbbrev = game.AwayTeam.Abbrev;
        row.HomeScore = game.HomeTeam.Score;
        row.AwayScore = game.AwayTeam.Score;
        row.LastPeriodType = game.GameOutcome?.LastPeriodType;
        row.SyncedUtc = now;
    }

    /// <summary>
    /// Each line paired with the boxscore entry it came from, so an unknown
    /// player can be created with his real name rather than an id.
    /// </summary>
    private static IEnumerable<(PlayerGameStat Line, BoxPlayerDto Source)> BuildPlayerLines(
        ScoreGameDto game, BoxscoreDto box, DateTime now)
    {
        var sides = new[]
        {
            (Players: box.PlayerByGameStats.AwayTeam, Team: game.AwayTeam.Abbrev, Opponent: game.HomeTeam.Abbrev, IsHome: false),
            (Players: box.PlayerByGameStats.HomeTeam, Team: game.HomeTeam.Abbrev, Opponent: game.AwayTeam.Abbrev, IsHome: true),
        };

        foreach (var (players, team, opponent, isHome) in sides)
        {
            foreach (var skater in players.Forwards.Concat(players.Defense))
                yield return (ToSkaterLine(skater, game, team, opponent, isHome, now), skater);

            // A dressed backup who never played is not a game played, and
            // counting him would put a zero-minute start in his season totals.
            foreach (var goalie in players.Goalies.Where(g => TimeOnIce(g.Toi) > 0))
                yield return (ToGoalieLine(goalie, players.Goalies, game, team, opponent, isHome, now), goalie);
        }
    }

    private static PlayerGameStat Base(
        BoxPlayerDto p, ScoreGameDto game, string team, string opponent, bool isHome, DateTime now) => new()
    {
        GameId = game.Id,
        PlayerId = p.PlayerId,
        GameDate = DateOnly.Parse(game.GameDate),
        Season = game.Season.ToString(),
        GameType = (byte)game.GameType,
        TeamAbbrev = team,
        OpponentAbbrev = opponent,
        Position = string.IsNullOrWhiteSpace(p.Position) ? "C" : p.Position[..1],
        IsHome = isHome,
        Toi = p.Toi,
        Pim = p.Pim,
        SyncedUtc = now,
    };

    private static PlayerGameStat ToSkaterLine(
        BoxPlayerDto p, ScoreGameDto game, string team, string opponent, bool isHome, DateTime now)
    {
        var line = Base(p, game, team, opponent, isHome, now);
        line.IsGoalie = false;
        line.Goals = p.Goals ?? 0;
        line.Assists = p.Assists ?? 0;
        line.Points = p.Points ?? 0;
        line.PlusMinus = p.PlusMinus ?? 0;
        line.Shots = p.Sog ?? 0;
        line.Hits = p.Hits ?? 0;
        line.BlockedShots = p.BlockedShots ?? 0;
        line.PowerPlayGoals = p.PowerPlayGoals ?? 0;
        return line;
    }

    private static PlayerGameStat ToGoalieLine(
        BoxPlayerDto goalie, List<BoxPlayerDto> teamGoalies, ScoreGameDto game,
        string team, string opponent, bool isHome, DateTime now)
    {
        var line = Base(goalie, game, team, opponent, isHome, now);
        line.IsGoalie = true;
        line.ShotsAgainst = goalie.ShotsAgainst ?? 0;
        line.Saves = goalie.Saves ?? 0;
        line.GoalsAgainst = goalie.GoalsAgainst ?? 0;
        line.Decision = goalie.Decision;
        line.Starter = goalie.Starter;
        line.Shutout = IsShutout(goalie, teamGoalies);
        line.OtLoss = goalie.Decision == "O";
        return line;
    }

    /// <summary>Copies a freshly built line onto the tracked row, leaving keys alone.</summary>
    private static void Copy(PlayerGameStat from, PlayerGameStat to)
    {
        to.GameDate = from.GameDate;
        to.Season = from.Season;
        to.GameType = from.GameType;
        to.TeamAbbrev = from.TeamAbbrev;
        to.OpponentAbbrev = from.OpponentAbbrev;
        to.Position = from.Position;
        to.IsGoalie = from.IsGoalie;
        to.IsHome = from.IsHome;
        to.Toi = from.Toi;
        to.Pim = from.Pim;
        to.Goals = from.Goals;
        to.Assists = from.Assists;
        to.Points = from.Points;
        to.PlusMinus = from.PlusMinus;
        to.Shots = from.Shots;
        to.Hits = from.Hits;
        to.BlockedShots = from.BlockedShots;
        to.PowerPlayGoals = from.PowerPlayGoals;
        to.ShotsAgainst = from.ShotsAgainst;
        to.Saves = from.Saves;
        to.GoalsAgainst = from.GoalsAgainst;
        to.Decision = from.Decision;
        to.Starter = from.Starter;
        to.Shutout = from.Shutout;
        to.OtLoss = from.OtLoss;
        to.SyncedUtc = from.SyncedUtc;
    }

    /// <summary>
    /// NHL rule approximation: a shutout requires being the only goalie who
    /// played for the team and allowing zero goals. Shootout goals are excluded
    /// because they never appear in goalsAgainst.
    /// </summary>
    public static bool IsShutout(BoxPlayerDto goalie, IEnumerable<BoxPlayerDto> teamGoalies) =>
        (goalie.GoalsAgainst ?? 0) == 0
        && TimeOnIce(goalie.Toi) > 0
        && teamGoalies.Count(g => TimeOnIce(g.Toi) > 0) == 1;

    /// <summary>
    /// "MM:SS" as seconds; 0 when empty or unparseable. Minutes can exceed 59
    /// in overtime, so this cannot be a TimeSpan parse.
    /// </summary>
    public static int TimeOnIce(string? toi)
    {
        if (string.IsNullOrWhiteSpace(toi)) return 0;
        var parts = toi.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s)
            ? m * 60 + s
            : 0;
    }
}
