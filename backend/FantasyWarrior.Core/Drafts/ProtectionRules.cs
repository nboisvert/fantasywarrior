namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// Who cannot be drafted away from his GM no matter what the GM does. Pure.
///
/// A keeper pool's off-season draft opens with rounds where a GM may steal a
/// rival's unprotected player. Protecting costs one of a limited number of
/// slots — but a player with too little NHL experience is off the table for
/// free, which is what stops a pool from turning into a prospect raid every
/// summer.
///
/// <b>The verdict is derived, never stored.</b> What is stored is the
/// measurement it reads: <c>Player.CareerNhlGames</c>, written by career-sync in
/// the same save as the career rows it sums. Keeping the two apart is what lets
/// a threshold move without rewriting a single row, and what keeps this rule
/// written in exactly one place — a threshold copied into a SQL view and a C#
/// class is a threshold that will disagree with itself.
///
/// <b>Goalies count separately, and lower.</b> A goalie plays roughly half his
/// team's games, so measuring him against a skater's bar would keep him
/// untouchable for twice as many seasons.
/// </summary>
public static class ProtectionRules
{
    /// <summary>A goalie at or under this many career NHL games cannot be drafted away.</summary>
    public const int MaxCareerGamesGoalie = 50;

    /// <summary>A forward or defenceman at or under this many career NHL games cannot be drafted away.</summary>
    public const int MaxCareerGamesSkater = 100;

    /// <summary>
    /// Is this player untouchable on experience alone?
    /// </summary>
    /// <param name="positionGroup">
    /// The persisted single letter: F, D, G — or T for the Équipe slot, which
    /// holds a franchise rather than a player. A franchise is never auto-
    /// protected here: it may only ever change hands against another franchise,
    /// so the draft has no way to take one and no need for this rule to say so
    /// twice.
    /// </param>
    /// <param name="careerNhlGames">
    /// Regular-season NHL games played, career to date. The caller is
    /// responsible for never passing a guess: an unsynced player's games are
    /// unknown, not zero, and unknown must not reach this method — see
    /// <c>Player.CareerNhlGames</c>.
    /// </param>
    public static bool IsAutoProtected(string positionGroup, int careerNhlGames) =>
        positionGroup switch
        {
            "T" => false,
            "G" => careerNhlGames <= MaxCareerGamesGoalie,
            _ => careerNhlGames <= MaxCareerGamesSkater,
        };
}
