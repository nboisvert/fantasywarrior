using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data;
using FantasyWarrior.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FantasyWarrior.Jobs.Sql;

/// <summary>
/// Resets Les Mordus' roster spots for a new season from its roster file
/// (<see cref="MordusRosterFile"/>, see .claude/doc/mordus.md), and reseeds
/// them fresh.
///
/// Unlike <see cref="SeedMordusJob"/> (which refuses if the league already
/// exists), this targets the live, existing "Les Mordus" — the league, its
/// Users, Teams, LeagueMembers, Trades and Messages all survive untouched.
/// Only <see cref="RosterSpot"/>s (and, by cascade, the
/// <see cref="RosterAssignment"/>s scored against them) are wiped and rebuilt:
/// the file is taken as the whole truth and everything open is replaced by
/// it, rather than reconciled spot by spot.
///
/// Requires the target season to already be declared (`season-init`,
/// `period-init`) and Les Mordus's <c>League.Season</c> to already equal it —
/// `season-phase --to InSeason` is the step that flips that, and this job
/// refuses to guess a roster's date onto a season nobody has opened yet.
/// </summary>
public sealed class ResetMordusRostersJob(FantasyWarriorDbContext db)
{
    private const string LeagueName = "Les Mordus";

    public async Task<int> RunAsync(string file, bool dryRun, CancellationToken ct = default)
    {
        if (await MordusRosterFile.LoadAsync(file, ct) is not { } data) return 1;

        Console.WriteLine($"=== reset-mordus-rosters{(dryRun ? "  [DRY RUN]" : "")} ===");
        Console.WriteLine($"{data.Teams.Count} teams, "
            + $"{data.Teams.Sum(t => t.Active.Count + t.Reserve.Count)} roster entries, season {data.Season}.\n");

        var league = await db.Leagues.FirstOrDefaultAsync(l => l.Name == LeagueName, ct);
        if (league is null)
        {
            Console.Error.WriteLine($"No league named \"{LeagueName}\".");
            return 1;
        }
        if (league.Season != data.Season)
        {
            Console.Error.WriteLine($"{LeagueName}.Season is {league.Season}, not {data.Season}. "
                + $"Run season-phase --league {league.JoinCode} --to InSeason first (stepping through the "
                + "phases in between) so the league is really open for this season before its rosters are reset.");
            return 1;
        }

        var firstPeriod = await db.Periods
            .Where(p => p.Season == data.Season)
            .OrderBy(p => p.Number)
            .FirstOrDefaultAsync(ct);
        if (firstPeriod is null)
        {
            Console.Error.WriteLine($"No period calendar for {data.Season}. Run season-init and period-init first.");
            return 1;
        }
        Console.WriteLine($"Roster spots will open on {firstPeriod.StartDate:yyyy-MM-dd} (week 1 start).\n");

        if (await data.ResolveAsync(db, ct) is not { } resolved) return 1;
        var positions = await db.Players.AsNoTracking()
            .Where(p => resolved.Values.Contains(p.PlayerId))
            .ToDictionaryAsync(p => p.PlayerId, p => p.Position, ct);

        var leagueId = league.LeagueId;
        var existingSpots = await db.RosterSpots.CountAsync(s => s.LeagueId == leagueId, ct);
        Console.WriteLine($"Existing roster spots to replace: {existingSpots}\n");

        if (dryRun)
        {
            Console.WriteLine($"[DRY RUN] Would wipe {existingSpots} roster spots and create "
                + $"{data.Teams.Sum(t => 1 + t.Active.Count + t.Reserve.Count)} (players + one franchise slot "
                + "per team). Nothing written.");
            return 0;
        }

        var now = DateTime.UtcNow;

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // RosterAssignments cascade off RosterSpots, but deleted explicitly
            // first anyway — same defensive ordering as WipePoolsJob, so a
            // provider that does not honor the cascade fails loudly here rather
            // than leaving orphaned scoring rows.
            await db.RosterAssignments.Where(ra => ra.RosterSpot!.LeagueId == leagueId).ExecuteDeleteAsync(ct);
            await db.RosterSpots.Where(s => s.LeagueId == leagueId).ExecuteDeleteAsync(ct);

            var spotCount = 0;
            foreach (var entry in data.Teams)
            {
                var owner = await db.Users.FirstAsync(u => u.Username == entry.Username, ct);
                var team = await db.Teams
                    .FirstOrDefaultAsync(t => t.LeagueId == leagueId && t.OwnerUserId == owner.UserId, ct);
                if (team is null)
                {
                    team = new Team
                    {
                        LeagueId = leagueId, OwnerUserId = owner.UserId, Name = entry.Franchise, CreatedUtc = now,
                    };
                    db.Teams.Add(team);
                    db.LeagueMembers.Add(new LeagueMember { LeagueId = leagueId, UserId = owner.UserId, JoinedUtc = now });
                }
                team.Name = entry.Franchise;
                team.FranchiseAbbrev = entry.FranchiseAbbrev;
                await db.SaveChangesAsync(ct);

                // The Équipe slot — one NHL franchise per GM.
                db.RosterSpots.Add(new RosterSpot
                {
                    LeagueId = leagueId,
                    TeamId = team.TeamId,
                    FranchiseAbbrev = entry.FranchiseAbbrev,
                    PositionGroup = "T",
                    StartDate = firstPeriod.StartDate,
                    StartReason = RosterSpotStartReason.Draft,
                    OpenedUtc = now,
                });

                var activeSpots = new List<RosterSpot>();
                var allSpots = new List<RosterSpot>();
                foreach (var player in entry.Active.Concat(entry.Reserve))
                {
                    var playerId = resolved[player];
                    var spot = new RosterSpot
                    {
                        LeagueId = leagueId,
                        TeamId = team.TeamId,
                        PlayerId = playerId,
                        PositionGroup = PositionGroups.CodeFrom(positions.GetValueOrDefault(playerId, "C")),
                        StartDate = firstPeriod.StartDate,
                        StartReason = RosterSpotStartReason.Draft,
                        OpenedUtc = now,
                    };
                    db.RosterSpots.Add(spot);
                    allSpots.Add(spot);
                    if (entry.Active.Contains(player)) activeSpots.Add(spot);
                    spotCount++;
                }
                await db.SaveChangesAsync(ct);

                // The week-1 lineup, as the file shows it — without this the
                // scoring pass finds no lineup and auto-fills with each team's
                // best available players.
                foreach (var spot in allSpots)
                    db.RosterAssignments.Add(new RosterAssignment
                    {
                        RosterSpotId = spot.RosterSpotId,
                        PeriodId = firstPeriod.PeriodId,
                        IsActive = activeSpots.Contains(spot),
                        EffectiveFrom = firstPeriod.StartDate,
                        EffectiveTo = firstPeriod.EndDate,
                        ScoredUtc = now,
                    });
                db.TeamPeriodLineups.Add(new TeamPeriodLineup
                {
                    TeamId = team.TeamId,
                    PeriodId = firstPeriod.PeriodId,
                    SetBy = entry.Username!,
                    SubmittedUtc = now,
                });
                await db.SaveChangesAsync(ct);

                Console.WriteLine($"  {entry.Username,-12} {entry.Franchise,-24} "
                    + $"{entry.Active.Count + entry.Reserve.Count,2} players  [{entry.FranchiseAbbrev}]");
            }

            await tx.CommitAsync(ct);
            Console.WriteLine($"\nReset \"{LeagueName}\" ({data.Season}) — {data.Teams.Count} teams, "
                + $"{spotCount} player spots + {data.Teams.Count} franchise spots.");
        });

        return 0;
    }
}
