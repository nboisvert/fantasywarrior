using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Players;

/// <summary>
/// Who counts as a prospect: a player with no NHL game to his name (Nick,
/// 2026-08-25). He stops being one the day he plays his first, and nothing has
/// to be told about it.
///
/// <b>Not <see cref="Player.Status"/>.</b> That column already carries the word
/// "prospect", but it means something else — not on an NHL club's season
/// roster. The two sets overlap and are not the same one: of the 41 players on
/// Les Mordus rosters with no NHL game on 2026-08-25, 19 carried
/// <see cref="PlayerStatus.Nhl"/>, dressed by a club and yet to play a minute.
/// Reading Status here would have quietly answered a different question.
///
/// <b>Derived, never stored.</b> A column would need career-sync to maintain
/// it, and career-sync is not in the nightly chain — the flag would sit stale
/// exactly on the day it matters, the debut. This is one indexed read against
/// a table of a few thousand rows instead, in the same house style as
/// "there is no cache to keep fresh".
/// </summary>
public static class Prospects
{
    /// <summary>The NHL's own abbreviation in <see cref="PlayerCareerSeasonStat.LeagueAbbrev"/>.
    /// Career rows cover junior, NCAA and European leagues too, and none of
    /// those make a player anything but a prospect.</summary>
    private const string Nhl = "NHL";

    /// <summary>
    /// Of <paramref name="playerIds"/>, the ones who are prospects.
    ///
    /// A player career-sync has never reached is deliberately <b>not</b>
    /// reported as one: "we looked and he has no NHL games" and "we never
    /// looked" are different states, the same distinction
    /// <see cref="Player.DraftChecked"/> exists to keep. Calling an unsynced
    /// veteran a prospect would be the worse error of the two — it would sink
    /// him to the bottom of his own GM's grid.
    /// </summary>
    public static async Task<HashSet<long>> ForAsync(
        FantasyWarriorDbContext db, IReadOnlyCollection<long> playerIds, CancellationToken ct = default)
    {
        if (playerIds.Count == 0) return [];

        var known = await db.Players
            .AsNoTracking()
            .Where(p => playerIds.Contains(p.PlayerId) && p.CareerStatsSyncedUtc != null)
            .Select(p => p.PlayerId)
            .ToListAsync(ct);

        // GamesPlayed > 0 rather than merely "has an NHL row": the NHL's own
        // payload carries a season line for a player called up and never
        // dressed, and being on the sheet is not having played.
        var played = await db.PlayerCareerSeasonStats
            .AsNoTracking()
            .Where(s => playerIds.Contains(s.PlayerId) && s.LeagueAbbrev == Nhl && s.GamesPlayed > 0)
            .Select(s => s.PlayerId)
            .Distinct()
            .ToListAsync(ct);

        var hasPlayed = played.ToHashSet();
        return known.Where(id => !hasPlayed.Contains(id)).ToHashSet();
    }
}
