using System.Text.Json;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Api;

/// <summary>
/// The wire shapes, matching <c>frontend/src/api.ts</c> field for field.
///
/// That match is the whole discipline of this migration: the UI is finished and
/// must not change, so the database underneath it can be replaced entirely as
/// long as these come out identical. Anything renamed here is a frontend change
/// in disguise.
/// </summary>
public static class Dtos
{
    /// <summary>
    /// A league's rules on the wire.
    ///
    /// <b>The document itself, not a projection of it.</b> The rules panel reads
    /// this and PATCHes it straight back, so any reshaping here would be a field
    /// the panel could not round-trip — and a rule silently reset to its default
    /// is exactly the failure this whole design removes. Serializing through
    /// <see cref="RuleSetJson"/> means the wire shape and the stored shape are
    /// the same shape, by construction rather than by discipline.
    /// </summary>
    public static object RuleSet(Core.Rules.RuleSet rules) =>
        JsonSerializer.Deserialize<JsonElement>(RuleSetJson.Serialize(rules));

    /// <summary>
    /// One scoring week. <c>index</c> keeps its name even though the column is
    /// <c>Number</c> — the frontend reads <c>index</c>.
    /// </summary>
    public static object Period(Period period, DateTimeOffset now) => new
    {
        index = period.Number,
        startDate = period.StartDate,
        endDate = period.EndDate,
        gameCount = period.GameCount,
        locked = period.LockUtc <= now.UtcDateTime,
        finalized = period.FinalizedUtc is not null,
    };

    public static object Player(Player p) => new
    {
        id = p.PlayerId,
        name = p.FullName,
        position = p.Position,
        team = p.TeamAbbrev,
        status = p.Status,
        capHit = (long?)null,
        headshotUrl = p.HeadshotUrl,
    };
}
