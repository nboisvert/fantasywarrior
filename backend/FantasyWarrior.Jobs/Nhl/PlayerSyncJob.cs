using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Nhl;

/// <summary>
/// Seeds/refreshes the `players` collection from the NHL API:
/// every team's season roster plus its prospects. Roster entries win over
/// prospect entries when a player appears in both.
/// </summary>
public sealed class PlayerSyncJob(NhlApiClient nhl, FirestoreDb db)
{
    private const int FirestoreBatchLimit = 500;

    // Fields owned by this job. capHit is deliberately absent: it comes from the
    // salary import and must survive roster syncs.
    private static readonly SetOptions MergeSyncedFields = SetOptions.MergeFields(
        "nhlId", "firstName", "lastName", "position", "teamAbbrev", "status",
        "sweaterNumber", "shootsCatches", "birthDate", "birthCountry",
        "heightCm", "weightKg", "headshotUrl", "lastSyncedUtc");

    public async Task<int> RunAsync(string season, CancellationToken ct = default)
    {
        var teams = await nhl.GetTeamAbbrevsAsync(ct);
        Console.WriteLine($"PlayerSync: {teams.Count} teams, season {season}");

        var players = new Dictionary<long, Player>();
        foreach (var team in teams)
        {
            // Prospects first so a roster entry overwrites a prospect entry.
            foreach (var dto in await nhl.GetProspectsAsync(team, ct))
                players[dto.Id] = ToPlayer(dto, team, status: "prospect");

            var roster = await nhl.GetRosterAsync(team, season, ct);
            if (roster.Count == 0)
            {
                // Offseason: next season's roster may not be published yet.
                var previous = PreviousSeason(season);
                roster = await nhl.GetRosterAsync(team, previous, ct);
                if (roster.Count > 0)
                    Console.WriteLine($"  {team}: season {season} not published, using {previous}");
            }
            foreach (var dto in roster)
                players[dto.Id] = ToPlayer(dto, team, status: "nhl");

            Console.WriteLine($"  {team}: roster {roster.Count}, total so far {players.Count}");
        }

        var collection = db.Collection("players");
        foreach (var chunk in players.Values.Chunk(FirestoreBatchLimit))
        {
            var batch = db.StartBatch();
            foreach (var player in chunk)
                batch.Set(collection.Document(player.NhlId.ToString()), player, MergeSyncedFields);
            await batch.CommitAsync(ct);
        }

        Console.WriteLine($"PlayerSync: upserted {players.Count} players");
        return players.Count;
    }

    private static string PreviousSeason(string season)
    {
        var startYear = int.Parse(season[..4]) - 1;
        return $"{startYear}{startYear + 1}";
    }

    private static Player ToPlayer(NhlPlayerDto dto, string teamAbbrev, string status) => new()
    {
        NhlId = dto.Id,
        FirstName = dto.FirstName?.Default ?? "",
        LastName = dto.LastName?.Default ?? "",
        Position = dto.PositionCode ?? "",
        TeamAbbrev = teamAbbrev,
        Status = status,
        SweaterNumber = dto.SweaterNumber,
        ShootsCatches = dto.ShootsCatches,
        BirthDate = dto.BirthDate,
        BirthCountry = dto.BirthCountry,
        HeightCm = dto.HeightInCentimeters,
        WeightKg = dto.WeightInKilograms,
        HeadshotUrl = dto.Headshot,
        LastSyncedUtc = Timestamp.GetCurrentTimestamp(),
    };
}
