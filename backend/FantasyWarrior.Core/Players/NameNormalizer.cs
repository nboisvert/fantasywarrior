using System.Globalization;
using System.Text;

namespace FantasyWarrior.Core.Players;

public static class NameNormalizer
{
    /// <summary>Lowercase, diacritics stripped, punctuation removed, spaces collapsed.</summary>
    public static string Normalize(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
            else if (char.IsWhiteSpace(c) && sb.Length > 0 && sb[^1] != ' ')
                sb.Append(' ');
        }
        return sb.ToString().TrimEnd();
    }
}
