namespace FantasyWarrior.Core.Players;

/// <summary>One hit from the NHL player-search endpoint. It publishes a single
/// <c>name</c> field rather than a first/last pair, which is why the split
/// happens here.</summary>
public readonly record struct PlayerSearchCandidate(
    long PlayerId,
    string Name,
    string? PositionCode,
    string? TeamAbbrev,
    bool Active);

/// <summary>
/// Picks the one candidate a wanted name refers to, out of what the NHL search
/// endpoint returned — or nothing.
///
/// **Search by surname, never by the whole name.** The endpoint falls back to a
/// fuzzy first-name match when the exact player escapes it, and it does so
/// silently: <c>q=Zack Bolduc</c> answers Zack *Smith*, <c>q=Sam Montembeault</c>
/// answers Sam *Morton*, <c>q=Cole Reschny</c> answers Cole *Brady*. Taking the
/// first hit would have written three strangers onto pool rosters. Querying the
/// surname alone returns the real man every time; the disambiguation then
/// happens here, on data we can see.
///
/// **Why not <see cref="PlayerNameIndex"/>.** It solves the neighbouring
/// problem — a news site writing "Jaden Schwartz" for a row we store as
/// "J. Schwartz" — and its first-initial fallback is right there, because the
/// source is abbreviating a player we already hold. Here the question is the
/// opposite: does this name refer to anyone at all? Under that fallback
/// "Marcel Bolduc" resolves to *Mathieu* Bolduc, the only M. Bolduc among
/// seven namesakes. Confidently, and wrongly.
///
/// So the given name has to agree on more than its first letter, without
/// demanding it be identical — the real list needs Zack for Zachary, Sam for
/// Samuel, Dmitriy for Dmitri. A shared prefix of three characters separates
/// those (zac, sam, dmitri) from Marcel against Mathieu (ma). Nicknames that
/// share no prefix — Bill for William — are reported as unresolved rather than
/// guessed, which is the direction this job is allowed to fail in.
///
/// Surnames must match exactly once normalized, with one adjustment:
/// <see cref="NameNormalizer"/> deletes punctuation without leaving a space
/// ("Jean-Gabriel" -> "jeangabriel"), which is deliberate and asserted in its
/// own tests, but would keep Sandin-Pellikka from ever meeting the
/// Sandin Pellikka the Mordus PDF wrote. Hyphens become spaces here, on both
/// sides, before normalizing.
/// </summary>
public static class PlayerSearchMatcher
{
    /// <summary>Given names shorter than this can never match a different
    /// spelling — three characters is what separates zac/zac from ma/ma.</summary>
    private const int MinSharedPrefix = 3;

    /// <summary>What to send to the search endpoint for this name — the surname
    /// only. Everything after the first space, so compound surnames survive
    /// whole ("Axel Sandin Pellikka" -> "Sandin Pellikka").</summary>
    public static string QueryFor(string fullName)
    {
        var trimmed = (fullName ?? "").Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[(space + 1)..].Trim();
    }

    /// <summary>The PlayerId <paramref name="wantedName"/> refers to, or null
    /// when nobody matches or several do.</summary>
    public static long? Resolve(string wantedName, IEnumerable<PlayerSearchCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(wantedName)) return null;

        var (wantedFirst, wantedLast) = NormalizedParts(wantedName);
        if (wantedLast.Length == 0) return null;

        var parsed = candidates
            .Select(c => (c.PlayerId, Parts: NormalizedParts(c.Name)))
            .Where(c => c.Parts.Last.Length > 0)
            .ToList();

        // The whole name, spelled the same way on both sides.
        var exact = Unique(parsed.Where(c =>
            c.Parts.First == wantedFirst && c.Parts.Last == wantedLast));
        if (exact is not null) return exact;

        // Same surname, and a given name close enough to be the same man.
        return Unique(parsed.Where(c =>
            c.Parts.Last == wantedLast && GivenNamesAgree(c.Parts.First, wantedFirst)));
    }

    /// <summary>First token is the given name, the remainder the surname —
    /// the convention the NHL's own <c>name</c> field follows.</summary>
    public static (string First, string Last) SplitName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? (trimmed, "") : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }

    /// <summary>The one id in the sequence, or null if it holds none or several.</summary>
    private static long? Unique(IEnumerable<(long PlayerId, (string First, string Last) Parts)> matches)
    {
        var ids = matches.Select(m => m.PlayerId).Distinct().Take(2).ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>Zack/Zachary yes, Sam/Samuel yes, Marcel/Mathieu no.</summary>
    private static bool GivenNamesAgree(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;

        var shared = 0;
        while (shared < a.Length && shared < b.Length && a[shared] == b[shared]) shared++;
        return shared >= MinSharedPrefix;
    }

    /// <summary>Hyphens to spaces first — see the class remarks.</summary>
    private static (string First, string Last) NormalizedParts(string? name)
    {
        var (first, last) = SplitName((name ?? "").Replace('-', ' '));
        return (NameNormalizer.Normalize(first), NameNormalizer.Normalize(last));
    }
}
