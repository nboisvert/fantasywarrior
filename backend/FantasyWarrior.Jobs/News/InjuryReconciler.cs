namespace FantasyWarrior.Jobs.News;

/// <summary>An open PlayerInjuries row, as the reconciler needs to see it.</summary>
public sealed record OpenInjury(int PlayerInjuryId, long PlayerId, string Status, string? InjuryType);

/// <summary>A player the source lists as unavailable right now.</summary>
public sealed record ListedInjury(long PlayerId, string Status, string? InjuryType);

/// <summary>What news-sync must write to bring one source's open rows in line
/// with what that source now lists.</summary>
public sealed record InjuryPlan(
    IReadOnlyList<ListedInjury> ToOpen,
    IReadOnlyList<(int PlayerInjuryId, string? InjuryType)> ToRelabel,
    IReadOnlyList<int> ToResolve);

/// <summary>
/// Decides, for a single source, which injuries are new, which changed, and
/// which are over — by comparing the rows we hold open against the players
/// that source lists today.
///
/// Pure on purpose: this is the one piece of injury handling with rules rather
/// than plumbing, and it is the piece whose mistakes are invisible (a row left
/// open forever marks a healthy player hurt for the rest of the season, and
/// nothing errors). Per source, never across sources — Rotowire dropping a
/// player says nothing about whether FantasySP still lists him, and letting one
/// site's silence resolve the other's report is how a flag starts lying.
///
/// A relabel ("Knee" becoming "Lower Body") stays the same row, so ReportedUtc
/// keeps answering "hurt since when". A change of *status* does not: injured
/// and suspended are different events about the same man, so the first is
/// closed and the second opened.
/// </summary>
public static class InjuryReconciler
{
    public static InjuryPlan Plan(IReadOnlyList<OpenInjury> open, IReadOnlyList<ListedInjury> listed)
    {
        // A source can list the same player twice (two entries for one man on
        // one page); the first wins, and the rest are not a second injury.
        var listedByPlayer = listed
            .GroupBy(l => l.PlayerId)
            .ToDictionary(g => g.Key, g => g.First());
        var openByPlayer = open
            .GroupBy(o => o.PlayerId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var toOpen = new List<ListedInjury>();
        var toRelabel = new List<(int, string?)>();
        var toResolve = new List<int>();

        foreach (var (playerId, rows) in openByPlayer)
        {
            if (!listedByPlayer.TryGetValue(playerId, out var now))
            {
                // No longer listed: the source is saying he is cleared.
                toResolve.AddRange(rows.Select(r => r.PlayerInjuryId));
                continue;
            }

            // Duplicate open rows for one player are not something this job
            // creates, but if some earlier run left two, keep the first and
            // close the rest rather than carrying the mess forward.
            var keep = rows[0];
            toResolve.AddRange(rows.Skip(1).Select(r => r.PlayerInjuryId));

            if (keep.Status != now.Status)
            {
                toResolve.Add(keep.PlayerInjuryId);
                toOpen.Add(now);
            }
            else if (keep.InjuryType != now.InjuryType)
            {
                toRelabel.Add((keep.PlayerInjuryId, now.InjuryType));
            }
        }

        toOpen.AddRange(listedByPlayer.Values.Where(l => !openByPlayer.ContainsKey(l.PlayerId)));

        return new InjuryPlan(toOpen, toRelabel, toResolve);
    }
}
