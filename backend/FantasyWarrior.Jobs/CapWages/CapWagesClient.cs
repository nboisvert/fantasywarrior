namespace FantasyWarrior.Jobs.CapWages;

/// <summary>
/// Fetches CapWages pages, politely.
///
/// The data is free and public, but it belongs to a small site with no paid
/// contract behind this access, so the client is deliberately conservative:
/// one request at a time, a real delay between them, an honest User-Agent that
/// says what this is and who to contact, and a backoff on 429/503 rather than
/// a retry storm. Personal, non-commercial use only.
///
/// robots.txt (checked 2026-08-01) disallows <c>/players/</c> and
/// <c>/trade-tree/</c> for Amazonbot specifically and allows everything for
/// every other agent, so these paths are permitted for a client that
/// identifies itself honestly — which is exactly why the User-Agent must not
/// impersonate a browser.
/// </summary>
public sealed class CapWagesClient(HttpClient http, TimeSpan? delay = null)
{
    public const string BaseUrl = "https://capwages.com";

    private const string UserAgent =
        "FantasyWarrior/0.1 (personal hockey pool project; +https://github.com/nboisvert/fantasywarrior)";

    private readonly TimeSpan _delay = delay ?? TimeSpan.FromSeconds(2);
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public Task<string?> GetTeamPageAsync(string teamSlug, CancellationToken ct = default) =>
        GetAsync($"{BaseUrl}/teams/{teamSlug}", ct);

    public Task<string?> GetPlayerPageAsync(string playerSlug, CancellationToken ct = default) =>
        GetAsync($"{BaseUrl}/players/{playerSlug}", ct);

    /// <summary>
    /// The page's HTML, or null if it could not be fetched.
    ///
    /// Returns null rather than throwing, matching the convention the other
    /// external clients in this project follow: one unreachable page must not
    /// abort a sync of a thousand others. Every failure is logged with its
    /// status so a run that comes back empty says why, instead of silently
    /// reporting zero.
    /// </summary>
    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await ThrottleAsync(ct);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(UserAgent);
                using var response = await http.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    // Exponential, and generous: being rate-limited means we
                    // were already asking too fast.
                    var wait = TimeSpan.FromSeconds(5 * Math.Pow(2, attempt - 1));
                    Console.Error.WriteLine($"  ! {(int)response.StatusCode} on {url} — backing off {wait.TotalSeconds:0}s");
                    await Task.Delay(wait, ct);
                    continue;
                }

                Console.Error.WriteLine($"  ! {(int)response.StatusCode} {response.ReasonPhrase} on {url}");
                return null;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"  ! {url}: {ex.Message}");
                return null;
            }
        }
        Console.Error.WriteLine($"  ! {url}: gave up after 3 attempts");
        return null;
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - _lastRequest;
        if (since < _delay) await Task.Delay(_delay - since, ct);
        _lastRequest = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// The 32 team slugs, derived from our own NhlTeams table's abbreviations
    /// rather than hard-coded — CapWages uses lowercase, underscore-separated
    /// full names ("pittsburgh_penguins").
    /// </summary>
    public static string SlugFor(string teamName) =>
        teamName.ToLowerInvariant()
            .Replace(".", "")
            .Replace("é", "e")
            .Replace(" ", "_");
}
