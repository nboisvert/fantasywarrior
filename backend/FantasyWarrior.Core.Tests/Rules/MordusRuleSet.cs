using FantasyWarrior.Core.Rules;

namespace FantasyWarrior.Core.Tests.Rules;

/// <summary>
/// Les Mordus' real rules, read from <c>data/mordus-rules.json</c> — the file
/// <c>seed-mordus</c> writes to a new season verbatim.
///
/// The one configuration that has to be completely supported: it is the pool the
/// app is built for. Loading the file itself rather than restating its numbers
/// means an edit to it that the code cannot honour fails a test rather than a
/// seed.
/// </summary>
public static class MordusRuleSet
{
    public static RuleSet Build() =>
        RuleSetJson.Deserialize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mordus-rules.json")));
}
