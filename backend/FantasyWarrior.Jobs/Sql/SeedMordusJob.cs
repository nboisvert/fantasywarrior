using System.Text.Json;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Creates the real "Les Mordus" league from the rosters imported out of Nick's
/// PoolExpert standings PDF (see .claude/doc/mordus.md).
///
/// The roster data is a checked-in artifact (data/mordus-rosters.json), not
/// something this job derives: the PDF's names arrive as one run of
/// concatenated text and had to be segmented against the player list offline.
/// Keeping the result in the repo makes the import reviewable and re-runnable
/// without the PDF.
///
/// Unlike the Firestore version, this seeds **roster spots too** — the model
/// that did not exist when that one was written. Every spot opens on the
/// season's first period start rather than on the day the league is created:
/// dating them from "now" is what made every team score zero the first time
/// this was tried, because every scoring window fell before the spots existed.
/// </summary>
public sealed class SeedMordusJob(FantasyWarriorDbContext db)
{
    private const string LeagueName = "Les Mordus";

    private sealed record RosterFile(string Source, ActiveSlots ActiveSlots, List<TeamEntry> Teams);
    private sealed record ActiveSlots(int Forwards, int Defense, int Goalies);
    private sealed record TeamEntry(string Gm, string Username, string Franchise, string? FranchiseAbbrev,
        List<PlayerEntry> Active, List<PlayerEntry> Reserve);
    private sealed record PlayerEntry(long PlayerId, string Name, string Pos, string Team);

    /// <param name="openingLineup">
    /// Whether to seed week 1's lineup from the roster file's active/reserve
    /// split — the alignment the GMs actually had when the PDF was captured.
    ///
    /// On by default because it is the truthful starting state. Turning it off
    /// leaves week 1 to be auto-filled with each team's best available players,
    /// which is what the Firestore build did, and is therefore the only setting
    /// under which a replay can be compared against
    /// golden-scores-preSql.json — the two produce genuinely different
    /// (and both legitimate) scores, so the oracle only validates the engine
    /// when the inputs match.
    /// </param>
    public async Task<int> RunAsync(
        string file, string season, string commissioner, long capAmount, bool dryRun,
        bool openingLineup = true, CancellationToken ct = default)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Roster file not found: {file}");
            return 1;
        }

        var data = JsonSerializer.Deserialize<RosterFile>(
            await File.ReadAllTextAsync(file, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Console.WriteLine($"=== seed-mordus{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{data.Teams.Count} teams, slots {data.ActiveSlots.Forwards}F/"
            + $"{data.ActiveSlots.Defense}D/{data.ActiveSlots.Goalies}G, cap ${capAmount:N0}");

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
            JoinCode = await UniqueJoinCodeAsync(ct),
            CommissionerUserId = commissionerUser.UserId,
            CapAmount = capAmount,
            RosterMin = 23,
            RosterMax = 35,
            // Three rounds a year, Les Mordus' own rule. It used to be set by
            // hand through the rules PATCH after a seed, which meant every
            // wipe-and-reseed silently produced a league with no draft — and
            // `draft-picks-init` reads this, so it generated nothing and the
            // trade sheet lost its Draft picks section without a word.
            DraftRounds = 3,
            ActiveForwards = data.ActiveSlots.Forwards,
            ActiveDefense = data.ActiveSlots.Defense,
            ActiveGoalies = data.ActiveSlots.Goalies,
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
            // The source PDF's own title says season 3, and the pool has counted
            // its own seasons for years — this is not derivable from the NHL
            // season string.
            Number = 3,
            Phase = LeagueSeasonPhase.InSeason,
            Rules = MordusRules(data, capAmount),
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

            // The Équipe slot: the PDF's `E` line, one franchise per GM, held
            // for life. A roster spot like any other since 2026-08-05 — it
            // scores its franchise's record and can be traded, both of which a
            // column on Teams could not express.
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
            // rosters did, and silently. Comparing against the pre-migration
            // snapshot is what surfaced it.
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

    /// <summary>
    /// Les Mordus' rules, as documented in <c>mordus.md</c> — which stays their
    /// single source, so a change there is a change here and nowhere else.
    ///
    /// <b>The three off-season numbers are written.</b> They were decided long
    /// ago and had never been entered: <c>ProtectionSlots</c>, <c>StealRounds</c>
    /// and <c>MaxLossesPerTeam</c> sat NULL on the live row, and two of them had
    /// no writer anywhere in the app.
    /// </summary>
    private static RuleSet MordusRules(RosterFile data, long capAmount)
    {
        var rules = RuleSetDefaults.ForNewLeague();

        rules.PoolType = PoolType.Keeper;
        rules.Cap.Max = capAmount;
        rules.Cap.DefaultCapHit = 1_000_000;
        rules.Roster.Min = 23;
        rules.Roster.Max = 35;
        // Every GM's `E` line: one NHL franchise apiece, held for life.
        rules.Roster.FranchiseSlot = true;
        rules.Lineup.Slots = new PositionCounts
        {
            Forwards = data.ActiveSlots.Forwards,
            Defense = data.ActiveSlots.Defense,
            Goalies = data.ActiveSlots.Goalies,
        };

        // The Équipe slot's own keys, not the goalie's: they happen to be priced
        // the same here, which is a coincidence, not a shared rule.
        rules.Scoring.Values[StatKeys.TeamWins] = 2;
        rules.Scoring.Values[StatKeys.TeamOtLosses] = 1;
        rules.Scoring.Values[StatKeys.TeamLosses] = 0;

        rules.Protection.Slots = 9;
        rules.Draft.Steal.Rounds = 2;
        rules.Draft.Steal.MaxLossesPerTeam = 2;
        // Three rounds a year. It used to be set by hand through the rules PATCH
        // after a seed, which meant every wipe-and-reseed silently produced a
        // league with no draft — and `draft-picks-init` reads it, so it
        // generated nothing and the trade sheet lost its Draft picks section
        // without a word.
        rules.Draft.RookieRounds = 3;

        return rules;
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
