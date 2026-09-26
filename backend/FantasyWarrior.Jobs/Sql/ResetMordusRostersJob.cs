using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Resets Les Mordus' roster spots for a new season, from Nick's PoolExpert
/// export (see .claude/doc/mordus.md), and reseeds them fresh.
///
/// Unlike <see cref="SeedMordusJob"/> (which refuses if the league already
/// exists) and <see cref="SeedMordus2Job"/> (which deletes and recreates the
/// whole league), this targets the live, existing "Les Mordus" — the league,
/// its Users, Teams, LeagueMembers, Trades and Messages all survive untouched.
/// Only <see cref="RosterSpot"/>s (and, by cascade, the
/// <see cref="RosterAssignment"/>s scored against them) are wiped and rebuilt,
/// because a keeper league's off-season is normally a handful of steals and
/// trades layered onto spots that already exist — but here the DB's spots
/// reflect a season-long replay that never matched what really happened on
/// PoolExpert, so reconciling them one by one is not worth it. The PDF is
/// taken as the whole truth and everything open is replaced by it.
///
/// Requires the target season to already be declared (`season-init`,
/// `period-init`) and Les Mordus's <c>League.Season</c> to already equal it —
/// `season-phase --to InSeason` is the step that flips that, and this job
/// refuses to guess a roster's date onto a season nobody has opened yet.
/// </summary>
public sealed class ResetMordusRostersJob(FantasyWarriorDbContext db)
{
    private const string LeagueName = "Les Mordus";

    private sealed record DataFile(string Source, string Season, string Commissioner, List<TeamEntry> Teams);
    private sealed record TeamEntry(
        string Username, string Franchise, string FranchiseAbbrev,
        List<PlayerEntry> Active, List<PlayerEntry> Reserve);
    private sealed record PlayerEntry(string First, string Last, string? Team, long? PlayerId = null);
    private sealed record PlayerLite(long PlayerId, string FirstName, string LastName, string Position, string? TeamAbbrev);

    /// <summary>Aliases the PDF's own team hints for franchises the league itself
    /// does not model as their casual abbreviation — used only to disambiguate a
    /// name shared by two real players, never to accept or reject a match on its
    /// own.</summary>
    private static readonly Dictionary<string, string> TeamHintAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TB"] = "TBL", ["SJ"] = "SJS", ["LA"] = "LAK", ["NJ"] = "NJD", ["WIN"] = "WPG",
    };

    public async Task<int> RunAsync(string file, bool dryRun, CancellationToken ct = default)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Roster file not found: {file}");
            return 1;
        }

        var data = JsonSerializer.Deserialize<DataFile>(
            await File.ReadAllTextAsync(file, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Console.WriteLine($"=== reset-mordus-rosters{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{data.Teams.Count} teams, "
            + $"{data.Teams.Sum(t => t.Active.Count + t.Reserve.Count)} roster entries, season {data.Season}.\n");

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Name == LeagueName, ct);
        if (league is null)
        {
            Console.Error.WriteLine($"No league named \"{LeagueName}\".");
            return 1;
        }
        if (league.Season != data.Season)
        {
            Console.Error.WriteLine($"{LeagueName}.Season is {league.Season}, not {data.Season}. "
                + $"Run season-phase --league {league.JoinCode} --to InSeason first (stepping through the "
                + "phases in between) so the league is really open for this season before its rosters are reset.");
            return 1;
        }

        var firstPeriod = await db.Periods
            .Where(p => p.Season == data.Season)
            .OrderBy(p => p.Number)
            .FirstOrDefaultAsync(ct);
        if (firstPeriod is null)
        {
            Console.Error.WriteLine($"No period calendar for {data.Season}. Run season-init and period-init first.");
            return 1;
        }
        Console.WriteLine($"Roster spots will open on {firstPeriod.StartDate:yyyy-MM-dd} (week 1 start).\n");

        // ---- 1. Resolve every roster entry to a Player -----------------------
        var allPlayers = await db.Players.AsNoTracking()
            .Select(p => new PlayerLite(p.PlayerId, p.FirstName, p.LastName, p.Position, p.TeamAbbrev))
            .ToListAsync(ct);
        var positionByPlayerId = allPlayers.ToDictionary(p => p.PlayerId, p => p.Position);

        var resolved = new Dictionary<(string Franchise, string Last, string First), long>();
        var unresolved = new List<string>();
        var autoCorrected = new List<string>();

        foreach (var team in data.Teams)
        {
            foreach (var entry in team.Active.Concat(team.Reserve))
            {
                string? note;
                long? match;
                if (entry.PlayerId is { } explicitId)
                {
                    match = allPlayers.Any(p => p.PlayerId == explicitId) ? explicitId : null;
                    note = null;
                }
                else
                {
                    match = ResolvePlayer(entry, allPlayers, out note);
                }
                if (match is null)
                {
                    unresolved.Add($"  {team.Franchise,-22} {entry.First} {entry.Last}");
                    continue;
                }
                if (note is not null) autoCorrected.Add(note);
                resolved[(team.FranchiseAbbrev, entry.Last, entry.First)] = match.Value;
            }
        }

        if (autoCorrected.Count > 0)
        {
            Console.WriteLine($"{autoCorrected.Count} name(s) matched by spelling correction:");
            foreach (var n in autoCorrected) Console.WriteLine("  " + n);
            Console.WriteLine();
        }

        if (unresolved.Count > 0)
        {
            Console.WriteLine($"{unresolved.Count} roster name(s) matched no player in the NHL feeds:");
            foreach (var u in unresolved) Console.WriteLine(u);
            Console.WriteLine($"\nRefusing to guess. Fix the name in {file} (or run player-resolve first) and re-run.");
            return 1;
        }

        // ---- 2. Wipe the league's existing roster spots -----------------------
        var leagueId = league.LeagueId;
        var existingSpots = await db.RosterSpots.CountAsync(s => s.LeagueId == leagueId, ct);
        Console.WriteLine($"Existing roster spots to replace: {existingSpots}\n");

        if (dryRun)
        {
            Console.WriteLine($"[DRY RUN] Would wipe {existingSpots} roster spots and create "
                + $"{data.Teams.Sum(t => 1 + t.Active.Count + t.Reserve.Count)} (players + one franchise slot "
                + "per team). Nothing written.");
            return 0;
        }

        var now = DateTime.UtcNow;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // RosterAssignments cascade off RosterSpots, but deleted explicitly
            // first anyway — same defensive ordering as WipePoolsJob and
            // SeedMordus2Job, so a provider that does not honor the cascade
            // fails loudly here rather than leaving orphaned scoring rows.
            await db.RosterAssignments.Where(ra => ra.RosterSpot!.LeagueId == leagueId).ExecuteDeleteAsync(ct);
            await db.RosterSpots.Where(s => s.LeagueId == leagueId).ExecuteDeleteAsync(ct);

            var spotCount = 0;
            foreach (var entry in data.Teams)
            {
                var owner = await db.Users.FirstAsync(u => u.Username == entry.Username, ct);
                var team = await db.Teams
                    .FirstOrDefaultAsync(t => t.LeagueId == leagueId && t.OwnerUserId == owner.UserId, ct);
                if (team is null)
                {
                    team = new Team
                    {
                        LeagueId = leagueId, OwnerUserId = owner.UserId, Name = entry.Franchise, CreatedUtc = now,
                    };
                    db.Teams.Add(team);
                    db.LeagueMembers.Add(new LeagueMember { LeagueId = leagueId, UserId = owner.UserId, JoinedUtc = now });
                }
                team.Name = entry.Franchise;
                team.FranchiseAbbrev = entry.FranchiseAbbrev;
                await db.SaveChangesAsync(ct);

                // The Équipe slot — one NHL franchise per GM, held for life.
                db.RosterSpots.Add(new RosterSpot
                {
                    LeagueId = leagueId,
                    TeamId = team.TeamId,
                    FranchiseAbbrev = entry.FranchiseAbbrev,
                    PositionGroup = "T",
                    StartDate = firstPeriod.StartDate,
                    StartReason = RosterSpotStartReason.Draft,
                    OpenedUtc = now,
                });

                var spotsByPlayer = new Dictionary<long, RosterSpot>();
                foreach (var player in entry.Active.Concat(entry.Reserve))
                {
                    var playerId = resolved[(entry.FranchiseAbbrev, player.Last, player.First)];
                    var spot = new RosterSpot
                    {
                        LeagueId = leagueId,
                        TeamId = team.TeamId,
                        PlayerId = playerId,
                        PositionGroup = PositionGroups.CodeFrom(positionByPlayerId.GetValueOrDefault(playerId, "C")),
                        StartDate = firstPeriod.StartDate,
                        StartReason = RosterSpotStartReason.Draft,
                        OpenedUtc = now,
                    };
                    db.RosterSpots.Add(spot);
                    spotsByPlayer[playerId] = spot;
                    spotCount++;
                }
                await db.SaveChangesAsync(ct);

                // The week-1 lineup, as the export shows it (active block vs.
                // "JOUEURS DE RÉSERVE") — without this the scoring pass finds no
                // lineup and auto-fills with each team's best available players.
                var activeSpotIds = entry.Active
                    .Select(p => spotsByPlayer[resolved[(entry.FranchiseAbbrev, p.Last, p.First)]].RosterSpotId)
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
                db.TeamPeriodLineups.Add(new TeamPeriodLineup
                {
                    TeamId = team.TeamId,
                    PeriodId = firstPeriod.PeriodId,
                    SetBy = data.Commissioner,
                    SubmittedUtc = now,
                });
                await db.SaveChangesAsync(ct);

                Console.WriteLine($"  {entry.Username,-12} {entry.Franchise,-24} "
                    + $"{entry.Active.Count + entry.Reserve.Count,2} players  [{entry.FranchiseAbbrev}]");
            }

            await tx.CommitAsync(ct);
            Console.WriteLine($"\nReset \"{LeagueName}\" ({data.Season}) — {data.Teams.Count} teams, "
                + $"{spotCount} player spots + {data.Teams.Count} franchise spots.");
        });

        return 0;
    }

    // ---- name resolution, same approach as SeedMordus2Job -------------------
    // (exact match -> Nom/Prénom swap -> nickname/transliteration on a shared
    // last name -> fuzzy last name gated on a close first name; never guesses
    // among several real candidates).

    private static long? ResolvePlayer(PlayerEntry entry, List<PlayerLite> players, out string? note)
    {
        note = null;
        var wantLast = Normalize(entry.Last);
        var wantFirst = Normalize(entry.First);

        var exact = players.Where(p =>
            Normalize(p.LastName) == wantLast && Normalize(p.FirstName) == wantFirst).ToList();
        if (exact.Count == 1) return exact[0].PlayerId;
        if (exact.Count > 1)
        {
            var byTeam = FilterByTeamHint(exact, entry.Team);
            if (byTeam.Count == 1)
            {
                note = $"{entry.First} {entry.Last} ({entry.Team}) -> disambiguated by NHL team "
                    + "(more than one player has this exact name)";
                return byTeam[0].PlayerId;
            }
            return null;
        }

        var swapped = players.Where(p =>
            Normalize(p.LastName) == wantFirst && Normalize(p.FirstName) == wantLast).ToList();
        if (swapped.Count == 1)
        {
            note = $"{entry.First} {entry.Last} -> {swapped[0].FirstName} {swapped[0].LastName} "
                + "(Nom/Prénom swapped)";
            return swapped[0].PlayerId;
        }

        var sameLast = players.Where(p => Normalize(p.LastName) == wantLast).ToList();
        if (sameLast.Count > 0)
        {
            var byFirst = sameLast
                .Select(p => (Player: p, Distance: Levenshtein(wantFirst, Normalize(p.FirstName))))
                .Where(x => IsCloseFirstName(wantFirst, Normalize(x.Player.FirstName)))
                .OrderBy(x => x.Distance)
                .ToList();
            if (byFirst.Count == 1 || (byFirst.Count > 1 && byFirst[0].Distance < byFirst[1].Distance))
            {
                note = $"{entry.First} {entry.Last} -> {byFirst[0].Player.FirstName} {byFirst[0].Player.LastName}";
                return byFirst[0].Player.PlayerId;
            }
            return null;
        }

        var close = players
            .Where(p => IsCloseFirstName(wantFirst, Normalize(p.FirstName)))
            .Select(p => (Player: p, Distance: Levenshtein(wantLast, Normalize(p.LastName))))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .ToList();
        if (close.Count == 1 || (close.Count > 1 && close[0].Distance < close[1].Distance))
        {
            note = $"{entry.First} {entry.Last} -> {close[0].Player.FirstName} {close[0].Player.LastName}";
            return close[0].Player.PlayerId;
        }

        return null;
    }

    private static List<PlayerLite> FilterByTeamHint(List<PlayerLite> candidates, string? hint)
    {
        if (hint is null) return candidates;
        var abbrev = TeamHintAlias.GetValueOrDefault(hint.Trim().ToUpperInvariant(), hint.Trim().ToUpperInvariant());
        var byTeam = candidates.Where(p => p.TeamAbbrev == abbrev).ToList();
        return byTeam.Count > 0 ? byTeam : candidates;
    }

    private static bool IsCloseFirstName(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (Levenshtein(a, b) <= 2) return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shorter.Length <= 2) return longer.StartsWith(shorter, StringComparison.Ordinal);
        return longer.StartsWith(shorter, StringComparison.Ordinal) && longer.Length - shorter.Length <= 4;
    }

    private static string Normalize(string s)
    {
        var formD = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in formD)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }
}
