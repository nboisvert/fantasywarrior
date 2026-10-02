namespace FantasyWarrior.Core.Players;

/// <summary>A player as the resolver needs to see him.</summary>
public sealed record RosterPlayer(long PlayerId, string FirstName, string LastName, string Position, string? TeamAbbrev);

/// <summary>
/// One name off a pool's roster export. <see cref="Team"/> is the export's own
/// team hint (PoolExpert writes TB, NJ, WIN…); <see cref="PlayerId"/>, when
/// present, skips resolution outright.
/// </summary>
public sealed record RosterName(string First, string Last, string? Team = null, long? PlayerId = null);

/// <summary>The player a name resolved to (null: not found or ambiguous), and
/// how, when it took more than an exact match.</summary>
public readonly record struct NameResolution(long? PlayerId, string? Note);

/// <summary>
/// Resolves names written by an outside pool tool to our players — and never
/// guesses among several real candidates.
///
/// The ladder: exact match (a shared full name is split by the team hint) →
/// Nom/Prénom swapped → a close first name on an exact last name → a last name
/// within two edits on a close first name. Every step past the first reports
/// what it did, so a spelling correction is visible rather than silent.
/// </summary>
public sealed class RosterNameResolver
{
    private readonly List<(RosterPlayer Player, string First, string Last)> _players;
    private readonly HashSet<long> _ids;

    public RosterNameResolver(IEnumerable<RosterPlayer> players)
    {
        _players = players.Select(p => (p, Key(p.FirstName), Key(p.LastName))).ToList();
        _ids = _players.Select(p => p.Player.PlayerId).ToHashSet();
    }

    public NameResolution Resolve(RosterName name)
    {
        if (name.PlayerId is { } explicitId)
            return new(_ids.Contains(explicitId) ? explicitId : null, null);

        var wantFirst = Key(name.First);
        var wantLast = Key(name.Last);

        var exact = _players.Where(p => p.Last == wantLast && p.First == wantFirst).Select(p => p.Player).ToList();
        if (exact.Count == 1) return new(exact[0].PlayerId, null);
        if (exact.Count > 1)
        {
            var byTeam = FilterByTeamHint(exact, name.Team);
            return byTeam.Count == 1
                ? new(byTeam[0].PlayerId, $"{name.First} {name.Last} ({name.Team}) -> disambiguated by NHL team")
                : new(null, null);
        }

        var swapped = _players.Where(p => p.Last == wantFirst && p.First == wantLast).Select(p => p.Player).ToList();
        if (swapped.Count == 1)
            return new(swapped[0].PlayerId,
                $"{name.First} {name.Last} -> {swapped[0].FirstName} {swapped[0].LastName} (Nom/Prénom swapped)");

        var sameLast = _players.Where(p => p.Last == wantLast).ToList();
        if (sameLast.Count > 0)
        {
            var byFirst = sameLast
                .Where(p => IsCloseFirstName(wantFirst, p.First))
                .Select(p => (p.Player, Distance: Levenshtein(wantFirst, p.First)))
                .OrderBy(x => x.Distance)
                .ToList();
            return Unique(byFirst, name);
        }

        var close = _players
            .Where(p => IsCloseFirstName(wantFirst, p.First))
            .Select(p => (p.Player, Distance: Levenshtein(wantLast, p.Last)))
            .Where(x => x.Distance <= 2)
            .OrderBy(x => x.Distance)
            .ToList();
        return Unique(close, name);
    }

    /// <summary>PoolExpert's team abbreviations, mapped onto the NHL's.</summary>
    public static string NhlAbbrev(string hint)
    {
        var upper = hint.Trim().ToUpperInvariant();
        return TeamHintAlias.GetValueOrDefault(upper, upper);
    }

    private static readonly Dictionary<string, string> TeamHintAlias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TB"] = "TBL", ["SJ"] = "SJS", ["LA"] = "LAK", ["NJ"] = "NJD", ["WIN"] = "WPG",
    };

    private static NameResolution Unique(List<(RosterPlayer Player, int Distance)> ranked, RosterName name) =>
        ranked.Count == 1 || (ranked.Count > 1 && ranked[0].Distance < ranked[1].Distance)
            ? new(ranked[0].Player.PlayerId,
                $"{name.First} {name.Last} -> {ranked[0].Player.FirstName} {ranked[0].Player.LastName}")
            : new(null, null);

    // The team hint only ever splits a tie between real players; it never
    // accepts or rejects a match on its own.
    private static List<RosterPlayer> FilterByTeamHint(List<RosterPlayer> candidates, string? hint)
    {
        if (hint is null) return candidates;
        var abbrev = NhlAbbrev(hint);
        var byTeam = candidates.Where(p => p.TeamAbbrev == abbrev).ToList();
        return byTeam.Count > 0 ? byTeam : candidates;
    }

    private static bool IsCloseFirstName(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (Levenshtein(a, b) <= 2) return true;
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);
        if (shorter.Length <= 2) return longer.StartsWith(shorter, StringComparison.Ordinal);
        return longer.StartsWith(shorter, StringComparison.Ordinal) && longer.Length - shorter.Length <= 4;
    }

    private static string Key(string s) => NameNormalizer.Normalize(s).Replace(" ", "");

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        return d[a.Length, b.Length];
    }
}
