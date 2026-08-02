namespace FantasyWarrior.Core.Players;

/// <summary>
/// Parsing/formatting for the NHL's own "MM:SS" time-on-ice strings — shared
/// by the stats sync (per-game) and the player card (season average), so the
/// two never drift on what counts as a valid value.
/// </summary>
public static class TimeOnIce
{
    /// <summary>
    /// "MM:SS" as seconds; 0 when empty or unparseable. Minutes can exceed 59
    /// in overtime, so this cannot be a TimeSpan parse.
    /// </summary>
    public static int ParseSeconds(string? toi)
    {
        if (string.IsNullOrWhiteSpace(toi)) return 0;
        var parts = toi.Split(':');
        return parts.Length == 2 && int.TryParse(parts[0], out var m) && int.TryParse(parts[1], out var s)
            ? m * 60 + s
            : 0;
    }

    /// <summary>"MM:SS", minutes unpadded, seconds zero-padded — matches the stored format.</summary>
    public static string Format(int totalSeconds)
    {
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}:{seconds:D2}";
    }

    /// <summary>
    /// Average TOI across a season's game lines, formatted back to "MM:SS".
    /// Null when nothing parseable was played — a card with no games yet
    /// should show "—", not a misleading "0:00".
    /// </summary>
    public static string? FormatAverage(IEnumerable<string?> lines)
    {
        var seconds = lines.Select(ParseSeconds).Where(s => s > 0).ToList();
        return seconds.Count == 0 ? null : Format((int)Math.Round(seconds.Average()));
    }
}
