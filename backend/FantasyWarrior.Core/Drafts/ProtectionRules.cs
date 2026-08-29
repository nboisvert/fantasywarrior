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
/// <summary>
/// Why a rostered player is out of a steal draft's reach — or that he is not.
///
/// Three ways to be safe, and they are not interchangeable: one was bought with
/// a slot, one is free, and one is an absence of data. A screen that collapsed
/// them would tell a GM his prospect was protected when in fact nobody spent
/// anything on him.
/// </summary>
public enum ProtectionKind : byte
{
    /// <summary>Takeable. Nothing is standing between him and a rival GM.</summary>
    Exposed = 0,

    /// <summary>His GM spent one of the league's protection slots on him.</summary>
    ByGm = 1,

    /// <summary>Too few career NHL games — free, and nobody chose it.</summary>
    Auto = 2,

    /// <summary>
    /// His career total was never synced. <c>DraftPool</c> refuses to draft him
    /// on exactly this ground, so he is safe — but calling that "protected"
    /// would be reporting a gap in our data as a rule of the pool.
    /// </summary>
    Unknown = 3,
}

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

    /// <summary>
    /// Which of the three shelters a rostered player is standing under, if any.
    ///
    /// <b>Deliberately mirrors the three untouchable branches of
    /// <c>DraftPool.StealReason</c></b>, in the same order — a GM's slot first,
    /// then unknown experience, then the auto bar. <c>DraftPoolTests</c> asserts
    /// the two agree, because a screen that said "exposed" about a man the pool
    /// then refused to hand over would be worse than no screen.
    ///
    /// It answers only about a player on a roster. An unrostered player belongs
    /// to the rookie rounds, where none of this applies.
    /// </summary>
    public static ProtectionKind KindOf(
        string positionGroup, int? careerNhlGames, bool protectedByGm)
    {
        // A franchise cannot be drafted at all, and no slot was spent saying so.
        if (positionGroup == "T") return ProtectionKind.Auto;

        if (protectedByGm) return ProtectionKind.ByGm;
        if (careerNhlGames is not { } games) return ProtectionKind.Unknown;

        return IsAutoProtected(positionGroup, games)
            ? ProtectionKind.Auto
            : ProtectionKind.Exposed;
    }
}
