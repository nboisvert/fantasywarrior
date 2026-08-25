using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Seasons;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Api;

/// <summary>
/// Everything the draft needs read from the database, gathered once.
///
/// Every draft endpoint needs the same five things - the season, the frozen
/// order, the entitlements, the selections so far and the teams - and the turn
/// arithmetic is only correct if they were read together. Loading them in one
/// place is what keeps "whose turn is it" from being answered two different
/// ways by two endpoints.
/// </summary>
public sealed record DraftContext(
    League League,
    LeagueSeason Season,
    IReadOnlyList<int> OrderedTeamIds,
    IReadOnlyList<PickSlot> Picks,
    IReadOnlyList<DraftSelection> Selections,
    IReadOnlyDictionary<int, Team> TeamsById)
{
    public int StealRounds => League.StealRounds ?? 0;

    public int SelectionsMade => Selections.Count;

    public DraftTurn? OnTheClock =>
        DraftOrder.OnTheClock(OrderedTeamIds, StealRounds, Picks, SelectionsMade);

    /// <summary>How many players this team has already lost to steals.</summary>
    public int LossesOf(int teamId) => Selections.Count(s => s.StolenFromTeamId == teamId);

    /// <summary>How many players this team has already taken.</summary>
    public int TakesOf(int teamId) =>
        Selections.Count(s => s.TeamId == teamId && s.PlayerId is not null);

    public string TeamName(int teamId) =>
        TeamsById.TryGetValue(teamId, out var t) ? t.Name : "Unknown";

    /// <summary>Everyone who has already changed hands in this draft.</summary>
    public HashSet<long> TakenPlayerIds =>
        [.. Selections.Where(s => s.PlayerId is not null).Select(s => s.PlayerId!.Value)];

    public string? OwnerUsername(int teamId) =>
        TeamsById.TryGetValue(teamId, out var t) ? t.Owner?.Username : null;
}

public static class DraftContextLoader
{
    /// <summary>
    /// Loads the draft as it stands. Returns null when this league has no
    /// active season at all - every caller then answers "no draft" rather than
    /// throwing.
    ///
    /// <b>The order is read from <c>DraftPick.PickInRound</c>, never from the
    /// standings.</b> The standings were frozen into those rows when the draft
    /// opened, and re-reading them live would be a latent bug: entering
    /// <c>InSeason</c> advances <c>Leagues.Season</c>, after which
    /// <c>vStandings</c> reports a different season entirely and reverse
    /// standings would quietly become meaningless.
    ///
    /// <b>Steal order reads <c>OriginalTeamId</c>; rookie order reads
    /// <c>CurrentTeamId</c>.</b> A GM who trades away a first-round rookie pick
    /// must not lose his steal turn with it - steal turns were never his to
    /// trade.
    /// </summary>
    public static async Task<DraftContext?> LoadAsync(
        FantasyWarriorDbContext db, League league, CancellationToken ct = default)
    {
        var season = await Queries.ActiveLeagueSeasonAsync(db, league.LeagueId, ct);
        if (season is null) return null;

        var year = Core.Seasons.Season.StartYear(season.Season);

        var picks = await db.DraftPicks
            .AsNoTracking()
            .Where(p => p.LeagueId == league.LeagueId && p.Year == year)
            .Select(p => new
            {
                p.DraftPickId,
                p.Round,
                p.PickInRound,
                p.OriginalTeamId,
                p.CurrentTeamId,
                p.UsedUtc,
                p.PlayerId,
            })
            .ToListAsync(ct);

        // The frozen reverse-standings order, read off round 1 by the team the
        // pick was *given* to rather than whoever holds it now.
        var ordered = picks
            .Where(p => p.Round == 1 && p.PickInRound is not null)
            .OrderBy(p => p.PickInRound)
            .Select(p => p.OriginalTeamId)
            .ToList();

        var slots = picks
            .Where(p => p.PickInRound is not null)
            .Select(p => new PickSlot(
                p.DraftPickId,
                p.Round,
                p.PickInRound!.Value,
                p.CurrentTeamId,
                Used: p.UsedUtc is not null || p.PlayerId is not null))
            .ToList();

        var selections = await db.DraftSelections
            .AsNoTracking()
            .Where(s => s.LeagueSeasonId == season.LeagueSeasonId)
            .OrderBy(s => s.OverallIndex)
            .ToListAsync(ct);

        var teams = await db.Teams
            .Where(t => t.LeagueId == league.LeagueId)
            .Include(t => t.Owner)
            .ToDictionaryAsync(t => t.TeamId, ct);

        return new DraftContext(league, season, ordered, slots, selections, teams);
    }

    /// <summary>
    /// The pool for the turn on the clock, already filtered and priced.
    ///
    /// Recomputed on every call and never cached - the "max losses per team"
    /// quota closes a whole roster out of the pool the moment its team reaches
    /// the limit, so the answer for a given player changes as other people pick.
    /// </summary>
    public static async Task<IReadOnlyList<DraftPoolRow>> AvailableAsync(
        FantasyWarriorDbContext db, DraftContext ctx, DraftTurn turn, CancellationToken ct = default)
    {
        var rows = turn.Segment == DraftSegment.Steal
            ? await RosteredAsync(db, ctx, ct)
            : await UnrosteredAsync(db, ctx, ct);

        var eligible = rows
            .Where(r => DraftPool.IsEligible(
                r.Candidate, turn.Segment, turn.TeamId, ctx.League.MaxLossesPerTeam))
            .ToList();

        var capHits = await Queries.CapHitsAsync(
            db, ctx.League.Season, eligible.Select(r => r.Candidate.PlayerId).ToList(), ct);

        return eligible
            .Select(r => r with
            {
                CapHit = capHits.TryGetValue(r.Candidate.PlayerId, out var c) ? c : null,
            })
            // Most expensive first: in a capped league the salary is the first
            // thing a GM reads on a draft row, so it is what the list sorts by.
            .OrderByDescending(r => r.CapHit ?? 0)
            .ThenBy(r => r.ShortName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every player held by a team in this league, with his owner.</summary>
    private static async Task<List<DraftPoolRow>> RosteredAsync(
        FantasyWarriorDbContext db, DraftContext ctx, CancellationToken ct)
    {
        var taken = ctx.TakenPlayerIds;

        // PlayerId != null drops the franchise slots before they can ride along
        // as nulls - the same trap that once silently emptied the free-agent
        // list.
        var held = await db.RosterSpots
            .AsNoTracking()
            .Where(s => s.LeagueId == ctx.League.LeagueId && s.PlayerId != null)
            .Where(RosterWindow.Committed())
            .Select(s => new
            {
                PlayerId = s.PlayerId!.Value,
                s.TeamId,
                s.ProtectionStatus,
                s.Player!.FirstName,
                s.Player.LastName,
                s.Player.Position,
                s.Player.PositionGroup,
                s.Player.CareerNhlGames,
                NhlTeam = s.Player.TeamAbbrev,
            })
            .ToListAsync(ct);

        return held
            .Select(h => new DraftPoolRow(
                Candidate: new DraftCandidate(
                    h.PlayerId,
                    h.PositionGroup,
                    h.CareerNhlGames,
                    h.TeamId,
                    h.ProtectionStatus == RosterProtectionStatus.Protected,
                    ctx.LossesOf(h.TeamId),
                    taken.Contains(h.PlayerId)),
                ShortName: DraftFormat.ShortName(h.FirstName, h.LastName),
                Position: h.Position,
                NhlTeam: h.NhlTeam,
                OwnerTeamName: ctx.TeamName(h.TeamId),
                OwnerUsername: ctx.OwnerUsername(h.TeamId),
                CapHit: null))
            .ToList();
    }

    /// <summary>Every player nobody in this league holds.</summary>
    private static async Task<List<DraftPoolRow>> UnrosteredAsync(
        FantasyWarriorDbContext db, DraftContext ctx, CancellationToken ct)
    {
        var taken = ctx.TakenPlayerIds;

        var rostered = await db.RosterSpots
            .Where(s => s.LeagueId == ctx.League.LeagueId && s.PlayerId != null)
            .Where(RosterWindow.Committed())
            .Select(s => s.PlayerId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var free = await db.Players
            .AsNoTracking()
            .Where(p => !rostered.Contains(p.PlayerId))
            .Select(p => new
            {
                p.PlayerId,
                p.FirstName,
                p.LastName,
                p.Position,
                p.PositionGroup,
                p.CareerNhlGames,
                NhlTeam = p.TeamAbbrev,
            })
            .ToListAsync(ct);

        return free
            .Select(p => new DraftPoolRow(
                Candidate: new DraftCandidate(
                    p.PlayerId, p.PositionGroup, p.CareerNhlGames, null, false, 0,
                    taken.Contains(p.PlayerId)),
                ShortName: DraftFormat.ShortName(p.FirstName, p.LastName),
                Position: p.Position,
                NhlTeam: p.NhlTeam,
                OwnerTeamName: null,
                OwnerUsername: null,
                CapHit: null))
            .ToList();
    }
}

/// <summary>
/// One row of the available list: the rule's view of the player plus what the
/// screen renders. Kept together so the pool is filtered and displayed from one
/// object rather than two lists that have to be zipped back up.
/// </summary>
public sealed record DraftPoolRow(
    DraftCandidate Candidate,
    string ShortName,
    string Position,
    string? NhlTeam,
    string? OwnerTeamName,
    string? OwnerUsername,
    long? CapHit);
