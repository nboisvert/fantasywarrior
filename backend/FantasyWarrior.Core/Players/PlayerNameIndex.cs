namespace FantasyWarrior.Core.Players;

/// <summary>
/// Resolves a name written by an outside source ("Jaden Schwartz") to a
/// PlayerId, when our own table may hold it differently ("J. Schwartz").
///
/// The abbreviation is not a data error to clean up: player-sync stores a
/// player the way the NHL currently publishes him, and a veteran between
/// contracts is published as an initial and a surname. Radko Gudas, Viktor
/// Arvidsson, Nick Jensen and Jaden Schwartz were all "R. Gudas" shaped in
/// August 2026, and every one of them can sit on a pool roster. News about
/// them matched nothing, so they could never be marked injured — silently,
/// because an unmatched headline is stored with a null PlayerId rather than
/// failing.
///
/// So there are two keys, tried in order:
///
///   1. the whole normalized name — "jaden schwartz"
///   2. first initial plus surname — "j schwartz"
///
/// Both are built only from names that are *unique* under that key. This is
/// what makes the initial fallback safe rather than reckless: the league has
/// two Sebastian Ahos, so "s aho" is ambiguous and is simply not a key. A
/// wrong match would put another man's injury on a GM's roster, which is worse
/// than no match at all — the second key exists to widen the net, never to
/// guess.
/// </summary>
public sealed class PlayerNameIndex
{
    private readonly Dictionary<string, long> _byFullName;
    private readonly Dictionary<string, long> _byInitial;

    public PlayerNameIndex(IEnumerable<(long PlayerId, string FirstName, string LastName)> players)
    {
        var all = players.ToList();

        _byFullName = UniqueBy(all, p => NameNormalizer.Normalize($"{p.FirstName} {p.LastName}"));
        _byInitial = UniqueBy(all, p => InitialKey(p.FirstName, p.LastName));
    }

    /// <summary>The PlayerId this name refers to, or null when we cannot say —
    /// either nobody matches, or too many do.</summary>
    public long? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var full = NameNormalizer.Normalize(name);
        if (_byFullName.TryGetValue(full, out var exact)) return exact;

        // "jaden schwartz" -> "j schwartz". Also collapses the other direction:
        // a source writing "J. Schwartz" normalizes to the same key.
        var space = full.IndexOf(' ');
        if (space <= 0) return null;
        var key = $"{full[0]} {full[(space + 1)..]}";
        return _byInitial.TryGetValue(key, out var initial) ? initial : null;
    }

    /// <summary>"Jaden", "Schwartz" -> "j schwartz". Already-abbreviated first
    /// names land on the same key, since normalizing drops the period.</summary>
    private static string InitialKey(string firstName, string lastName)
    {
        var first = NameNormalizer.Normalize(firstName);
        var last = NameNormalizer.Normalize(lastName);
        return first.Length == 0 || last.Length == 0 ? "" : $"{first[0]} {last}";
    }

    /// <summary>Keys shared by more than one player are dropped, not
    /// arbitrated — see the class remarks.</summary>
    private static Dictionary<string, long> UniqueBy(
        List<(long PlayerId, string FirstName, string LastName)> players,
        Func<(long PlayerId, string FirstName, string LastName), string> key) =>
        players
            .Select(p => (Key: key(p), p.PlayerId))
            .Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key)
            .Where(g => g.Select(x => x.PlayerId).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().PlayerId);
}
