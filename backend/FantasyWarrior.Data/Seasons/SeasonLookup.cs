using FantasyWarrior.Core.Seasons;
using Microsoft.EntityFrameworkCore;
using Season = FantasyWarrior.Core.Seasons.Season;

namespace FantasyWarrior.Data.Seasons;

/// <summary>
/// Reads the <c>Seasons</c> table — the declared NHL calendar. The arithmetic on
/// the season string itself stays in <see cref="Core.Seasons.Season"/>; this is
/// only the part that needs the database.
/// </summary>
public static class SeasonLookup
{
    /// <summary>
    /// A season's declared regular-season span, or null when no row exists.
    /// Feeds <see cref="SeasonBounds.Resolve"/> alongside what the games say.
    /// </summary>
    public static async Task<SeasonWindow?> DeclaredWindowAsync(
        FantasyWarriorDbContext db, string season, CancellationToken ct = default)
    {
        var row = await db.Seasons.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Season == season, ct);
        return row is null ? null : new SeasonWindow(row.RegularSeasonStart, row.RegularSeasonEnd);
    }

    /// <summary>
    /// Which season a day belongs to, according to the declared calendar: the
    /// one whose regular season contains it, otherwise the next one to open.
    ///
    /// <b>Null when the table cannot answer</b> — an empty table, or a date past
    /// every season we know about. Callers fall back to
    /// <see cref="Core.Seasons.Season.CurrentOn"/>, the September-cutover
    /// heuristic this replaces: a real answer when we have one, a documented
    /// guess when we do not, and never a silent guess dressed as a fact.
    ///
    /// A day in the off-season resolves to the season about to start, which is
    /// what every caller wants — that is the one being prepared, drafted for and
    /// synced.
    /// </summary>
    public static async Task<string?> CurrentOnAsync(
        FantasyWarriorDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var current = await db.Seasons.AsNoTracking()
            .Where(s => s.RegularSeasonStart <= today && today <= s.RegularSeasonEnd)
            .Select(s => s.Season)
            .FirstOrDefaultAsync(ct);
        if (current is not null) return current;

        return await db.Seasons.AsNoTracking()
            .Where(s => s.RegularSeasonStart > today)
            .OrderBy(s => s.RegularSeasonStart)
            .Select(s => s.Season)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// <see cref="CurrentOnAsync"/> with the heuristic fallback applied — what
    /// every job's <c>--season</c> default resolves to.
    /// </summary>
    public static async Task<string> CurrentOrGuessAsync(
        FantasyWarriorDbContext db, DateOnly today, CancellationToken ct = default) =>
        await CurrentOnAsync(db, today, ct) ?? Season.CurrentOn(today);
}
