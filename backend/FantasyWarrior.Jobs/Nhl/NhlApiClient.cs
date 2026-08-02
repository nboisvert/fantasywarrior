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

    /// <summary>Per-player detail page — only source for draft info (the
    /// team roster/prospect endpoints don't carry it), so this is one HTTP
    /// call per player, used by DraftSyncJob rather than the nightly sync.</summary>
    public async Task<PlayerLandingDto?> GetPlayerLandingAsync(long playerId, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api-web.nhle.com/v1/player/{playerId}/landing", ct);
        if (!response.IsSuccessStatusCode)
            return null;
        return JsonSerializer.Deserialize<PlayerLandingDto>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
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

public sealed class PlayerLandingDto
{
    public DraftDetailsDto? DraftDetails { get; set; }

    /// <summary>
    /// Every season/league/team stint of this player's career — junior,
    /// NCAA, European leagues, AHL, NHL, right back to childhood tournaments.
    /// Consumed by CareerStatsSyncJob; <see cref="FantasyWarrior.Core.Players.NotableLeagues"/>
    /// is what filters the noise out.
    /// </summary>
    public List<SeasonTotalDto> SeasonTotals { get; set; } = [];

    public sealed class DraftDetailsDto
    {
        public int? Year { get; set; }
        public int? Round { get; set; }
        public int? OverallPick { get; set; }
        public string? TeamAbbrev { get; set; }
    }

    public sealed class SeasonTotalDto
    {
        public int Season { get; set; }
        public int GameTypeId { get; set; }
        public string? LeagueAbbrev { get; set; }
        public TeamNameDto? TeamName { get; set; }
        public int GamesPlayed { get; set; }

        // --- skaters ---
        public int? Goals { get; set; }
        public int? Assists { get; set; }
        public int? Points { get; set; }
        public int? Pim { get; set; }
        public int? PlusMinus { get; set; }

        // --- goalies ---
        public int? Wins { get; set; }
        public int? Losses { get; set; }
        public int? OtLosses { get; set; }
        public int? GoalsAgainst { get; set; }
        public double? GoalsAgainstAvg { get; set; }
        public double? SavePctg { get; set; }
        public int? Shutouts { get; set; }

        public sealed class TeamNameDto
        {
            public string? Default { get; set; }
        }
    }
}
