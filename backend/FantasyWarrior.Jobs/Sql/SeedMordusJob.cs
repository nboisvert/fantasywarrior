using System.Text.Json;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Creates the "Les Mordus" league from a roster file and the league's rules
/// file (data/mordus-rules.json, see .claude/doc/mordus.md).
///
/// Both are checked-in artifacts rather than something this job derives, so a
/// seed is reviewable and re-runnable. The roster file carries resolved player
/// ids; the rules file is a serialized <see cref="RuleSet"/>, written to the
/// new season verbatim — this job holds no rule of its own.
///
/// Every spot opens on the season's first period start rather than on the day
/// the league is created: dating them from "now" puts every scoring window
/// before the spots exist, and every team scores zero.
/// </summary>
public sealed class SeedMordusJob(FantasyWarriorDbContext db)
{
    private const string LeagueName = "Les Mordus";

    private sealed record RosterFile(string Source, List<TeamEntry> Teams);
    private sealed record TeamEntry(string Gm, string Username, string Franchise, string? FranchiseAbbrev,
        List<PlayerEntry> Active, List<PlayerEntry> Reserve);
    private sealed record PlayerEntry(long PlayerId, string Name, string Pos, string Team);

    /// <param name="openingLineup">
    /// Whether to seed week 1's lineup from the roster file's active/reserve
    /// split — the alignment the GMs actually had. On by default because it is
    /// the truthful starting state; off leaves week 1 to be auto-filled with
    /// each team's best available players.
    /// </param>
    public async Task<int> RunAsync(
        string file, string rulesFile, string season, string commissioner, bool dryRun,
        bool openingLineup = true, string? joinCode = null,
        int? seasonNumber = null, CancellationToken ct = default)
    {
        if (seasonNumber is null)
        {
            // The pool's own lifetime count predates this app and is derivable
            // from nothing else, so it is never defaulted.
            Console.Error.WriteLine("seed-mordus needs --season-number <N> (the pool's own season count).");
            return 1;
        }
        foreach (var path in new[] { file, rulesFile })
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"File not found: {path}");
                return 1;
            }

        var data = JsonSerializer.Deserialize<RosterFile>(
            await File.ReadAllTextAsync(file, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var rules = RuleSetJson.Deserialize(await File.ReadAllTextAsync(rulesFile, ct));
        var errors = rules.IsUnwritten ? ["no version"] : RuleSetValidation.Validate(rules);
        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"{rulesFile} is not a valid rules document:");
            foreach (var e in errors) Console.Error.WriteLine($"  {e}");
            return 1;
        }

        Console.WriteLine($"=== seed-mordus{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{data.Teams.Count} teams, slots {rules.Lineup.Slots.Forwards}F/"
            + $"{rules.Lineup.Slots.Defense}D/{rules.Lineup.Slots.Goalies}G, cap ${rules.Cap.Max:N0}"
            + (rules.Cap.Min is { } min ? $", floor ${min:N0}" : ""));

        if (await db.Leagues.AnyAsync(l => l.Name == LeagueName && l.Season == season, ct))
        {
            Console.Error.WriteLine($"\"{LeagueName}\" already exists for {season}. Refusing to overwrite it.");
            return 1;
        }

        // Spots must start when the season does, not when the league is created.
        var firstPeriod = await db.Periods
            .Where(p => p.Season == season)
            .OrderBy(p => p.Number)
            .FirstOrDefaultAsync(ct);
        if (firstPeriod is null)
        {
            Console.Error.WriteLine($"No period calendar for {season}. Run sql-period-init first.");
            return 1;
        }
        Console.WriteLine($"Roster spots open on {firstPeriod.StartDate:yyyy-MM-dd} (week 1 start).\n");

        // Every player id is verified before anything is written: a bad id would
        // otherwise fail the save halfway and leave a half-built league.
        var wanted = data.Teams
            .SelectMany(t => t.Active.Concat(t.Reserve))
            .Select(p => p.PlayerId)
            .Distinct()
            .ToList();
        var known = (await db.Players.Where(p => wanted.Contains(p.PlayerId))
            .Select(p => p.PlayerId).ToListAsync(ct)).ToHashSet();
        var missing = wanted.Where(id => !known.Contains(id)).ToHashSet();
        if (missing.Count > 0)
        {
            // A GM can roster a player who never dressed: no NHL roster or
            // prospect list returns him, and no boxscore creates him, but he
            // still occupies a spot and counts against roster size. The roster
            // file is a verified artifact, so it is a good enough source for a
            // stub — the next player-sync fills in the rest if he ever appears.
            Console.WriteLine($"{missing.Count} rostered player(s) unknown to the NHL feeds — "
                + "creating them from the roster file:");
            var seen = new HashSet<long>();
            foreach (var p in data.Teams.SelectMany(t => t.Active.Concat(t.Reserve))
                         .Where(p => missing.Contains(p.PlayerId) && seen.Add(p.PlayerId)))
            {
                Console.WriteLine($"  {p.PlayerId}  {p.Name} ({p.Pos}, {p.Team})");
                var space = p.Name.LastIndexOf(' ');
                db.Players.Add(new Player
                {
                    PlayerId = p.PlayerId,
                    FirstName = space > 0 ? p.Name[..space] : "",
                    LastName = space > 0 ? p.Name[(space + 1)..] : p.Name,
                    Position = string.IsNullOrWhiteSpace(p.Pos) ? "C" : p.Pos[..1],
                    TeamAbbrev = p.Team.Length == 3 ? p.Team : null,
                    Status = PlayerStatus.Prospect,
                    LastSyncedUtc = DateTime.UtcNow,
                });
            }
            if (!dryRun) await db.SaveChangesAsync(ct);
            Console.WriteLine();
        }

        var positions = await db.Players
            .Where(p => wanted.Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, p => p.Position, ct);

        if (dryRun)
        {
            Console.WriteLine($"[DRY RUN] Would create {data.Teams.Count} teams and "
                + $"{data.Teams.Sum(t => t.Active.Count + t.Reserve.Count)} roster spots. Nothing written.");
            return 0;
        }

        var now = DateTime.UtcNow;
        var commissionerUser = await UpsertUserAsync(commissioner, now, ct);

        var league = new League
        {
            Name = LeagueName,
            Season = season,
            JoinCode = joinCode ?? await UniqueJoinCodeAsync(ct),
            CommissionerUserId = commissionerUser.UserId,
            CreatedUtc = now,
        };
        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct);

        // The league's own season row, and with it every rule it plays by. It
        // has to exist here: a league with no LeagueSeason has nowhere to keep
        // its rules, and every consumer refuses on that rather than inventing
        // one — so a seed without this would produce a pool that cannot trade,
        // score or draft.
        db.LeagueSeasons.Add(new LeagueSeason
        {
            LeagueId = league.LeagueId,
            Season = season,
            Number = seasonNumber.Value,
            Phase = LeagueSeasonPhase.InSeason,
            Rules = rules,
            StartedUtc = now,
        });

        var spotCount = 0;
        foreach (var entry in data.Teams)
        {
            var user = await UpsertUserAsync(entry.Username, now, ct, entry.Gm);
            var team = new Team
            {
                LeagueId = league.LeagueId,
                OwnerUserId = user.UserId,
                Name = entry.Franchise,
                FranchiseAbbrev = entry.FranchiseAbbrev,
                CreatedUtc = now,
            };
            db.Teams.Add(team);
            db.LeagueMembers.Add(new LeagueMember
            {
                LeagueId = league.LeagueId, UserId = user.UserId, JoinedUtc = now,
            });
            await db.SaveChangesAsync(ct);

            // The Équipe slot: one franchise per GM. A roster spot like any
            // other — it scores its franchise's record and can be traded, both
            // of which a column on Teams could not express.
            if (entry.FranchiseAbbrev is { } franchise)
                db.RosterSpots.Add(new RosterSpot
                {
                    LeagueId = league.LeagueId,
                    TeamId = team.TeamId,
                    FranchiseAbbrev = franchise,
                    PositionGroup = "T",
                    StartDate = firstPeriod.StartDate,
                    StartReason = RosterSpotStartReason.Draft,
                    OpenedUtc = now,
                });

            var spotsByPlayer = new Dictionary<long, RosterSpot>();
            foreach (var player in entry.Active.Concat(entry.Reserve))
            {
                var spot = new RosterSpot
                {
                    LeagueId = league.LeagueId,
                    TeamId = team.TeamId,
                    PlayerId = player.PlayerId,
                    PositionGroup = PositionGroups.CodeFrom(positions.GetValueOrDefault(player.PlayerId, "C")),
                    StartDate = firstPeriod.StartDate,
                    StartReason = RosterSpotStartReason.Draft,
                    OpenedUtc = now,
                };
                db.RosterSpots.Add(spot);
                spotsByPlayer[player.PlayerId] = spot;
                spotCount++;
            }
            await db.SaveChangesAsync(ct);

            // **The week-1 lineup, as the GMs actually set it.** Without this the
            // scoring pass finds no lineup and auto-fills with each team's best
            // available players — which scores strictly higher than the real
            // rosters did, and silently.
            if (openingLineup)
            {
                var activeSpotIds = entry.Active
                    .Where(p => spotsByPlayer.ContainsKey(p.PlayerId))
                    .Select(p => spotsByPlayer[p.PlayerId].RosterSpotId)
                    .ToHashSet();

                foreach (var (_, spot) in spotsByPlayer)
                    db.RosterAssignments.Add(new RosterAssignment
                    {
                        RosterSpotId = spot.RosterSpotId,
                        PeriodId = firstPeriod.PeriodId,
                        IsActive = activeSpotIds.Contains(spot.RosterSpotId),
                        EffectiveFrom = firstPeriod.StartDate,
                        EffectiveTo = firstPeriod.EndDate,
                        ScoredUtc = now,
                    });

                // Attributed to the GM, not "auto", so the scoring pass treats
                // it as a real submission and does not overwrite it.
                db.TeamPeriodLineups.Add(new TeamPeriodLineup
                {
                    TeamId = team.TeamId,
                    PeriodId = firstPeriod.PeriodId,
                    SetBy = user.Username,
                    SubmittedUtc = now,
                });
                await db.SaveChangesAsync(ct);
            }

            Console.WriteLine($"  {entry.Username,-12} {entry.Franchise,-24} "
                + $"{entry.Active.Count + entry.Reserve.Count,2} players"
                + (entry.FranchiseAbbrev is null ? "" : $"  [{entry.FranchiseAbbrev}]"));
        }

        Console.WriteLine($"\nCreated \"{LeagueName}\" ({league.Season}) — join code {league.JoinCode}, "
            + $"{data.Teams.Count} teams, {spotCount} roster spots.");
        return 0;
    }

    private async Task<User> UpsertUserAsync(
        string username, DateTime now, CancellationToken ct, string? displayName = null)
    {
        var normalized = username.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct);
        if (user is not null) return user;

        user = new User
        {
            Username = normalized,
            DisplayName = displayName ?? username.Trim(),
            CreatedUtc = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task<string> UniqueJoinCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = JoinCodes.New();
            if (!await db.Leagues.AnyAsync(l => l.JoinCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not generate a unique join code in 10 attempts.");
    }
}
