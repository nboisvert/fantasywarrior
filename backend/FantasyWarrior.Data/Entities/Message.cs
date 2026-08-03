namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One direct message between two GMs, scoped to a league.
///
/// <b>Threads are per-league on purpose.</b> A user can belong to several pools
/// (see <see cref="User"/>), and the same two people talking in two different
/// leagues are having two different conversations — the context is the pool, not
/// the person. It also keeps the contact list trivially correct: a league's
/// conversation list is its own membership, never a union across pools.
///
/// There is no Conversation entity. A thread is just the messages between two
/// user ids, read in both directions; with a pool of a dozen GMs the join that
/// would save is not worth the row it would cost. <c>ConversationSummary</c> in
/// FantasyWarrior.Core turns a flat list into the list the UI renders.
/// </summary>
public sealed class Message
{
    public long MessageId { get; set; }

    public int LeagueId { get; set; }

    public int SenderUserId { get; set; }

    public int RecipientUserId { get; set; }

    public required string Body { get; set; }

    public DateTime SentUtc { get; set; }

    /// <summary>
    /// When the recipient opened the thread. Null means unread, which is the
    /// whole of the unread-badge query — no counter to keep in step with it.
    /// </summary>
    public DateTime? ReadUtc { get; set; }

    public League? League { get; set; }

    public User? Sender { get; set; }

    public User? Recipient { get; set; }
}
