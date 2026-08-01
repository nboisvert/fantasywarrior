using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FantasyWarrior.Jobs.CapWages;

/// <summary>One player's contract terms for one season, as CapWages reports them.</summary>
public sealed record CapWagesSeason(
    string Season,
    long CapHit,
    long? Aav,
    long? BaseSalary,
    long? SigningBonuses,
    long? PerformanceBonuses,
    long? TotalSalary,
    string? Clause);

/// <summary>A player found on a CapWages page, with every contract season we could read.</summary>
public sealed record CapWagesPlayer(
    string Slug,
    string Name,
    string? TeamTricode,
    string? Position,
    long? NhlId,
    IReadOnlyList<CapWagesSeason> Seasons);

/// <summary>
/// Reads CapWages pages.
///
/// **Not HTML scraping.** CapWages is a Next.js site and every page embeds the
/// data the React tree was rendered from in a
/// <c>&lt;script id="__NEXT_DATA__"&gt;</c> block — the same numbers as the
/// visible tables, already structured, with names and types. Parsing that JSON
/// instead of the rendered <c>&lt;table&gt;</c> elements means a CSS or layout
/// change cannot break the import, which is the usual way a scraper dies.
///
/// The player page additionally carries <c>nhlId</c>, which is what lets a
/// contract join straight onto our <c>Players</c> table with no name matching
/// at all. Team pages do not, so they fall back to matching on name + team —
/// safe in practice, since two players with the same name on the same roster
/// does not happen.
///
/// Everything here is pure: it takes HTML text and returns records. The HTTP
/// and the database live elsewhere, so the parsing is unit-testable against a
/// saved page.
/// </summary>
public static class CapWagesParser
{
    private static readonly Regex NextDataBlock = new(
        """<script id="__NEXT_DATA__" type="application/json">(.*?)</script>""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// The embedded JSON document, or null when the page has none — which is
    /// how a 404 page, a redirect, or a site rewrite all present themselves.
    /// </summary>
    public static JsonDocument? ExtractNextData(string html)
    {
        var match = NextDataBlock.Match(html);
        if (!match.Success) return null;
        try
        {
            return JsonDocument.Parse(match.Groups[1].Value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// "$8,700,000" as 8700000. Null for a blank, a dash, or anything that is
    /// not a number — a missing salary must read as "unknown", never as zero,
    /// or a roster's cap total would quietly under-report.
    /// </summary>
    public static long? ParseMoney(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = raw.Replace("$", "").Replace(",", "").Replace(" ", "").Trim();
        if (digits.Length == 0 || digits == "-") return null;
        return long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// "2025-26" as "20252026", the season key used everywhere else in this app.
    ///
    /// The end year is computed from the start rather than read from the two
    /// trailing digits, so the 1999-00 rollover cannot produce "19990000".
    /// </summary>
    public static string? ParseSeason(string? raw)
    {
        if (raw is null || raw.Length < 4) return null;
        if (!int.TryParse(raw.AsSpan(0, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
            return null;
        if (start is < 1917 or > 2100) return null;
        return $"{start}{start + 1}";
    }

    /// <summary>
    /// Every player on a team page, with the contract seasons CapWages lists
    /// for them.
    ///
    /// Reads <c>data.roster</c> and <c>data["non-roster"]</c>. Deliberately
    /// skips <c>reserves</c>: those are drafted players who have not signed,
    /// so they carry no contract and would only add unmatched-name noise.
    /// </summary>
    public static IReadOnlyList<CapWagesPlayer> ParseTeamPage(string html)
    {
        using var doc = ExtractNextData(html);
        if (doc is null) return [];
        if (!TryPath(doc.RootElement, out var pageProps, "props", "pageProps")) return [];
        if (!pageProps.TryGetProperty("data", out var data)) return [];

        var players = new List<CapWagesPlayer>();
        foreach (var groupName in (string[])["roster", "non-roster"])
        {
            if (!data.TryGetProperty(groupName, out var group)) continue;
            foreach (var positionGroup in (string[])["forwards", "defense", "goalies"])
            {
                if (!group.TryGetProperty(positionGroup, out var list)
                    || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var entry in list.EnumerateArray())
                    if (ReadPlayer(entry) is { } player)
                        players.Add(player);
            }
        }
        return players;
    }

    /// <summary>
    /// The single player on a player page. This is the only source of
    /// <c>nhlId</c>, and it carries the player's full contract history rather
    /// than just the current and next deals.
    /// </summary>
    public static CapWagesPlayer? ParsePlayerPage(string html)
    {
        using var doc = ExtractNextData(html);
        if (doc is null) return null;
        return TryPath(doc.RootElement, out var player, "props", "pageProps", "player")
            ? ReadPlayer(player)
            : null;
    }

    private static CapWagesPlayer? ReadPlayer(JsonElement entry)
    {
        var slug = Str(entry, "slug");
        if (string.IsNullOrWhiteSpace(slug)) return null;

        var seasons = new List<CapWagesSeason>();
        if (entry.TryGetProperty("contracts", out var contracts) && contracts.ValueKind == JsonValueKind.Array)
            foreach (var contract in contracts.EnumerateArray())
            {
                if (!contract.TryGetProperty("details", out var details)
                    || details.ValueKind != JsonValueKind.Array) continue;
                foreach (var row in details.EnumerateArray())
                {
                    var season = ParseSeason(Str(row, "season"));
                    var capHit = ParseMoney(Str(row, "capHit"));
                    // A row without a season or a cap hit says nothing about
                    // what a player costs, and writing it would create a
                    // contract record that is worse than none at all.
                    if (season is null || capHit is null) continue;
                    seasons.Add(new CapWagesSeason(
                        Season: season,
                        CapHit: capHit.Value,
                        Aav: ParseMoney(Str(row, "aav")),
                        BaseSalary: ParseMoney(Str(row, "baseSalary")),
                        SigningBonuses: ParseMoney(Str(row, "signingBonuses")),
                        PerformanceBonuses: ParseMoney(Str(row, "performanceBonuses")),
                        TotalSalary: ParseMoney(Str(row, "totalSalary")),
                        Clause: NullIfBlank(Str(row, "clause"))));
                }
            }

        // The same season can appear twice when an extension overlaps the deal
        // it replaces. The later-listed row wins: CapWages orders contracts
        // newest first, so the first occurrence is the one in force.
        var deduped = seasons
            .GroupBy(s => s.Season)
            .Select(g => g.First())
            .OrderBy(s => s.Season)
            .ToList();

        return new CapWagesPlayer(
            Slug: slug!,
            Name: Str(entry, "name") ?? slug!,
            TeamTricode: NullIfBlank(Str(entry, "currentTeamTricode")),
            Position: NullIfBlank(Str(entry, "officialPosition")) ?? NullIfBlank(Str(entry, "pos")),
            NhlId: Long(entry, "nhlId"),
            Seasons: deduped);
    }

    /// <summary>
    /// CapWages writes names as "Crosby, Sidney"; the NHL API and our own table
    /// use "Sidney Crosby". Anything without a comma is passed through, so a
    /// single-word or already-natural name is not mangled.
    /// </summary>
    public static string ToNaturalName(string name)
    {
        var comma = name.IndexOf(',');
        if (comma < 0) return name.Trim();
        var last = name[..comma].Trim();
        var first = name[(comma + 1)..].Trim();
        return first.Length == 0 ? last : $"{first} {last}";
    }

    private static bool TryPath(JsonElement root, out JsonElement result, params string[] path)
    {
        result = root;
        foreach (var segment in path)
        {
            if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty(segment, out var next))
            {
                result = default;
                return false;
            }
            result = next;
        }
        return true;
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Accepts a number or a numeric string — CapWages uses both for nhlId.</summary>
    private static long? Long(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(value.GetString(), out var n) => n,
            _ => null,
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
