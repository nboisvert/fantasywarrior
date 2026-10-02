using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Leagues;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Creates draft picks: <c>draft.rookieRounds</c> picks per team, one per
/// round, for every draft year the league trades in.
///
/// The analogue of <c>period-init</c>, and manual for the same reason — a
/// calendar for a season that has not started is a decision, not a nightly
/// chore.
///
/// <b>Picks live <c>trades.pickYearsAhead</c> years ahead, and no further</b>
/// (<see cref="DraftPickYears"/>). Without <c>--year</c> every missing year in
/// that window is generated, so running it again after a draft adds just the
/// newly opened year.
///
/// Idempotent by construction — a year that already has picks is skipped, and
/// <c>DraftPicks</c> is unique on (LeagueId, Year, Round, OriginalTeamId), so a
/// second run cannot duplicate anything even if that check were wrong.
/// </summary>
public sealed class DraftPicksInitJob(FantasyWarriorDbContext db)
{
    /// <param name="year">One explicit year; null means the whole window.</param>
    /// <param name="nextDraftYear">The draft after the season being played.</param>
    public async Task<int> RunAsync(
        string? leagueCode, int? year, int nextDraftYear, bool dryRun, CancellationToken ct = default)
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

        // The season being prepared: these picks stock the draft it will run.
        RuleSet rules;
        try
        {
            rules = await RuleSetResolver.ForActiveSeasonAsync(db, league.LeagueId, ct);
        }
        catch (RuleSetUnavailableException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        if (rules.Draft.RookieRounds is not { } rounds || rounds < 1)
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

        var years = year is { } one ? [one] : DraftPickYears.Ahead(nextDraftYear, rules.Trades.PickYearsAhead);
        var now = DateTime.UtcNow;
        foreach (var y in years)
        {
            var existing = await db.DraftPicks.CountAsync(p => p.LeagueId == league.LeagueId && p.Year == y, ct);
            if (existing > 0)
            {
                Console.WriteLine($"{league.Name} already has {existing} picks for {y}. Skipped.");
                continue;
            }

            Console.WriteLine($"{league.Name}: {teams.Count} teams x {rounds} rounds for {y}"
                + (dryRun ? $" (dry run — would create {teams.Count * rounds} picks)" : ""));
            if (dryRun) continue;

            foreach (var team in teams)
                for (var round = 1; round <= rounds; round++)
                    db.DraftPicks.Add(new DraftPick
                    {
                        LeagueId = league.LeagueId,
                        Year = y,
                        Round = round,
                        // PickInRound stays null: the order is not known until the
                        // season it drafts for has a standings to derive it from.
                        OriginalTeamId = team.TeamId,
                        CurrentTeamId = team.TeamId,
                        CreatedUtc = now,
                    });
            await db.SaveChangesAsync(ct);
            Console.WriteLine($"  Created {teams.Count * rounds} picks.");
        }
        return 0;
    }
}
