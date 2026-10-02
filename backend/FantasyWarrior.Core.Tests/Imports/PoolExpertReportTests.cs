using FantasyWarrior.Core.Imports;
using FantasyWarrior.Core.Players;

namespace FantasyWarrior.Core.Tests.Imports;

/// <summary>
/// Reading PoolExpert's "Classement" report. The text below is copied verbatim
/// from the real 2026-27 export through <c>pdftotext -raw</c> — ghost lines,
/// standings block and swap history included.
/// </summary>
public class PoolExpertReportTests
{
    private const string Report = """
        CLASSEMENT, MORDUS2026-27
        Pos Participant PJ B P 1 7 30 PPts PPts/PJ Dep Diff. 1er
        1 Alexandre Giguere Briere new jersey 16 8 9 13 24 24 24 1.50 0 0
        Personnaliser ce rapport
        1 - ALEXANDRE GIGUERE BRIERE NEW JERSEY
        T Nom Ville PJ B P 1 7 30 PPts PPP Hier H St Sal CapH
        Leon Draisaitl EDM 2 2 2 3 4 4 4 2.00 G9-7 16.50 14.00
        Tim Stützle OTT - - - 0 0 0 0 0.00 9.00 8.35
        D Evan Bouchard EDM 2 4 4 3 8 8 8 4.00 G9-7 12.00 10.50
        D O P BUF 1 0 2 2 2 2 2 2 00 P3 6 8 35 8 35
        D Owen Power BUF 1 0 2 2 2 2 2 2.00 P3-6 8.35 8.35
        G Arturs Silovs PIT 1 0 0 0 5 5 5 5.00 2.80 2.80
        E Devils New Jersey NJ 1 0 0 2 2 2 2 2.00 G3-2 - -
        16 8 9 13 24 24 24 1.20 122.25 109.99
        JOUEURS DE RÉSERVE
        T Nom Ville PJ B P 1 7 30 PPts PPP Hier H St Sal CapH
        Calum Ritchie NYI 1 0 0 0 0 0 0 0.00 0.95 0.94
        Luca Del Bel Belluz CBJ - - - 0 0 0 0 0.00 G6-3 0.86 0.86
        D Justin Faulk DET - - - 0 0 0 0 0.00 4.50 6.50
        G John Gibson DET - - - 0 0 0 0 0.00 6.40 6.40
        HISTORIQUE ACTIF/RÉSERVE
        T Nom Ville PJ B P 1 7 30 PPts PPP Hier H St Sal CapH Début Fin
        D Justin Faulk DET - - - 0 0 0 0 0.00 4.50 6.50 29 septembre 28 septembre
        0 0 0 0 0 0 0 0.00 11.81 13.81
        Personnaliser ce rapport Haut de la page
        2 - JONATHAN MARCIL FLORIDE
        T Nom Ville PJ B P 1 7 30 PPts PPP Hier H St Sal CapH
        J.T. Miller NYR 2 0 0 0 0 0 0 0.00 G5-1 7.00 8.00
        E Panthers Florida FLA 2 0 0 1 3 3 3 1.50 P3-4 - -
        JOUEURS DE RÉSERVE
        A G i k NJ 1 0 0 0 0 0 0 0 00 G3 2 2 25 3 25
        Arseny Gritsyuk NJ 1 0 0 0 0 0 0 0.00 G3-2 2.25 3.25
        """;

    private static readonly PoolExpertParse Parsed = PoolExpertReport.Parse(Report);

    [Fact]
    public void EachParticipantSectionIsATeam()
    {
        Assert.Equal(["ALEXANDRE GIGUERE BRIERE NEW JERSEY", "JONATHAN MARCIL FLORIDE"],
            Parsed.Teams.Select(t => t.Participant));
    }

    [Fact]
    public void TheELineIsTheFranchiseMappedToTheNhlAbbrev()
    {
        Assert.Equal(["NJD", "FLA"], Parsed.Teams.Select(t => t.FranchiseAbbrev));
    }

    [Fact]
    public void LinesAboveTheReserveHeaderAreTheActiveLineup()
    {
        Assert.Equal(
        [
            new RosterName("Leon", "Draisaitl", "EDM"),
            new RosterName("Tim", "Stützle", "OTT"),
            new RosterName("Evan", "Bouchard", "EDM"),
            new RosterName("Owen", "Power", "BUF"),
            new RosterName("Arturs", "Silovs", "PIT"),
        ], Parsed.Teams[0].Active);
    }

    [Fact]
    public void TheReserveStopsAtTheSwapHistory()
    {
        // Faulk appears again in the history block; he is listed once.
        Assert.Equal(
        [
            new RosterName("Calum", "Ritchie", "NYI"),
            new RosterName("Luca", "Del Bel Belluz", "CBJ"),
            new RosterName("Justin", "Faulk", "DET"),
            new RosterName("John", "Gibson", "DET"),
        ], Parsed.Teams[0].Reserve);
    }

    [Fact]
    public void GarbledGhostLinesAreSkippedAndReported()
    {
        Assert.Equal(2, Parsed.Skipped.Count);
        Assert.Contains(Parsed.Skipped, s => s.Contains("D O P BUF"));
        Assert.Contains(Parsed.Skipped, s => s.Contains("A G i k NJ"));
        Assert.Equal([new RosterName("Arseny", "Gritsyuk", "NJ")], Parsed.Teams[1].Reserve);
    }

    [Fact]
    public void InitialsWithDotsStayInTheFirstName()
    {
        Assert.Equal([new RosterName("J.T.", "Miller", "NYR")], Parsed.Teams[1].Active);
    }
}
