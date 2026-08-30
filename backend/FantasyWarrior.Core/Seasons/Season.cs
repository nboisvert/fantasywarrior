namespace FantasyWarrior.Core.Seasons;

/// <summary>
/// The NHL season identifier — <c>"20262027"</c> — treated as a value, not a
/// row. It is the NHL's own identifier, the same argument that keeps
/// <c>Player.PlayerId</c> the NHL's id rather than an identity column: stable,
/// globally unique, and already present in every payload this app ingests. A
/// <c>Seasons</c> table would carry no attribute the string lacks, would put a
/// foreign key on tens of thousands of <c>PlayerGameStats</c> rows for nothing,
/// and the one thing it would ever be asked — succession — is a pure function.
/// See <c>offseason.md</c>.
///
/// What was actually missing was this class. The string surgery it replaces
/// used to live in four places that could each get it wrong on their own:
/// <c>Jobs/Program.cs</c>'s <c>CurrentSeason()</c>, the draft-year calculation
/// one line below it, three hardcoded <c>"20252026"</c> defaults, and the
/// frontend's <c>formatSeason</c>. And the column itself is free text — nothing
/// stopped <c>"2025-2026"</c> from creating a phantom season.
/// </summary>
public static class Season
{
    /// <summary>Eight digits, and the second half is the first half plus one.</summary>
    public static bool IsValid(string season) =>
        season.Length == 8
        && int.TryParse(season[..4], out var start)
        && int.TryParse(season[4..], out var end)
        && end == start + 1;

    /// <summary><c>"20262027"</c> -&gt; 2026. Throws on a malformed season — callers are expected to have validated already.</summary>
    public static int StartYear(string season) =>
        IsValid(season) ? int.Parse(season[..4])
        : throw new ArgumentException($"\"{season}\" is not a valid NHL season string.", nameof(season));

    /// <summary><c>"20262027"</c> -&gt; 2027.</summary>
    public static int EndYear(string season) => StartYear(season) + 1;

    /// <summary>2026 -&gt; <c>"20262027"</c>.</summary>
    public static string FromStartYear(int startYear) => $"{startYear}{startYear + 1}";

    /// <summary><c>"20262027"</c> -&gt; <c>"20272028"</c>.</summary>
    public static string Next(string season) => FromStartYear(StartYear(season) + 1);

    /// <summary><c>"20262027"</c> -&gt; <c>"20252026"</c>.</summary>
    public static string Previous(string season) => FromStartYear(StartYear(season) - 1);

    /// <summary>
    /// Which NHL season a given date falls in, on the same September cutover
    /// <c>Jobs/Program.cs</c>'s <c>CurrentSeason()</c> used — the regular
    /// season starts in October, so any date from September onward already
    /// belongs to the season opening that fall.
    /// </summary>
    public static string CurrentOn(DateOnly today) =>
        FromStartYear(today.Month >= 9 ? today.Year : today.Year - 1);

    /// <summary><c>"20262027"</c> -&gt; <c>"2026-27"</c>, for display.</summary>
    public static string Display(string season)
    {
        if (!IsValid(season)) return season;
        return $"{season[..4]}-{season[6..]}";
    }
}
