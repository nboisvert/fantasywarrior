using System.Security.Cryptography;

namespace FantasyWarrior.Data;

/// <summary>
/// Generates a league's public code — the string the API returns as the
/// league's <c>id</c> and that a GM types to join.
///
/// It is read aloud and retyped from a phone, so the alphabet drops every
/// character that looks like another: no 0/O, no 1/I/L, no 5/S, no 8/B. Six
/// characters from the remaining 27 is ~387 million combinations, which is
/// absurd overkill for a handful of pools and exactly the point — collisions
/// should never need handling beyond a retry.
/// </summary>
public static class JoinCodes
{
    private const string Alphabet = "ACDEFGHJKMNPQRTUVWXYZ234679";

    public const int Length = 6;

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>
    /// Whether a user-typed code could possibly be one of ours. Cheap enough to
    /// run before hitting the database on a bad paste.
    /// </summary>
    public static bool IsWellFormed(string? code) =>
        code is { Length: Length } && code.All(Alphabet.Contains);
}
