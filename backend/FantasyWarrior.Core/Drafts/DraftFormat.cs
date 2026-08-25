namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// How a player's name reads on a draft row. Pure, and on the server on
/// purpose.
///
/// The room's rows are the truncated form — "N. MacKinnon" (Nick, 2026-08-25).
/// Deriving that on the client would mean splitting <c>FullName</c> on a space,
/// which is wrong for "Jean-Gabriel Pageau" and "Ryan Nugent-Hopkins" the moment
/// anyone tries it. The database already stores the two halves separately, so
/// the honest thing is to use them and send the answer.
/// </summary>
public static class DraftFormat
{
    /// <summary>
    /// "Nathan", "MacKinnon" -> "N. MacKinnon". A missing first name yields the
    /// surname alone rather than a stray dot.
    /// </summary>
    public static string ShortName(string? firstName, string? lastName)
    {
        var last = (lastName ?? "").Trim();
        var first = (firstName ?? "").Trim();

        if (first.Length == 0) return last;
        if (last.Length == 0) return first;

        return $"{first[0]}. {last}";
    }
}
