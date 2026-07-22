using System.Text.Json;

namespace FantasyWarrior.Jobs.Nhl;

/// <summary>
/// Thin client over the official NHL JSON APIs (api-web.nhle.com).
/// </summary>
public sealed class NhlApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // All 32 active franchises; used as fallback when the standings endpoint is unavailable.
    public static readonly string[] ActiveTeamAbbrevs =
    [
        "ANA", "BOS", "BUF", "CAR", "CBJ", "CGY", "CHI", "COL", "DAL", "DET", "EDM",
        "FLA", "LAK", "MIN", "MTL", "NJD", "NSH", "NYI", "NYR", "OTT", "PHI", "PIT",
        "SEA", "SJS", "STL", "TBL", "TOR", "UTA", "VAN", "VGK", "WPG", "WSH"
    ];

    public async Task<IReadOnlyList<string>> GetTeamAbbrevsAsync(CancellationToken ct = default)
    {
        try
        {
            using var doc = await GetJsonAsync("https://api-web.nhle.com/v1/standings/now", ct);
            var abbrevs = doc.RootElement.GetProperty("standings")
                .EnumerateArray()
                .Select(t => t.GetProperty("teamAbbrev").GetProperty("default").GetString()!)
                .Distinct()
                .ToList();
            return abbrevs.Count > 0 ? abbrevs : ActiveTeamAbbrevs;
        }
        catch (Exception)
        {
            return ActiveTeamAbbrevs;
        }
    }

    /// <summary>Roster players for a team season (e.g. "20252026"), or empty when not published.</summary>
    public async Task<IReadOnlyList<NhlPlayerDto>> GetRosterAsync(string teamAbbrev, string season, CancellationToken ct = default)
        => await GetPlayerGroupsAsync($"https://api-web.nhle.com/v1/roster/{teamAbbrev}/{season}", ct);

    /// <summary>Prospects for a team, or empty when unavailable.</summary>
    public async Task<IReadOnlyList<NhlPlayerDto>> GetProspectsAsync(string teamAbbrev, CancellationToken ct = default)
        => await GetPlayerGroupsAsync($"https://api-web.nhle.com/v1/prospects/{teamAbbrev}", ct);

    private async Task<IReadOnlyList<NhlPlayerDto>> GetPlayerGroupsAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return [];

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var players = new List<NhlPlayerDto>();
        foreach (var group in new[] { "forwards", "defensemen", "goalies" })
        {
            if (!doc.RootElement.TryGetProperty(group, out var arr))
                continue;
            players.AddRange(arr.EnumerateArray()
                .Select(p => p.Deserialize<NhlPlayerDto>(JsonOptions))
                .Where(p => p is not null)!);
        }
        return players;
    }

    /// <summary>All games (any state) for a calendar date.</summary>
    public async Task<IReadOnlyList<ScoreGameDto>> GetDailyScoresAsync(string date, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api-web.nhle.com/v1/score/{date}", ct);
        if (!response.IsSuccessStatusCode)
            return [];
        var dto = JsonSerializer.Deserialize<DailyScoresDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
        return dto?.Games ?? [];
    }

    public async Task<BoxscoreDto?> GetBoxscoreAsync(long gameId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api-web.nhle.com/v1/gamecenter/{gameId}/boxscore", ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return JsonSerializer.Deserialize<BoxscoreDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}

public sealed class NhlPlayerDto
{
    public long Id { get; set; }
    public LocalizedName? FirstName { get; set; }
    public LocalizedName? LastName { get; set; }
    public string? PositionCode { get; set; }
    public int? SweaterNumber { get; set; }
    public string? ShootsCatches { get; set; }
    public string? BirthDate { get; set; }
    public string? BirthCountry { get; set; }
    public int? HeightInCentimeters { get; set; }
    public int? WeightInKilograms { get; set; }
    public string? Headshot { get; set; }

    public sealed class LocalizedName
    {
        public string? Default { get; set; }
    }
}
