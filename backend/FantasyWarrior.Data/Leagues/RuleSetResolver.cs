using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Leagues;

/// <summary>
/// Thrown when a league's rules cannot be produced. Every caller is about to
/// enforce a rule, so there is no useful degraded answer: guessing would enforce
/// a rule nobody set.
/// </summary>
public sealed class RuleSetUnavailableException(string message) : InvalidOperationException(message);

/// <summary>
/// The one way to read a league's rules.
///
/// <b>Which season's rules is a real question, and the three entry points are
/// the three answers.</b> During the off-season a league has two live
/// <see cref="LeagueSeason"/> rows — the one it is still being scored on
/// (<see cref="League.Season"/>, sitting <c>Complete</c>) and the one it is
/// preparing (the row that is not <c>Complete</c>) — and they carry different
/// documents. Scoring last season's banked week under next season's scale, or
/// running a draft under the rules of the season that just ended, are both
/// silent errors that a single "get the rules" method would invite.
///
/// <b>An unwritten document is refused, never defaulted.</b> The column defaults
/// to <c>'{}'</c>, which reads as every property's default — no cap, no slots,
/// no protections. Serving that as a league's rules would enforce a
/// configuration nobody chose and would look exactly like a correctly configured
/// permissive league. Run <c>rules-backfill</c>.
/// </summary>
public static class RuleSetResolver
{
    /// <summary>
    /// The rules the league's points are currently scored under — the
    /// <see cref="LeagueSeason"/> whose <c>Season</c> matches
    /// <see cref="League.Season"/>.
    ///
    /// This is what scoring, lineups, the cap and trades read: they all act on
    /// the season being played, which through the whole off-season is still the
    /// one that just finished.
    /// </summary>
    public static Task<RuleSet> ForScoringAsync(
        FantasyWarriorDbContext db, League league, CancellationToken ct = default) =>
        ForSeasonAsync(db, league.LeagueId, league.Season, ct);

    /// <summary>
    /// The rules of the season being prepared — the one <see cref="LeagueSeason"/>
    /// row that is not <c>Complete</c>, guaranteed unique by
    /// <c>UX_LeagueSeasons_OneActivePerLeague</c>.
    ///
    /// This is what the protection phase and the draft read. Note it is the same
    /// row as <see cref="ForScoringAsync"/> for most of the year and a different
    /// one from the moment the next season opens.
    /// </summary>
    public static async Task<RuleSet> ForActiveSeasonAsync(
        FantasyWarriorDbContext db, int leagueId, CancellationToken ct = default)
    {
        var row = await db.LeagueSeasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Phase != LeagueSeasonPhase.Complete, ct)
            ?? throw new RuleSetUnavailableException(
                $"League {leagueId} has no open season, so there are no rules to read. "
                + "Open one with `season-phase --league <joinCode> --to Preparing`.");

        return Checked(row);
    }

    /// <summary>
    /// One named season's rules — the history. What a lifetime pool asks when it
    /// wants to know what season 2 was actually played under.
    /// </summary>
    public static async Task<RuleSet> ForSeasonAsync(
        FantasyWarriorDbContext db, int leagueId, string season, CancellationToken ct = default)
    {
        var row = await db.LeagueSeasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Season == season, ct)
            ?? throw new RuleSetUnavailableException(
                $"League {leagueId} has no season row for {Season.Display(season)}, so its rules "
                + "were never recorded.");

        return Checked(row);
    }

    /// <summary>
    /// The row to write a rules change onto: the season being prepared, tracked
    /// so a change to it saves.
    ///
    /// Deliberately the active season and not the scored one — editing a closed
    /// season's rules would restate what a finished season was played under, and
    /// the whole point of storing rules per season is that it cannot.
    /// </summary>
    public static async Task<LeagueSeason> ForEditingAsync(
        FantasyWarriorDbContext db, int leagueId, CancellationToken ct = default) =>
        await db.LeagueSeasons
            .FirstOrDefaultAsync(s => s.LeagueId == leagueId && s.Phase != LeagueSeasonPhase.Complete, ct)
        ?? throw new RuleSetUnavailableException(
            $"League {leagueId} has no open season, so there are no rules to change. "
            + "Open one with `season-phase --league <joinCode> --to Preparing`.");

    private static RuleSet Checked(LeagueSeason row) =>
        row.Rules.IsUnwritten
            ? throw new RuleSetUnavailableException(
                $"League {row.LeagueId}'s rules for {Season.Display(row.Season)} were never written. "
                + "Run `rules-backfill` to convert them from the league's columns.")
            : row.Rules;
}
