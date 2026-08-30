using FantasyWarrior.Core.Rules;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Season = FantasyWarrior.Core.Seasons.Season;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Writes every <see cref="LeagueSeason"/>'s rules document from the columns
/// that used to hold them — the one-time move from ten columns on
/// <c>Leagues</c> plus <c>LeagueScoringRules</c> into one versioned document.
///
/// <b>Idempotent, and it only ever fills a blank.</b> A season whose document
/// has been written is left alone: re-running must not overwrite a rule someone
/// changed since, and re-running is exactly what happens if this ends up in a
/// pipeline. <c>--force</c> exists for a re-import and says what it is.
///
/// <b>Every closed season gets today's rules.</b> Nothing recorded yesterday's,
/// so that is the only thing there is to write — the history is honest from here
/// forward, not backwards. Worth saying out loud rather than discovering later
/// from a season 2 that claims a scale it never played under.
/// </summary>
public sealed class RulesBackfillJob(FantasyWarriorDbContext db)
{
    public async Task<int> RunAsync(
        string? leagueCode, bool force, bool dryRun, CancellationToken ct = default)
    {
        var leagues = await db.Leagues
            .Where(l => leagueCode == null || l.JoinCode == leagueCode)
            .OrderBy(l => l.LeagueId)
            .ToListAsync(ct);

        if (leagues.Count == 0)
        {
            Console.Error.WriteLine(leagueCode is null
                ? "No leagues."
                : $"No league with join code {leagueCode}.");
            return 1;
        }

        Console.WriteLine($"=== rules-backfill{(dryRun ? "  [DRY RUN]" : "")}{(force ? "  [FORCE]" : "")} ===");

        int written = 0, skipped = 0;
        foreach (var league in leagues)
        {
            var legacy = await LegacyOf(league, ct);
            var rules = LegacyRules.ToRuleSet(legacy);

            var errors = RuleSetValidation.Validate(rules);
            if (errors.Count > 0)
            {
                // The conversion is mechanical, so this can only mean the
                // league's columns already contradict each other — worth
                // stopping on rather than persisting a document nothing will
                // accept a later edit to.
                Console.Error.WriteLine($"\n{league.Name} ({league.JoinCode}) does not convert cleanly:");
                foreach (var error in errors) Console.Error.WriteLine($"  - {error}");
                return 1;
            }

            var seasons = await db.LeagueSeasons
                .Where(s => s.LeagueId == league.LeagueId)
                .OrderBy(s => s.Number)
                .ToListAsync(ct);

            Console.WriteLine($"\n{league.Name} ({league.JoinCode}) — {Describe(rules)}");
            if (seasons.Count == 0)
                Console.WriteLine("  no season rows; nothing to write");

            foreach (var season in seasons)
            {
                if (!season.Rules.IsUnwritten && !force)
                {
                    Console.WriteLine($"  season {season.Number} ({Season.Display(season.Season)})  "
                        + "[already written, untouched]");
                    skipped++;
                    continue;
                }

                Console.WriteLine($"  season {season.Number} ({Season.Display(season.Season)})  "
                    + (season.Rules.IsUnwritten ? "-> written" : "-> overwritten"));
                // A copy per row: the documents are independent from here on, and
                // sharing one graph would make a later edit to one season change
                // every other.
                if (!dryRun) season.Rules = LegacyRules.ToRuleSet(legacy);
                written++;
            }
        }

        if (!dryRun) await db.SaveChangesAsync(ct);
        Console.WriteLine(dryRun
            ? $"\nDry run: nothing written ({written} would be, {skipped} left alone)."
            : $"\nWrote {written} season(s); {skipped} already had rules.");
        return 0;
    }

    private async Task<LegacyLeagueRules> LegacyOf(League league, CancellationToken ct)
    {
        var scale = await db.LeagueScoringRules
            .Where(r => r.LeagueId == league.LeagueId)
            .ToDictionaryAsync(r => r.StatKey, r => r.PointValue, ct);

        // Never a column: whether a league uses the Équipe slot was only ever a
        // consequence of how it was seeded, so the truth is in its spots.
        var hasFranchiseSlots = await db.RosterSpots
            .AnyAsync(s => s.LeagueId == league.LeagueId && s.PositionGroup == "T", ct);

        return new LegacyLeagueRules(
            CapAmount: league.CapAmount,
            DefaultCapHit: league.DefaultCapHit,
            RosterMin: league.RosterMin,
            RosterMax: league.RosterMax,
            ActiveForwards: league.ActiveForwards,
            ActiveDefense: league.ActiveDefense,
            ActiveGoalies: league.ActiveGoalies,
            DraftRounds: league.DraftRounds,
            ProtectionSlots: league.ProtectionSlots,
            StealRounds: league.StealRounds,
            MaxLossesPerTeam: league.MaxLossesPerTeam,
            HasFranchiseSlots: hasFranchiseSlots,
            Scale: scale);
    }

    private static string Describe(RuleSet rules) =>
        $"cap {Show(rules.Cap.Max)}, roster {Show(rules.Roster.Min)}-{Show(rules.Roster.Max)}, "
        + $"lineup {rules.Lineup.Slots.Forwards}F/{rules.Lineup.Slots.Defense}D/{rules.Lineup.Slots.Goalies}G"
        + (rules.Roster.FranchiseSlot ? "+T" : "")
        + $", {rules.Scoring.Values.Count} scored stats, protections {Show(rules.Protection.Slots)}, "
        + $"steal rounds {rules.Draft.Steal.Rounds}, rookie rounds {Show(rules.Draft.RookieRounds)}";

    private static string Show(long? value) => value?.ToString("N0") ?? "none";

    private static string Show(int? value) => value?.ToString() ?? "none";
}
