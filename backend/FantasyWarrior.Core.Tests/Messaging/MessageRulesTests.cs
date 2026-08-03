using FantasyWarrior.Core.Messaging;

namespace FantasyWarrior.Core.Tests.Messaging;

public class MessageRulesTests
{
    // --- Prepare ---

    [Fact]
    public void Prepare_AcceptsAnOrdinaryMessage()
    {
        var (body, error) = MessageRules.Prepare("Nice pickup on that trade");
        Assert.Null(error);
        Assert.Equal("Nice pickup on that trade", body);
    }

    [Fact]
    public void Prepare_TrimsRatherThanRejecting_TrailingWhitespace()
    {
        // A phone keyboard adds a trailing space constantly; refusing it would
        // be a wrong answer to a non-problem.
        var (body, error) = MessageRules.Prepare("  ok deal  ");
        Assert.Null(error);
        Assert.Equal("ok deal", body);
    }

    [Fact]
    public void Prepare_RejectsAnEmptyBody()
    {
        Assert.NotNull(MessageRules.Prepare("").Error);
    }

    [Fact]
    public void Prepare_RejectsAWhitespaceOnlyBody_AsEmptyNotAsContent()
    {
        Assert.NotNull(MessageRules.Prepare("   \t \n ").Error);
    }

    [Fact]
    public void Prepare_RejectsNull()
    {
        Assert.NotNull(MessageRules.Prepare(null).Error);
    }

    [Fact]
    public void Prepare_AcceptsExactlyTheColumnLength()
    {
        var (_, error) = MessageRules.Prepare(new string('a', MessageRules.MaxLength));
        Assert.Null(error);
    }

    [Fact]
    public void Prepare_RejectsOneCharacterPastTheColumnLength()
    {
        // The guard exists to stop a silent SQL truncation, so the boundary is
        // the whole point of it.
        Assert.NotNull(MessageRules.Prepare(new string('a', MessageRules.MaxLength + 1)).Error);
    }

    [Fact]
    public void Prepare_MeasuresAfterTrimming_NotBefore()
    {
        var padded = "  " + new string('a', MessageRules.MaxLength) + "  ";
        Assert.Null(MessageRules.Prepare(padded).Error);
    }

    [Fact]
    public void Prepare_KeepsAccentsAndEmoji()
    {
        // The column is nvarchar for exactly this reason: a bilingual pool.
        var (body, error) = MessageRules.Prepare("t'es sérieux? 🏒");
        Assert.Null(error);
        Assert.Equal("t'es sérieux? 🏒", body);
    }

    // --- Preview ---

    [Fact]
    public void Preview_LeavesAShortMessageAlone()
    {
        Assert.Equal("ok deal", MessageRules.Preview("ok deal"));
    }

    [Fact]
    public void Preview_CollapsesNewlinesIntoOneLine()
    {
        // The list row is a single line; a raw newline would break its layout.
        Assert.Equal("first second", MessageRules.Preview("first\n\nsecond"));
    }

    [Fact]
    public void Preview_CutsOnAWordBoundary_NotMidWord()
    {
        var preview = MessageRules.Preview("aaa bbb ccc ddd eee fff ggg hhh", maxLength: 10);
        Assert.Equal("aaa bbb…", preview);
    }

    [Fact]
    public void Preview_CutsMidWord_WhenTheFirstWordIsLongerThanTheBudget()
    {
        // Falling back to a hard cut beats returning nothing when someone
        // pastes an unbroken string.
        var preview = MessageRules.Preview(new string('a', 40), maxLength: 10);
        Assert.Equal(new string('a', 10) + "…", preview);
    }

    [Fact]
    public void Preview_NeverExceedsItsBudgetByMoreThanTheEllipsis()
    {
        var preview = MessageRules.Preview(string.Join(' ', Enumerable.Repeat("word", 60)), maxLength: 80);
        Assert.True(preview.Length <= 81, $"Preview was {preview.Length} characters.");
    }
}
