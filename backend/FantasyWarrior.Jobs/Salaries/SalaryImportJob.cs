
using System.Text;
using FantasyWarrior.Core.Players;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Jobs.Salaries;

/// <summary>
/// Imports player cap hits from a CSV file into the `players` collection.
/// Source-agnostic: works with any export as long as the columns are present.
///
/// Expected header (case-insensitive, extra columns ignored):
///   nhlId,firstName,lastName,teamAbbrev,capHit
/// - nhlId is optional; when absent (or 0) the row is matched by normalized
///   name, using teamAbbrev to break ties.
/// - capHit accepts plain integers ("9250000") or formatted ("$9,250,000").
/// </summary>
public sealed class SalaryImportJob(FirestoreDb db)
{
    public async Task<int> RunAsync(string csvPath, CancellationToken ct = default)
    {
        var rows = ParseCsv(csvPath);
        Console.WriteLine($"SalaryImport: {rows.Count} rows in {Path.GetFileName(csvPath)}");

        var players = db.Collection("players");
        var snapshot = await players.GetSnapshotAsync(ct);
        var byId = new Dictionary<long, DocumentSnapshot>();
        var byName = new Dictionary<string, List<DocumentSnapshot>>();
        foreach (var doc in snapshot.Documents)
        {
            var p = doc.ConvertTo<Player>();
            byId[p.NhlId] = doc;
            var key = NameNormalizer.Normalize($"{p.FirstName} {p.LastName}");
            (byName.TryGetValue(key, out var list) ? list : byName[key] = []).Add(doc);
        }

        var updated = 0;
        var unmatched = new List<string>();
        var batch = db.StartBatch();
        var batchCount = 0;

        foreach (var row in rows)
        {
            var doc = Match(row, byId, byName);
            if (doc is null)
            {
                unmatched.Add($"{row.FirstName} {row.LastName} ({row.TeamAbbrev})");
                continue;
            }

            batch.Update(doc.Reference, new Dictionary<string, object?>
            {
                ["capHit"] = row.CapHit,
                ["capHitUpdatedUtc"] = Timestamp.GetCurrentTimestamp(),
            });
            updated++;
            if (++batchCount == 500)
            {
                await batch.CommitAsync(ct);
                batch = db.StartBatch();
                batchCount = 0;
            }
        }
        if (batchCount > 0)
            await batch.CommitAsync(ct);

        Console.WriteLine($"SalaryImport: updated {updated}, unmatched {unmatched.Count}");
        foreach (var name in unmatched)
            Console.WriteLine($"  unmatched: {name}");
        return updated;
    }

    private static DocumentSnapshot? Match(
        SalaryRow row,
        Dictionary<long, DocumentSnapshot> byId,
        Dictionary<string, List<DocumentSnapshot>> byName)
    {
        if (row.NhlId > 0 && byId.TryGetValue(row.NhlId, out var byIdDoc))
            return byIdDoc;

        if (!byName.TryGetValue(NameNormalizer.Normalize($"{row.FirstName} {row.LastName}"), out var candidates))
            return null;
        if (candidates.Count == 1)
            return candidates[0];

        // Same name on several players: only the team can disambiguate.
        var byTeam = candidates
            .Where(d => string.Equals(d.GetValue<string>("teamAbbrev"), row.TeamAbbrev, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byTeam.Count == 1 ? byTeam[0] : null;
    }

    private static List<SalaryRow> ParseCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
            throw new InvalidDataException("CSV needs a header line and at least one data row.");

        var headers = SplitCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        int Col(string name) => Array.IndexOf(headers, name.ToLowerInvariant());
        var idCol = Col("nhlid");
        var firstCol = Col("firstname");
        var lastCol = Col("lastname");
        var teamCol = Col("teamabbrev");
        var capCol = Col("caphit");
        if (capCol < 0 || (idCol < 0 && (firstCol < 0 || lastCol < 0)))
            throw new InvalidDataException("CSV must contain capHit plus nhlId or firstName+lastName columns.");

        var rows = new List<SalaryRow>();
        foreach (var line in lines.Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var fields = SplitCsvLine(line);
            string Get(int col) => col >= 0 && col < fields.Count ? fields[col].Trim() : "";
            rows.Add(new SalaryRow(
                NhlId: long.TryParse(Get(idCol), out var id) ? id : 0,
                FirstName: Get(firstCol),
                LastName: Get(lastCol),
                TeamAbbrev: Get(teamCol),
                CapHit: ParseMoney(Get(capCol))));
        }
        return rows;
    }

    private static long ParseMoney(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? long.Parse(digits) : 0;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private sealed record SalaryRow(long NhlId, string FirstName, string LastName, string TeamAbbrev, long CapHit);
}
