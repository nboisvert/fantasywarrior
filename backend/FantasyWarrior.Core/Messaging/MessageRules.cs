namespace FantasyWarrior.Core.Messaging;

/// <summary>
/// What may be sent. Pure, so the one rule that protects the column is tested
/// without a database in the loop.
/// </summary>
public static class MessageRules
{
    /// <summary>Matches <c>Message.Body</c>'s nvarchar(1000).</summary>
    public const int MaxLength = 1000;

    /// <summary>
    /// Trims and checks a draft. <c>Error</c> is null when the body is sendable,
    /// and <c>Body</c> is then exactly what should be stored.
    ///
    /// Trailing whitespace is stripped rather than rejected: a message typed on
    /// a phone keyboard routinely ends in a space, and refusing it would be a
    /// wrong answer to a non-problem. An all-whitespace body, on the other hand,
    /// is an empty one — the Send button should never have been enabled.
    /// </summary>
    public static (string Body, string? Error) Prepare(string? raw)
    {
        var body = (raw ?? string.Empty).Trim();

        if (body.Length == 0) return (body, "A message cannot be empty.");
        if (body.Length > MaxLength) return (body, $"A message cannot exceed {MaxLength} characters.");

        return (body, null);
    }

    /// <summary>
    /// The one-line preview shown in the conversation list, cut on a word
    /// boundary so the ellipsis never lands mid-word. Purely cosmetic, but it
    /// is also what keeps the list response small: without it every row would
    /// carry up to a kilobyte of text nobody reads.
    /// </summary>
    public static string Preview(string body, int maxLength = 80)
    {
        var flattened = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flattened.Length <= maxLength) return flattened;

        var cut = flattened[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2) cut = cut[..lastSpace];
        return cut.TrimEnd() + "…";
    }
}
