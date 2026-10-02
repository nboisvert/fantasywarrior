using System.Text.RegularExpressions;
using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Imports;

/// <summary>One participant's roster as a PoolExpert standings report shows it.</summary>
public sealed record PoolExpertTeam(
    string Participant, string? FranchiseAbbrev, List<RosterName> Active, List<RosterName> Reserve);

public sealed record PoolExpertParse(List<PoolExpertTeam> Teams, List<string> Skipped);

/// <summary>
/// Reads PoolExpert's "Classement" report, as <c>pdftotext -raw</c> extracts it.
///
/// Each participant section opens on <c>N - NAME CITY</c>. Its player lines are
/// <c>[D|G|E] First Last TEAM stats…</c>: no prefix is a forward, <c>E</c> is the
/// participant's own NHL franchise. Lines above "JOUEURS DE RÉSERVE" are the
/// active lineup, the block below it the reserve, and the "HISTORIQUE
/// ACTIF/RÉSERVE" block that follows is past swaps — ignored.
///
/// <b>A player line must carry a PPP with a decimal point.</b> The PDF holds a
/// few garbled ghost copies of a real line (<c>D O P BUF … 2 00 …</c>, letters
/// and decimals lost) right next to the real one; that is what tells them
/// apart. They are reported in <see cref="PoolExpertParse.Skipped"/>, never
/// guessed at.
/// </summary>
public static partial class PoolExpertReport
{
    [GeneratedRegex(@"^\d+ - (?<participant>.+)$")]
    private static partial Regex SectionHeader();

    // Name runs lazily up to the first standalone 2-3 capital team code that is
    // followed by the stats (a number or a dash).
    [GeneratedRegex(@"^(?:(?<pos>[DGE]) )?(?<name>\S.*?) (?<team>[A-Z]{2,3}) (?<rest>(?:\d|-).*)$")]
    private static partial Regex PlayerLine();

    [GeneratedRegex(@"(?:^| )\d+\.\d{2}(?: |$)")]
    private static partial Regex Decimal();

    public static PoolExpertParse Parse(string rawText)
    {
        var teams = new List<PoolExpertTeam>();
        var skipped = new List<string>();
        PoolExpertTeam? current = null;
        var section = Section.None;

        foreach (var rawLine in rawText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (SectionHeader().Match(line) is { Success: true } header)
            {
                current = new PoolExpertTeam(header.Groups["participant"].Value.Trim(), null, [], []);
                teams.Add(current);
                section = Section.Active;
                continue;
            }
            if (current is null) continue;
            if (line.StartsWith("JOUEURS DE R", StringComparison.Ordinal)) { section = Section.Reserve; continue; }
            if (line.StartsWith("HISTORIQUE", StringComparison.Ordinal)) { section = Section.None; continue; }
            if (section == Section.None) continue;

            if (PlayerLine().Match(line) is not { Success: true } m) continue;
            if (!Decimal().IsMatch(m.Groups["rest"].Value))
            {
                skipped.Add($"{current.Participant}: {line}");
                continue;
            }

            var team = m.Groups["team"].Value;
            if (m.Groups["pos"].Value == "E")
            {
                current = current with { FranchiseAbbrev = RosterNameResolver.NhlAbbrev(team) };
                teams[^1] = current;
                continue;
            }

            var name = m.Groups["name"].Value.Trim();
            var space = name.IndexOf(' ');
            var entry = space > 0
                ? new RosterName(name[..space], name[(space + 1)..], team)
                : new RosterName("", name, team);
            (section == Section.Active ? current.Active : current.Reserve).Add(entry);
        }

        return new PoolExpertParse(teams, skipped);
    }

    private enum Section { None, Active, Reserve }
}
