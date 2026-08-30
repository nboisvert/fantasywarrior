using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Rosters;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Data.Leagues;

/// <summary>What a clone produced.</summary>
/// <param name="TeamIdBySourceTeamId">
/// Which new team stands for which old one. The caller needs it to carry
/// anything team-shaped across — the draft order, above all.
/// </param>
public sealed record LeagueCloneResult(
    League League,
    IReadOnlyDictionary<int, int> TeamIdBySourceTeamId,
    int PlayerSpots,
    int FranchiseSpots);

/// <summary>
/// Copies a league's <b>rules and rosters</b> into a brand new one, and nothing
/// else.
///
/// <b>What it deliberately leaves behind.</b> Not the weeks: no
/// <see cref="RosterAssignment"/>, no <see cref="TeamPeriodLineup"/>, no
/// <see cref="Trade"/>, no <see cref="LeagueSeason"/> history, no messages, no
/// cockcoins. A cloned league is a fresh pool holding the same men under the
/// same rules — it has never played a game, and its standings are honestly
/// empty rather than borrowed. That is what makes the copy cheap enough to
/// throw away, which is the whole point of having one.
///
/// <b>The users are shared, not copied.</b> A <see cref="User"/> is global; the
/// same GM simply owns a team in both leagues, exactly as multi-tenancy already
/// intends. Nothing here creates or renames an account.
///
/// <b>Only open spots come across</b> (<see cref="RosterWindow.Committed"/> —
/// the engaged roster, so a player promised away in an accepted trade lands
/// with his new team and not his old one). Every copied spot is re-stamped
/// <see cref="RosterSpotStartReason.Draft"/> with no trade or pick behind it:
/// the trade that opened the original was not copied, so pointing at it would
/// dangle.
/// </summary>
public static class LeagueClone
{
    public static async Task<LeagueCloneResult> CreateAsync(
        FantasyWarriorDbContext db, League source, string name, DateOnly today,
        bool everyOwnerJoins = true, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var league = new League
        {
            Name = name,
            // The same NHL season, because League.Season names the season whose
            // points count and the copy inherits the rules that price them. The
            // season being *prepared* is the caller's business — it lives on a
            // LeagueSeason row, not here (offseason.md).
            Season = source.Season,
            JoinCode = await UniqueJoinCodeAsync(db, ct),
            CommissionerUserId = source.CommissionerUserId,
            CapAmount = source.CapAmount,
            DefaultCapHit = source.DefaultCapHit,
            RosterMin = source.RosterMin,
            RosterMax = source.RosterMax,
            DraftRounds = source.DraftRounds,
            ProtectionSlots = source.ProtectionSlots,
            StealRounds = source.StealRounds,
            MaxLossesPerTeam = source.MaxLossesPerTeam,
            ActiveForwards = source.ActiveForwards,
            ActiveDefense = source.ActiveDefense,
            ActiveGoalies = source.ActiveGoalies,
            CreatedUtc = now,
        };
        db.Leagues.Add(league);
        await db.SaveChangesAsync(ct);

        var rules = await db.LeagueScoringRules
            .AsNoTracking()
            .Where(r => r.LeagueId == source.LeagueId)
            .ToListAsync(ct);
        foreach (var rule in rules)
            db.LeagueScoringRules.Add(new LeagueScoringRule
            {
                LeagueId = league.LeagueId,
                StatKey = rule.StatKey,
                PointValue = rule.PointValue,
            });

        var sourceTeams = await db.Teams
            .AsNoTracking()
            .Where(t => t.LeagueId == source.LeagueId)
            .OrderBy(t => t.TeamId)
            .ToListAsync(ct);

        var teamIdBySource = new Dictionary<int, int>(sourceTeams.Count);
        foreach (var sourceTeam in sourceTeams)
        {
            var team = new Team
            {
                LeagueId = league.LeagueId,
                OwnerUserId = sourceTeam.OwnerUserId,
                Name = sourceTeam.Name,
                FranchiseAbbrev = sourceTeam.FranchiseAbbrev,
                CreatedUtc = now,
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync(ct);
            teamIdBySource[sourceTeam.TeamId] = team.TeamId;

            // Membership is what puts a league in someone's list. Withholding it
            // gives the commissioner a copy nobody else can see — which is the
            // difference between a sandbox and an announcement.
            if (everyOwnerJoins || sourceTeam.OwnerUserId == source.CommissionerUserId)
                db.LeagueMembers.Add(new LeagueMember
                {
                    LeagueId = league.LeagueId,
                    UserId = sourceTeam.OwnerUserId,
                    JoinedUtc = now,
                });
        }
        await db.SaveChangesAsync(ct);

        var spots = await db.RosterSpots
            .AsNoTracking()
            .Where(s => s.LeagueId == source.LeagueId)
            .Where(RosterWindow.Committed())
            .OrderBy(s => s.RosterSpotId)
            .ToListAsync(ct);

        foreach (var spot in spots)
            db.RosterSpots.Add(new RosterSpot
            {
                LeagueId = league.LeagueId,
                TeamId = teamIdBySource[spot.TeamId],
                PlayerId = spot.PlayerId,
                FranchiseAbbrev = spot.FranchiseAbbrev,
                PositionGroup = spot.PositionGroup,
                // Never dated into the future. An engaged spot can start on a
                // Monday still to come — the incoming half of an accepted trade —
                // and the copy has no trade to land it, so it would sit invisible
                // to every "held today" query, cap included, with nothing ever
                // making it real.
                StartDate = spot.StartDate < today ? spot.StartDate : today,
                StartReason = RosterSpotStartReason.Draft,
                // Protections are worth one summer and are the next phase's
                // business; the copy starts everyone exposed.
                ProtectionStatus = RosterProtectionStatus.Unprotected,
                OpenedUtc = now,
            });

        await db.SaveChangesAsync(ct);

        return new LeagueCloneResult(
            league,
            teamIdBySource,
            PlayerSpots: spots.Count(s => s.PlayerId is not null),
            FranchiseSpots: spots.Count(s => s.PlayerId is null));
    }

    private static async Task<string> UniqueJoinCodeAsync(
        FantasyWarriorDbContext db, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var code = JoinCodes.New();
            if (!await db.Leagues.AnyAsync(l => l.JoinCode == code, ct)) return code;
        }
        throw new InvalidOperationException("Could not generate a unique join code in 10 attempts.");
    }
}
