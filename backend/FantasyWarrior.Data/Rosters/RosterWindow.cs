using System.Linq.Expressions;
using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Data.Rosters;

/// <summary>
/// The three questions anyone asks of a <see cref="RosterSpot"/>'s date range,
/// named once so they stop being re-derived at every call site.
///
/// <b>Why this exists now.</b> A spot used to be either open or closed, and
/// <c>EndDate IS NULL</c> answered every question at once. That stopped being
/// true when accepting a trade started dating the swap forward: a spot can now
/// have ended *in the future* (its player is still yours this week and leaves
/// on Monday) or begun in the future (his replacement is already yours on
/// paper). <c>EndDate IS NULL</c> gets both of those backwards, and the three
/// questions below no longer have the same answer.
///
/// Written as <see cref="Expression"/> factories rather than plain predicates
/// so EF Core translates them into the WHERE clause instead of fetching a
/// team's whole history and filtering in memory. The composite index on
/// (LeagueId, StartDate, EndDate) covers all three.
/// </summary>
public static class RosterWindow
{
    /// <summary>
    /// Held <b>today</b>: the roster a GM actually has right now, and the one
    /// the salary cap is charged for. A spot promised away in a trade still
    /// counts here until the day it leaves — its player is still scoring for
    /// this team.
    /// </summary>
    public static Expression<Func<RosterSpot, bool>> OwnedOn(DateOnly today) =>
        s => s.StartDate <= today && (s.EndDate == null || s.EndDate >= today);

    /// <summary>
    /// Owning any part of a scoring week — the question the weekly lineup and
    /// the scoring pass ask. A spot that opens or closes mid-week owns only
    /// part of it and still belongs here; <c>PeriodRollupJob</c> has inlined
    /// this predicate since the SQL migration, and forgetting its second half
    /// is the bug that used to make every team's score sag each night until the
    /// week finalized.
    /// </summary>
    public static Expression<Func<RosterSpot, bool>> OwnsPeriod(DateOnly start, DateOnly end) =>
        s => s.StartDate <= end && (s.EndDate == null || s.EndDate >= start);

    /// <summary>
    /// <b>Engaged</b>: what this team is committed to once every accepted trade
    /// has landed. Deliberately blind to both dates — a player arriving on
    /// Monday counts the moment the trade is agreed, and one leaving on Monday
    /// stops counting just as early. That is the whole point of validating a
    /// trade against the engaged figure rather than today's: checking against
    /// today let a GM accept a $9M contract in the morning and bust the cap in
    /// the afternoon, with each trade looking fine on its own.
    ///
    /// <b>This is the old <c>EndDate IS NULL</c> filter, unchanged.</b> It did
    /// not become wrong when trades started dating their spots forward — it
    /// changed meaning. "Never closed" used to be the same question as "held
    /// today"; now it is the same question as "held once everything lands", and
    /// only <see cref="OwnedOn"/> needs the date. Call sites that want the
    /// engaged figure are therefore already correct and simply want a name.
    /// </summary>
    public static Expression<Func<RosterSpot, bool>> Committed() =>
        s => s.EndDate == null;

    /// <summary>
    /// Mine today, but already promised away — the flag the Team screen turns
    /// into "leaving via trade". Held now (so he still scores for me this week)
    /// with an end date already on the books.
    /// </summary>
    public static Expression<Func<RosterSpot, bool>> Engaged(DateOnly today) =>
        s => s.StartDate <= today && s.EndDate != null && s.EndDate >= today;

    /// <summary>
    /// Not here yet — acquired in a trade that has not reached its effective
    /// date. He has a real spot, a real cap hit and a real lineup row for next
    /// week; he simply is not on this week's roster.
    /// </summary>
    public static Expression<Func<RosterSpot, bool>> Arriving(DateOnly today) =>
        s => s.StartDate > today;
}
