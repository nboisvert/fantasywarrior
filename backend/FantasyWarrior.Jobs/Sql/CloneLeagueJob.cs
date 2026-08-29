using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Copies an existing league's rules and rosters into a new one, and — with
/// <c>--drafting</c> — hands it straight to the draft room.
///
/// <b>What it is for.</b> Exercising the off-season on real rosters without
/// touching the league those rosters belong to. Les Mordus is a live pool
/// halfway through a replayed season; its draft cannot be rehearsed in place,
/// because opening one means advancing its phase and freezing an order that
/// fourteen real GMs would then have to live with.
///
/// <b>The weeks are not copied</b> — see <see cref="LeagueClone"/>. The copy has
/// never played a game. That is deliberate, and it costs one thing which this
/// job pays for explicitly: with no standings of its own, the copy has no
/// reverse-standings order, so <c>--drafting</c> freezes <b>the source league's</b>
/// order onto it. An order of "whatever sequence the teams were created in"
/// would make the rehearsal worthless for the one thing a pool argues about
/// most.
/// </summary>
public sealed class CloneLeagueJob(FantasyWarriorDbContext db)
{
    /// <param name="protectionSlots">
    /// Overrides <c>League.ProtectionSlots</c> on the copy only. It exists
    /// because a league can have an off-season rule that was agreed but never
    /// written to its row — Les Mordus' nine protections were decided on
    /// 2026-08-28 and are in <c>mordus-pool.md</c>, not in the database. The
    /// rehearsal should not be blocked on that, and it must not be the thing
    /// that quietly edits a live league's rules to unblock itself.
    /// </param>
    public async Task<int> RunAsync(
        string? sourceCode, string? name, bool drafting, bool everyOwnerJoins,
        int? protectionSlots, int? stealRounds, int? maxLosses,
        bool dryRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            Console.Error.WriteLine("clone-league needs --from <joinCode>.");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("clone-league needs --name <name>.");
            return 1;
        }

        var source = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == sourceCode, ct);
        if (source is null)
        {
            Console.Error.WriteLine($"No league with join code {sourceCode}.");
            return 1;
        }

        // Same posture as seed-mordus: a name collision is far likelier to be a
        // re-run than an intention, and a second "Mordus2" would be
        // indistinguishable from the first in every list in the app.
        if (await db.Leagues.AnyAsync(l => l.Name == name, ct))
        {
            Console.Error.WriteLine($"A league named \"{name}\" already exists. Refusing to make a second one.");
            return 1;
        }

        var sourceSeason = await db.LeagueSeasons
            .FirstOrDefaultAsync(s => s.LeagueId == source.LeagueId
                                      && s.Phase != LeagueSeasonPhase.Complete, ct);
        if (sourceSeason is null)
        {
            Console.Error.WriteLine(
                $"{source.Name} has no open season, so nothing says which one the copy prepares.");
            return 1;
        }

        // Which season the copy prepares. If the source is playing the season
        // League.Season names, the copy prepares the one after it — that is the
        // off-season being rehearsed. If the source is already past that (it is
        // itself protecting or drafting), the copy mirrors the same row.
        protectionSlots ??= source.ProtectionSlots;
        stealRounds ??= source.StealRounds;
        maxLosses ??= source.MaxLossesPerTeam;

        var playingNow = sourceSeason.Season == source.Season;
        var season = playingNow ? Season.Next(sourceSeason.Season) : sourceSeason.Season;
        var number = playingNow ? sourceSeason.Number + 1 : sourceSeason.Number;
        var year = Season.StartYear(season);

        var clock = new SimulationClockService(db);
        var today = await clock.TodayEtAsync();

        Console.WriteLine($"=== clone-league{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"  {source.Name} ({source.JoinCode}) -> \"{name}\"");
        Console.WriteLine($"  Preparing season {number} ({Season.Display(season)}), "
            + $"draft year {year}, today {today:yyyy-MM-dd}.");
        Console.WriteLine($"  Off-season rules: protection slots {Show(protectionSlots)}, "
            + $"steal rounds {Show(stealRounds)}, max losses {Show(maxLosses)}, "
            + $"draft rounds {Show(source.DraftRounds)}.");

        if (drafting)
        {
            if (source.DraftRounds is not > 0)
            {
                Console.Error.WriteLine($"{source.Name} has no draft rounds configured — nothing to open.");
                return 1;
            }
            if (protectionSlots is null)
            {
                Console.Error.WriteLine(
                    $"{source.Name} has no protection slots configured, and none was given — "
                    + "every veteran would be exposed. Pass --protection-slots N.");
                return 1;
            }
        }

        if (dryRun)
        {
            var teamCount = await db.Teams.CountAsync(t => t.LeagueId == source.LeagueId, ct);
            var spotCount = await db.RosterSpots
                .Where(s => s.LeagueId == source.LeagueId)
                .Where(RosterWindow.Committed())
                .CountAsync(ct);
            Console.WriteLine($"  Would copy {teamCount} teams and {spotCount} open roster spots, then "
                + $"{(drafting ? "open the draft" : "stop at Protecting")}. Nothing written.");
            return 0;
        }

        var clone = await LeagueClone.CreateAsync(db, source, name, today, everyOwnerJoins, ct);

        // The copy's rules, not the source's — the source is a live pool and
        // this job never edits it.
        clone.League.ProtectionSlots = protectionSlots;
        clone.League.StealRounds = stealRounds;
        clone.League.MaxLossesPerTeam = maxLosses;
        await db.SaveChangesAsync(ct);

        Console.WriteLine($"  {clone.TeamIdBySourceTeamId.Count} teams, "
            + $"{clone.PlayerSpots} players, {clone.FranchiseSpots} franchise slots"
            + $"{(everyOwnerJoins ? "" : " — commissioner only, nobody else sees it")}.");

        var leagueSeason = new LeagueSeason
        {
            LeagueId = clone.League.LeagueId,
            Season = season,
            Number = number,
            Phase = LeagueSeasonPhase.Protecting,
            StartedUtc = DateTime.UtcNow,
        };
        db.LeagueSeasons.Add(leagueSeason);
        await db.SaveChangesAsync(ct);

        var picks = await CreatePicksAsync(clone, year, ct);
        Console.WriteLine($"  {picks.Count} draft picks for {year}.");

        if (!drafting)
        {
            Console.WriteLine($"\nCreated \"{name}\" — join code {clone.League.JoinCode}, Protecting. "
                + "The commissioner opens the draft from the Draft tab.");
            return 0;
        }

        // Protections, exactly as the commissioner's button computes them: each
        // roster's best under this league's own scale, bounded to the simulated
        // day. Shared code, not a second implementation — a sandbox protected by
        // a different rule would be rehearsing a draft nobody will run.
        var slate = await ProtectionSlate.AutofillAsync(
            db, clone.League.LeagueId, Season.Previous(season), protectionSlots!.Value,
            (await clock.StateAsync())?.AsOfDate, write: true, ct);
        Console.WriteLine($"  {slate.Protected} protected ({slate.Slots} per team), "
            + $"{slate.Free} safe on NHL experience, {slate.Exposed} exposed.");

        var order = await FreezeOrderAsync(source, clone, picks, ct);

        leagueSeason.Phase = LeagueSeasonPhase.Drafting;
        await db.SaveChangesAsync(ct);

        Console.WriteLine($"\nCreated \"{name}\" — join code {clone.League.JoinCode}, Drafting.");
        Console.WriteLine($"  Order: {string.Join(", ", order)}");
        return 0;
    }

    /// <summary>"not set" rather than a blank, so a missing rule is readable.</summary>
    private static string Show(int? value) => value?.ToString() ?? "not set";

    private async Task<List<DraftPick>> CreatePicksAsync(
        LeagueCloneResult clone, int year, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var picks = new List<DraftPick>();

        // Every pick starts back with the team it belongs to, even if the source
        // had traded some away: a pick's value is tied to the season it drafts
        // for, and this copy drafts for one the source never traded against.
        foreach (var teamId in clone.TeamIdBySourceTeamId.Values)
            for (var round = 1; round <= clone.League.DraftRounds; round++)
                picks.Add(new DraftPick
                {
                    LeagueId = clone.League.LeagueId,
                    Year = year,
                    Round = round,
                    OriginalTeamId = teamId,
                    CurrentTeamId = teamId,
                    CreatedUtc = now,
                });

        db.DraftPicks.AddRange(picks);
        await db.SaveChangesAsync(ct);
        return picks;
    }

    /// <summary>
    /// Writes the source league's reverse-standings order onto the copy's picks
    /// — what <c>POST .../draft/open</c> does from the league's own standings.
    /// The copy has none, having never played, so the order is borrowed rather
    /// than derived.
    /// </summary>
    private async Task<List<string>> FreezeOrderAsync(
        League source, LeagueCloneResult clone, List<DraftPick> picks, CancellationToken ct)
    {
        var standings = await db.Standings
            .Where(s => s.LeagueId == source.LeagueId)
            .Select(s => new { s.TeamId, s.Score })
            .ToListAsync(ct);

        var order = DraftOrder.ReverseStandings(
            clone.TeamIdBySourceTeamId.Keys.Select(sourceTeamId =>
                (sourceTeamId, standings.FirstOrDefault(s => s.TeamId == sourceTeamId)?.Score ?? 0d)));

        var slotByTeam = order
            .Select((sourceTeamId, index) =>
                (teamId: clone.TeamIdBySourceTeamId[sourceTeamId], pick: index + 1))
            .ToDictionary(x => x.teamId, x => x.pick);

        foreach (var pick in picks)
            pick.PickInRound = slotByTeam.GetValueOrDefault(pick.OriginalTeamId);

        var names = await db.Teams
            .Where(t => t.LeagueId == clone.League.LeagueId)
            .ToDictionaryAsync(t => t.TeamId, t => t.Name, ct);

        return [.. order.Select(sourceTeamId => names[clone.TeamIdBySourceTeamId[sourceTeamId]])];
    }
}
