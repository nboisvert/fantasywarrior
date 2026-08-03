using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Creates one season's draft picks: <c>League.DraftRounds</c> picks per team,
/// one per round. Les Mordus run three.
///
/// The analogue of <c>period-init</c>, and manual for the same reason — a
/// calendar for a season that has not started is a decision, not a nightly
/// chore.
///
/// <b>Picks live one year ahead, and only one.</b> They are generated for the
/// season after the one being played, which is exactly what makes "tradable one
/// year in advance" hold without a rule enforcing it: no other year exists to
/// trade. Trading validation refuses any pick id that is not one of these.
///
/// Idempotent by construction — <c>DraftPicks</c> is unique on
/// (LeagueId, Year, Round, OriginalTeamId), so a second run cannot duplicate
/// anything even if this check were wrong.
/// </summary>
public sealed class DraftPicksInitJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(string? leagueCode, int year, bool dryRun, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(leagueCode))
        {
            Console.Error.WriteLine("draft-picks-init needs --league <joinCode>.");
            return 1;
        }

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.JoinCode == leagueCode, ct);
        if (league is null)
        {
            Console.Error.WriteLine($"No league with join code {leagueCode}.");
            return 1;
        }

        if (league.DraftRounds is not { } rounds || rounds < 1)
        {
            Console.Error.WriteLine(
                $"{league.Name} has no draft configured. Set draftRounds in the league rules first.");
            return 1;
        }

        var teams = await db.Teams.Where(t => t.LeagueId == league.LeagueId)
            .OrderBy(t => t.TeamId)
            .ToListAsync(ct);
        if (teams.Count == 0)
        {
            Console.Error.WriteLine($"{league.Name} has no teams yet.");
            return 1;
        }

        var existing = await db.DraftPicks
            .CountAsync(p => p.LeagueId == league.LeagueId && p.Year == year, ct);
        if (existing > 0)
        {
            Console.WriteLine($"{league.Name} already has {existing} picks for {year}. Nothing to do.");
            return 0;
        }

        Console.WriteLine($"{league.Name}: {teams.Count} teams x {rounds} rounds for {year}");
        if (dryRun)
        {
            Console.WriteLine($"  (dry run — would create {teams.Count * rounds} picks)");
            return 0;
        }

        var now = DateTime.UtcNow;
        foreach (var team in teams)
            for (var round = 1; round <= rounds; round++)
                db.DraftPicks.Add(new DraftPick
                {
                    LeagueId = league.LeagueId,
                    Year = year,
                    Round = round,
                    // PickInRound stays null: the order is not known until the
                    // season it drafts for has a standings to derive it from.
                    OriginalTeamId = team.TeamId,
                    CurrentTeamId = team.TeamId,
                    CreatedUtc = now,
                });

        await db.SaveChangesAsync(ct);
        Console.WriteLine($"  Created {teams.Count * rounds} picks.");
        return 0;
    }
}
