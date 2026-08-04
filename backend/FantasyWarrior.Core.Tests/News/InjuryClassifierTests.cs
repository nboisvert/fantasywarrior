using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.News;

namespace FantasyWarrior.Core.Tests.News;

/// <summary>
/// Injured or suspended — the only distinction the app draws, decided once
/// from the source's own short label. Both keep a player out of the lineup,
/// but calling a suspension an injury is a statement about a real person that
/// happens to be false.
/// </summary>
public class InjuryClassifierTests
{
    [Theory]
    [InlineData("Suspension")]
    [InlineData("suspension")]
    [InlineData("Suspended")]
    [InlineData("Suspended - 6 games")]
    public void Suspensions_AreNotInjuries(string label) =>
        Assert.Equal(InjuryStatuses.Suspended, InjuryClassifier.StatusFor(label));

    [Theory]
    [InlineData("Hip")]
    [InlineData("Achilles")]
    [InlineData("Upper Body")]
    [InlineData("Undisclosed")]
    public void EveryOtherLabel_IsAnInjury(string label) =>
        Assert.Equal(InjuryStatuses.Injured, InjuryClassifier.StatusFor(label));

    /// <summary>The sites can invent a new label any day, and "hurt, cause
    /// unclear" is the safer of the two wrong answers — a player on an
    /// injuries page is unavailable either way.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Personal reasons")]
    public void AnUnknownOrMissingLabel_StillMarksHimOut(string? label) =>
        Assert.Equal(InjuryStatuses.Injured, InjuryClassifier.StatusFor(label));
}
