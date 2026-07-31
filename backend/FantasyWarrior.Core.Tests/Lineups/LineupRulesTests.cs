using FantasyWarrior.Core.Lineups;

namespace FantasyWarrior.Core.Tests.Lineups;

public class LineupRulesTests
{
    // Les Mordus: 9 forwards, 4 defence, 1 goalie active.
    private static readonly LineupSlots Mordus = new(Forwards: 9, Defense: 4, Goalies: 1);
    private static readonly LineupSlots Small = new(Forwards: 2, Defense: 1, Goalies: 1);

    private static LineupCandidate C(string id, string group, double points = 0, int activatedDay = 1) =>
        new(id, PlayerId: long.Parse(id.TrimStart('f', 'd', 'g')), group, points,
            new DateTime(2026, 1, activatedDay, 0, 0, 0, DateTimeKind.Utc));

    private static readonly LineupCandidate[] Roster =
    [
        C("f1", "F", 50), C("f2", "F", 40), C("f3", "F", 30), C("f4", "F", 20),
        C("d1", "D", 25), C("d2", "D", 15),
        C("g1", "G", 35), C("g2", "G", 10),
    ];

    // --- Validate ---

    [Fact]
    public void Validate_AcceptsAFullLegalLineup()
    {
        Assert.Empty(LineupRules.Validate(Roster, ["f1", "f2", "d1", "g1"], Small));
    }

    [Fact]
    public void Validate_AcceptsAnUnderfilledLineup()
    {
        // Fielding fewer players than allowed is a choice, not an error --
        // you simply score less.
        Assert.Empty(LineupRules.Validate(Roster, ["f1", "g1"], Small));
    }

    [Fact]
    public void Validate_AcceptsAnEmptyLineup()
    {
        Assert.Empty(LineupRules.Validate(Roster, [], Small));
    }

    [Fact]
    public void Validate_RejectsTooManyAtOnePosition()
    {
        var errors = LineupRules.Validate(Roster, ["f1", "f2", "f3", "d1", "g1"], Small);

        Assert.Single(errors);
        Assert.Contains("Too many active at F: 3 of 2", errors[0]);
    }

    [Fact]
    public void Validate_RejectsAGoalieFillingAForwardSlot()
    {
        // Slots are per position group; a spare goalie cannot pad the forwards.
        var errors = LineupRules.Validate(Roster, ["g1", "g2"], Small);

        Assert.Single(errors);
        Assert.Contains("Too many active at G: 2 of 1", errors[0]);
    }

    [Fact]
    public void Validate_RejectsASpotFromAnotherRoster()
    {
        var errors = LineupRules.Validate(Roster, ["f1", "someone-elses-spot"], Small);

        Assert.Contains(errors, e => e.Contains("Not on this roster"));
    }

    [Fact]
    public void Validate_RejectsDuplicates()
    {
        // Otherwise the same player could fill two slots.
        var errors = LineupRules.Validate(Roster, ["f1", "f1"], Small);

        Assert.Contains(errors, e => e.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_ReportsEveryProblemAtOnce()
    {
        var errors = LineupRules.Validate(Roster, ["f1", "f1", "f2", "f3", "ghost"], Small);

        Assert.True(errors.Count >= 3);
    }

    // --- Used ---

    [Fact]
    public void Used_CountsPerPositionGroup()
    {
        var used = LineupRules.Used(Roster, ["f1", "f2", "d1"]);

        Assert.Equal(2, used["F"]);
        Assert.Equal(1, used["D"]);
        Assert.Equal(0, used["G"]);
    }

    // --- LegalActiveSet ---

    [Fact]
    public void LegalActiveSet_LeavesALegalLineupAlone()
    {
        var kept = LineupRules.LegalActiveSet(Roster, ["f1", "f2", "d1", "g1"], Small);

        Assert.Equal(4, kept.Count);
    }

    [Fact]
    public void LegalActiveSet_TrimsTheMostRecentlyActivatedFirst()
    {
        // f3 was activated last, so f3 is what made the lineup illegal.
        var roster = new[] { C("f1", "F", 10, activatedDay: 1), C("f2", "F", 99, activatedDay: 2), C("f3", "F", 99, activatedDay: 3) };

        var kept = LineupRules.LegalActiveSet(roster, ["f1", "f2", "f3"], Small);

        Assert.Contains("f1", kept);
        Assert.Contains("f2", kept);
        Assert.DoesNotContain("f3", kept);
    }

    [Fact]
    public void LegalActiveSet_IsDeterministicWhenActivationTimesTie()
    {
        var roster = new[] { C("f1", "F", 1, 1), C("f2", "F", 1, 1), C("f3", "F", 1, 1) };

        var first = LineupRules.LegalActiveSet(roster, ["f3", "f2", "f1"], Small);
        var second = LineupRules.LegalActiveSet(roster, ["f1", "f3", "f2"], Small);

        Assert.Equal(first.OrderBy(x => x), second.OrderBy(x => x));
    }

    [Fact]
    public void LegalActiveSet_DropsSpotsNotOnTheRoster()
    {
        var kept = LineupRules.LegalActiveSet(Roster, ["f1", "ghost"], Small);

        Assert.Equal(["f1"], kept);
    }

    // --- CarryForward ---

    [Fact]
    public void CarryForward_KeepsLastWeeksChoices()
    {
        var carried = LineupRules.CarryForward(Roster, ["f1", "f2", "d1", "g1"], Small);

        Assert.Contains("f1", carried);
        Assert.Contains("f2", carried);
        Assert.Contains("d1", carried);
        Assert.Contains("g1", carried);
    }

    [Fact]
    public void CarryForward_DropsPlayersNoLongerOnTheRoster()
    {
        // "traded" is last week's pick but was dealt away since.
        var carried = LineupRules.CarryForward(Roster, ["f1", "traded", "d1", "g1"], Small);

        Assert.DoesNotContain("traded", carried);
    }

    [Fact]
    public void CarryForward_RefillsTheSlotLeftByADepartedPlayer()
    {
        // A forgotten lineup must not silently waste a slot.
        var carried = LineupRules.CarryForward(Roster, ["f1", "traded", "d1", "g1"], Small);

        Assert.Equal(2, carried.Count(id => id.StartsWith('f')));
        Assert.Contains("f2", carried); // best available forward
    }

    [Fact]
    public void CarryForward_FromNothingFieldsAFullLineup()
    {
        // First week of the season: nobody has set anything yet.
        var carried = LineupRules.CarryForward(Roster, [], Small);

        Assert.Equal(Small.Total, carried.Count);
    }

    // --- AutoFill ---

    [Fact]
    public void AutoFill_PicksTheBestAvailablePerPosition()
    {
        var filled = LineupRules.AutoFill(Roster, new HashSet<string>(), Small);

        Assert.Contains("f1", filled); // 50 pts
        Assert.Contains("f2", filled); // 40 pts
        Assert.DoesNotContain("f3", filled);
        Assert.Contains("d1", filled); // 25 over 15
        Assert.Contains("g1", filled); // 35 over 10
    }

    [Fact]
    public void AutoFill_RespectsAlreadyActivePicks()
    {
        // The GM deliberately started his weaker forward; auto-fill tops up
        // around that choice rather than overriding it.
        var filled = LineupRules.AutoFill(Roster, new HashSet<string> { "f4" }, Small);

        Assert.Contains("f4", filled);
        Assert.Contains("f1", filled);
        Assert.Equal(2, filled.Count(id => id.StartsWith('f')));
    }

    [Fact]
    public void AutoFill_CannotExceedTheRoster()
    {
        var thin = new[] { C("f1", "F", 10) };

        var filled = LineupRules.AutoFill(thin, new HashSet<string>(), Mordus);

        Assert.Single(filled);
    }

    [Fact]
    public void AutoFill_IsIdempotent()
    {
        var once = LineupRules.AutoFill(Roster, new HashSet<string>(), Small);
        var twice = LineupRules.AutoFill(Roster, once, Small);

        Assert.Equal(once.OrderBy(x => x), twice.OrderBy(x => x));
    }

    [Fact]
    public void AutoFilledLineupsAreAlwaysLegal()
    {
        var filled = LineupRules.AutoFill(Roster, new HashSet<string>(), Mordus);

        Assert.Empty(LineupRules.Validate(Roster, [.. filled], Mordus));
    }
}
