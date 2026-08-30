using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantasyWarrior.Core.Rules;

/// <summary>
/// How a <see cref="RuleSet"/> is written to and read from storage.
///
/// <b>One options instance, shared.</b> The document is persisted, so the
/// serializer settings are part of the storage format: a second instance
/// configured slightly differently would write documents the first cannot read,
/// and the failure would surface as a rule silently reverting to its default.
///
/// <b>Enums as names, not numbers.</b> The point of a JSON column over a wall of
/// columns is that the rules can be read straight out of the database by a human
/// — <c>"lineup": { "mode": "activeSelection" }</c> rather than <c>0</c>. It also
/// means reordering an enum cannot silently repoint every stored value.
/// </summary>
public static class RuleSetJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The scale and the per-position overrides are keyed by stat name and by
        // position letter; those are data, and must survive verbatim.
        DictionaryKeyPolicy = null,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // Absent means "this document predates the property", which is exactly
        // what the property's default is for.
        RespectNullableAnnotations = false,
        WriteIndented = false,
    };

    public static string Serialize(RuleSet rules) => JsonSerializer.Serialize(rules, Options);

    /// <summary>
    /// Reads a stored document.
    ///
    /// <b>An empty or absent one deliberately does not become the defaults.</b>
    /// It deserializes to <see cref="RuleSet.IsUnwritten"/>, which callers
    /// refuse on: a league whose rules were never converted would otherwise read
    /// as one with no cap, no slots and no protections, and every screen would
    /// report those as its rules. Never a null — nothing downstream could carry
    /// one usefully.
    /// </summary>
    public static RuleSet Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new RuleSet()
            : JsonSerializer.Deserialize<RuleSet>(json, Options) ?? new RuleSet();
}
