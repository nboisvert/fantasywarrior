using System.Globalization;
using System.Text;
using System.Text.Json;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Rebuilds "Mordus2" — the standalone league that stands in for Les Mordus
/// while Les Mordus itself is mid-replay and cannot have its phase touched
/// (see <see cref="CloneLeagueJob"/>) — from Nick's own spreadsheet and the
/// real GMs' protection photos, instead of from Les Mordus's live rows.
///
/// <b>The spreadsheet is the source of truth here, not the live league.</b>
/// Les Mordus is busy replaying 2025-26 day by day; the real current rosters,
/// 2026-2027 salaries and draft-pick trades live in Nick's own bookkeeping
/// outside the app. <c>data/mordus2.json</c> is that bookkeeping, extracted
/// once and checked in — reviewable and re-runnable, the same posture
/// <see cref="SeedMordusJob"/> takes with the PDF-derived roster file.
///
/// <b>Lands directly in Drafting, not Protecting.</b> The protections in the
/// data file are the real GMs' own choices (photographed off Messenger), not
/// something this job computes — there is nothing left for a protection
/// window to do.
/// </summary>
public sealed class SeedMordus2Job(FantasyWarriorDbContext db)
{
    private const string LeagueName = "Mordus2";
    private const string SourceLeagueName = "Les Mordus";

    private sealed record DataFile(
        string Commissioner, string Season, int SeasonNumber, int DraftYear,
        List<string> DraftOrder, List<TeamEntry> Teams, List<PickEntry> Picks);

    private sealed record TeamEntry(
        string Username, string Franchise, string FranchiseAbbrev, List<PlayerEntry> Roster);

    private sealed record PlayerEntry(
        string LastName, string FirstName, string? NhlTeam, bool Protected, long? PlayerId = null);

    private sealed record PickEntry(int Round, string OriginalFranchiseAbbrev, string OwnedByFranchiseAbbrev);

    private sealed record PlayerLite(long PlayerId, string FirstName, string LastName, string Position, string? TeamAbbrev);

    /// <summary>City names as the spreadsheet spells them (French included) ->
    /// NHL abbreviation, used only to break a same-name tie (two real players
    /// sharing first and last name — e.g. two Elias Petterssons).</summary>
    private static readonly Dictionary<string, string> CityAbbrev = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Anaheim"] = "ANA", ["Boston"] = "BOS", ["Buffalo"] = "BUF", ["Calgary"] = "CGY",
        ["Caroline"] = "CAR", ["Carolina"] = "CAR", ["Chicago"] = "CHI", ["Colorado"] = "COL",
        ["Columbus"] = "CBJ", ["Colombus"] = "CBJ", ["Dallas"] = "DAL", ["Detroit"] = "DET",
        ["Edmonton"] = "EDM", ["Floride"] = "FLA", ["Florida"] = "FLA", ["Los Angeles"] = "LAK",
        ["Los-Angeles"] = "LAK", ["Minnesota"] = "MIN", ["Montreal"] = "MTL", ["Nashville"] = "NSH",
        ["New Jersey"] = "NJD", ["New-Jersey"] = "NJD", ["Devils"] = "NJD", ["New York Islanders"] = "NYI",
        ["NY Islanders"] = "NYI", ["NYIslanders"] = "NYI", ["NY Rangers"] = "NYR", ["Ottawa"] = "OTT",
        ["Philadelphie"] = "PHI", ["Philadelphia"] = "PHI", ["Pittsburgh"] = "PIT", ["San Jose"] = "SJS",
        ["Seattle"] = "SEA", ["St-Louis"] = "STL", ["St. Louis"] = "STL", ["Tampa Bay"] = "TBL",
        ["Toronto"] = "TOR", ["Utah"] = "UTA", ["Vancouver"] = "VAN", ["Vegas"] = "VGK",
        ["Washington"] = "WSH", ["Winnipeg"] = "WPG", ["Panthers"] = "FLA",
    };

    public async Task<int> RunAsync(string file, bool dryRun, CancellationToken ct = default)
    {
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"Data file not found: {file}");
            return 1;
        }

        var data = JsonSerializer.Deserialize<DataFile>(
            await File.ReadAllTextAsync(file, ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Console.WriteLine($"=== seed-mordus2{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{data.Teams.Count} teams, "
            + $"{data.Teams.Sum(t => t.Roster.Count)} roster entries, "
            + $"{data.Teams.Sum(t => t.Roster.Count(p => p.Protected))} protected, "
            + $"{data.Picks.Count} picks, draft year {data.DraftYear}.\n");

        // ---- 1. Resolve every roster entry to a Player ----------------------
        var allPlayers = await db.Players.AsNoTracking()
            .Select(p => new PlayerLite(p.PlayerId, p.FirstName, p.LastName, p.Position, p.TeamAbbrev))
            .ToListAsync(ct);
        var positionByPlayerId = allPlayers.ToDictionary(p => p.PlayerId, p => p.Position);

        var resolved = new Dictionary<(string Team, string Last, string First), long>();
        var unresolved = new List<string>();
        var autoCorrected = new List<string>();

        foreach (var team in data.Teams)
        {
            foreach (var entry in team.Roster)
            {
                string? note;
                long? match;
                if (entry.PlayerId is { } explicitId)
                {
                    // An explicit id in the data file wins outright — used only
                    // for the rare case where the NHL search endpoint itself
                    // cannot disambiguate two real players sharing a full name
                    // (see data/mordus2.json's comment on Elias Pettersson).
                    match = allPlayers.Any(p => p.PlayerId == explicitId) ? explicitId : null;
                    note = null;
                }
                else
                {
                    match = ResolvePlayer(entry, allPlayers, out note);
                }
                if (match is null)
                {
                    unresolved.Add($"  {team.Franchise,-22} {entry.FirstName} {entry.LastName}");
                    continue;
                }
                if (note is not null) autoCorrected.Add(note);
                resolved[(team.FranchiseAbbrev, entry.LastName, entry.FirstName)] = match.Value;
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
            Console.WriteLine("\nRefusing to guess. Fix the name in data/mordus2.json (or run player-resolve "
                + "first) and re-run.");
            return 1;
        }

        // ---- 2. Wipe any existing Mordus2, scoped to that one league --------
        var existing = await db.Leagues.FirstOrDefaultAsync(l => l.Name == LeagueName, ct);
        if (existing is not null)
        {
            var leagueId = existing.LeagueId;
            var counts = new (string Name, int Count)[]
            {
                ("RosterSpots", await db.RosterSpots.CountAsync(s => s.LeagueId == leagueId, ct)),
                ("DraftPicks", await db.DraftPicks.CountAsync(p => p.LeagueId == leagueId, ct)),
                ("Teams", await db.Teams.CountAsync(t => t.LeagueId == leagueId, ct)),
                ("LeagueMembers", await db.LeagueMembers.CountAsync(m => m.LeagueId == leagueId, ct)),
                ("Trades", await db.Trades.CountAsync(t => t.LeagueId == leagueId, ct)),
                ("Messages", await db.Messages.CountAsync(m => m.LeagueId == leagueId, ct)),
            };
            Console.WriteLine($"Existing \"{LeagueName}\" (join code {existing.JoinCode}) will be wiped first:");
            foreach (var (name, count) in counts) Console.WriteLine($"  {name,-16} {count,6}");
            Console.WriteLine();

            if (!dryRun)
            {
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);

                    await db.RosterAssignments
                        .Where(ra => ra.RosterSpot!.LeagueId == leagueId)
                        .ExecuteDeleteAsync(ct);
                    await db.RosterSpots.Where(s => s.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.TradeAssets.Where(a => a.Trade!.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.TradeVotes.Where(v => v.Trade!.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.Messages.Where(m => m.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.TeamPeriodLineups
                        .Where(l => l.Team!.LeagueId == leagueId)
                        .ExecuteDeleteAsync(ct);
                    await db.Trades.Where(t => t.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.DraftSelections
                        .Where(s => s.LeagueSeason!.LeagueId == leagueId)
                        .ExecuteDeleteAsync(ct);
                    await db.DraftPicks.Where(p => p.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.LeagueSeasons.Where(s => s.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.LeagueMembers.Where(m => m.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.Teams.Where(t => t.LeagueId == leagueId).ExecuteDeleteAsync(ct);
                    await db.Leagues.Where(l => l.LeagueId == leagueId).ExecuteDeleteAsync(ct);

                    await tx.CommitAsync(ct);
                });
            }
        }

        // ---- 3. Copy Les Mordus's current live rules -------------------------
        var source = await db.Leagues.FirstOrDefaultAsync(l => l.Name == SourceLeagueName, ct);
        if (source is null)
        {
            Console.Error.WriteLine($"No league named \"{SourceLeagueName}\" — nothing to copy rules from.");
            return 1;
        }
        var sourceSeason = await db.LeagueSeasons
            .FirstOrDefaultAsync(s => s.LeagueId == source.LeagueId && s.Phase != LeagueSeasonPhase.Complete, ct);
        if (sourceSeason is null)
        {
            Console.Error.WriteLine($"{SourceLeagueName} has no open season — nothing says what its rules are.");
            return 1;
        }
        if (sourceSeason.Rules.IsUnwritten)
        {
            Console.Error.WriteLine($"{SourceLeagueName}'s rules were never written. Run rules-backfill first.");
            return 1;
        }
        var rules = RuleSetJson.Copy(sourceSeason.Rules);

        // The off-season numbers, as documented in mordus.md (decided
        // 2026-08-28) rather than whatever the live row happens to carry —
        // that row is known to sit NULL/0 on exactly these fields (see
        // mordus.md, project_status.md), which is a data gap on Les Mordus,
        // not a rule change. Copying it verbatim would hand Mordus2 a draft
        // with no steal rounds and no protection cap. Nick confirmed
        // (2026-09-19): use the documented values here.
        rules.Protection.Slots = 9;
        rules.Draft.Steal.Rounds = 2;
        rules.Draft.Steal.MaxLossesPerTeam = 2;
        rules.Draft.RookieRounds = 3;

        var leagueSeasonValue = Season.Previous(data.Season);
        Console.WriteLine($"Rules copied from {SourceLeagueName}, off-season numbers from mordus.md "
            + $"(protection slots {rules.Protection.Slots}, steal rounds {rules.Draft.Steal.Rounds}, "
            + $"max losses {rules.Draft.Steal.MaxLossesPerTeam}, rookie rounds {rules.Draft.RookieRounds}).");
        Console.WriteLine($"League.Season = {leagueSeasonValue}, LeagueSeason.Season = {data.Season} "
            + $"(#{data.SeasonNumber}), Phase = Drafting.\n");

        if (dryRun)
        {
            Console.WriteLine("[DRY RUN] Nothing written.");
            return 0;
        }

        // ---- 4. Create the league ---------------------------------------------
        var now = DateTime.UtcNow;
        var clock = new SimulationClockService(db);
        var today = await clock.TodayEtAsync();

        var commissioner = await db.Users.FirstAsync(u => u.Username == data.Commissioner, ct);

        var league = new League
        {
            Name = LeagueName,
            Season = leagueSeasonValue,
            JoinCode = await UniqueJoinCodeAsync(ct),
            CommissionerUserId = commissioner.UserId,
            CreatedUtc = now,
        };
        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct);

        db.LeagueSeasons.Add(new LeagueSeason
        {
            LeagueId = league.LeagueId,
            Season = data.Season,
            Number = data.SeasonNumber,
            Phase = LeagueSeasonPhase.Drafting,
            Rules = rules,
            StartedUtc = now,
        });

        var teamIdByAbbrev = new Dictionary<string, int>();
        var spotCount = 0;
        var protectedCount = 0;

        foreach (var entry in data.Teams)
        {
            var owner = await db.Users.FirstAsync(u => u.Username == entry.Username, ct);
            var team = new Team
            {
                LeagueId = league.LeagueId,
                OwnerUserId = owner.UserId,
                Name = entry.Franchise,
                FranchiseAbbrev = entry.FranchiseAbbrev,
                CreatedUtc = now,
            };
            db.Teams.Add(team);
            db.LeagueMembers.Add(new LeagueMember
            {
                LeagueId = league.LeagueId, UserId = owner.UserId, JoinedUtc = now,
            });
            await db.SaveChangesAsync(ct);
            teamIdByAbbrev[entry.FranchiseAbbrev] = team.TeamId;

            // The Équipe slot — one NHL franchise per GM, held for life.
            db.RosterSpots.Add(new RosterSpot
            {
                LeagueId = league.LeagueId,
                TeamId = team.TeamId,
                FranchiseAbbrev = entry.FranchiseAbbrev,
                PositionGroup = "T",
                StartDate = today,
                StartReason = RosterSpotStartReason.Draft,
                OpenedUtc = now,
            });

            foreach (var player in entry.Roster)
            {
                var playerId = resolved[(entry.FranchiseAbbrev, player.LastName, player.FirstName)];
                db.RosterSpots.Add(new RosterSpot
                {
                    LeagueId = league.LeagueId,
                    TeamId = team.TeamId,
                    PlayerId = playerId,
                    PositionGroup = PositionGroups.CodeFrom(
                        positionByPlayerId.GetValueOrDefault(playerId, "C")),
                    StartDate = today,
                    StartReason = RosterSpotStartReason.Draft,
                    ProtectionStatus = player.Protected
                        ? RosterProtectionStatus.Protected
                        : RosterProtectionStatus.Unprotected,
                    OpenedUtc = now,
                });
                spotCount++;
                if (player.Protected) protectedCount++;
            }
            await db.SaveChangesAsync(ct);

            Console.WriteLine($"  {entry.Username,-12} {entry.Franchise,-24} "
                + $"{entry.Roster.Count,2} players, {entry.Roster.Count(p => p.Protected)} protected");
        }

        // ---- 5. Draft picks, ownership and order from the spreadsheet --------
        var orderSlot = data.DraftOrder
            .Select((abbrev, index) => (abbrev, pick: index + 1))
            .ToDictionary(x => x.abbrev, x => x.pick);

        var picks = data.Picks.Select(p => new DraftPick
        {
            LeagueId = league.LeagueId,
            Year = data.DraftYear,
            Round = p.Round,
            PickInRound = orderSlot[p.OriginalFranchiseAbbrev],
            OriginalTeamId = teamIdByAbbrev[p.OriginalFranchiseAbbrev],
            CurrentTeamId = teamIdByAbbrev[p.OwnedByFranchiseAbbrev],
            CreatedUtc = now,
        }).ToList();
        db.DraftPicks.AddRange(picks);
        await db.SaveChangesAsync(ct);

        Console.WriteLine($"\nCreated \"{LeagueName}\" — join code {league.JoinCode}, Drafting.");
        Console.WriteLine($"  {spotCount} roster spots ({protectedCount} protected), {picks.Count} draft picks.");
        Console.WriteLine($"  Order: {string.Join(", ", data.DraftOrder)}");
        return 0;
    }

    /// <summary>
    /// Matches one roster entry to a <see cref="Player"/>. Tries an exact
    /// (accent/case-insensitive) name match first; falls back to a
    /// last-name edit-distance match when the first name agrees exactly —
    /// the spreadsheet has a handful of known typos (Kaprisov/Kaprizov,
    /// Hugues/Hughes...) that an exact match would otherwise report as
    /// missing players who are, in fact, on nobody's roster by mistake.
    /// Returns null — never a guess among several candidates — when nothing
    /// clears that bar.
    /// </summary>
    private static long? ResolvePlayer(
        PlayerEntry entry, List<PlayerLite> players, out string? note)
    {
        note = null;
        var wantLast = Normalize(entry.LastName);
        var wantFirst = Normalize(entry.FirstName);

        // 1. Exact match, as given. Two real people can share a full name
        // (two Elias Petterssons) — when they do, the spreadsheet's own NHL
        // team column is the only thing that tells them apart.
        var exact = players.Where(p =>
            Normalize(p.LastName) == wantLast && Normalize(p.FirstName) == wantFirst).ToList();
        if (exact.Count == 1) return exact[0].PlayerId;
        if (exact.Count > 1)
        {
            var byTeam = FilterByTeamHint(exact, entry.NhlTeam);
            if (byTeam.Count == 1)
            {
                note = $"{entry.FirstName} {entry.LastName} ({entry.NhlTeam}) -> disambiguated by NHL team "
                    + "(more than one player has this exact name)";
                return byTeam[0].PlayerId;
            }
            return null; // still ambiguous — do not guess between two real people.
        }

        // 2. Exact match with Nom/Prénom swapped — a handful of rows in the
        // spreadsheet have the two columns transposed for one player only.
        var swapped = players.Where(p =>
            Normalize(p.LastName) == wantFirst && Normalize(p.FirstName) == wantLast).ToList();
        if (swapped.Count == 1)
        {
            note = $"{entry.FirstName} {entry.LastName} -> {swapped[0].FirstName} {swapped[0].LastName} "
                + "(Nom/Prénom swapped in the spreadsheet)";
            return swapped[0].PlayerId;
        }

        // 3. Exact last name, first name close enough to be the same person —
        // catches nicknames and transliteration variants (Mitch/Mitchell,
        // Nikolaj/Nicolai, "M."/Michael...). "Close enough" is deliberately
        // strict: a shared last name with a genuinely different first name
        // (Kasperi Kapanen vs. Oliver Kapanen; Reilly Smith vs. Will Smith) is
        // two different people, not a typo, and must never be merged.
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
                note = $"{entry.FirstName} {entry.LastName} -> "
                    + $"{byFirst[0].Player.FirstName} {byFirst[0].Player.LastName}";
                return byFirst[0].Player.PlayerId;
            }
            return null; // no first name close enough, or genuinely ambiguous — do not guess.
        }

        // 4. No exact last name at all — fuzzy on the last name (typos like
        // Kaprisov/Kaprizov, Sergatchev/Sergachev), still gated on the first
        // name being recognisably the same.
        var close = players
            .Where(p => IsCloseFirstName(wantFirst, Normalize(p.FirstName)))
            .Select(p => (Player: p, Distance: Levenshtein(wantLast, Normalize(p.LastName))))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .ToList();
        if (close.Count == 1 || (close.Count > 1 && close[0].Distance < close[1].Distance))
        {
            note = $"{entry.FirstName} {entry.LastName} -> {close[0].Player.FirstName} {close[0].Player.LastName}";
            return close[0].Player.PlayerId;
        }

        return null;
    }

    /// <summary>
    /// Whether two first-name spellings are plausibly the same person: close
    /// by edit distance, one an initial of the other ("M." / "Michael"), or
    /// one a short prefix of the other ("Mitch" / "Mitchell"). Anything looser
    /// than this is a different person wearing the same surname, not a typo.
    /// </summary>
    private static List<PlayerLite> FilterByTeamHint(List<PlayerLite> candidates, string? nhlTeam)
    {
        if (nhlTeam is null || !CityAbbrev.TryGetValue(nhlTeam.Trim(), out var abbrev)) return candidates;
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
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(c))
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
