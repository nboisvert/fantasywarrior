namespace FantasyWarrior.Core.Messaging;

/// <summary>
/// One stored message, reduced to what the conversation list needs. A plain
/// record rather than the EF entity, for the same reason as
/// <c>LineupCandidate</c>: it is what lets the rollup below be pure.
/// </summary>
public sealed record MessageRow(
    long MessageId,
    int SenderUserId,
    int RecipientUserId,
    string Body,
    DateTime SentUtc,
    DateTime? ReadUtc);

/// <summary>One row of the conversation list.</summary>
/// <param name="PeerUserId">The other person — never the viewer.</param>
/// <param name="LastFromMe">Drives the "You: " prefix on the preview line.</param>
public sealed record Conversation(
    int PeerUserId,
    string Preview,
    DateTime LastSentUtc,
    bool LastFromMe,
    int UnreadCount);

/// <summary>
/// Turns a league's flat message history into the list the chat sheet renders.
///
/// This is a rollup rather than a query so the shape of a conversation is
/// defined in one tested place: there is no Conversation table to disagree
/// with, and a thread is only ever "the messages between these two people",
/// read in both directions.
/// </summary>
public static class ConversationSummary
{
    /// <summary>
    /// Newest conversation first. Messages that involve neither side of the
    /// viewer are ignored rather than throwing — the caller filters by league,
    /// and a defensive skip here is cheaper than trusting it.
    /// </summary>
    public static IReadOnlyList<Conversation> Build(IEnumerable<MessageRow> messages, int viewerUserId)
    {
        var byPeer = new Dictionary<int, List<MessageRow>>();

        foreach (var m in messages)
        {
            int peer;
            if (m.SenderUserId == viewerUserId) peer = m.RecipientUserId;
            else if (m.RecipientUserId == viewerUserId) peer = m.SenderUserId;
            else continue;

            // A message to oneself has no peer to name; nothing in the UI can
            // create one, so drop it rather than inventing a row for it.
            if (peer == viewerUserId) continue;

            if (!byPeer.TryGetValue(peer, out var list)) byPeer[peer] = list = [];
            list.Add(m);
        }

        return byPeer
            .Select(kv =>
            {
                // MessageId breaks the tie: two messages can share a SentUtc
                // down to the tick, and "last" has to be deterministic or the
                // preview flickers between them on every refetch.
                var last = kv.Value
                    .OrderByDescending(m => m.SentUtc)
                    .ThenByDescending(m => m.MessageId)
                    .First();

                return new Conversation(
                    PeerUserId: kv.Key,
                    Preview: MessageRules.Preview(last.Body),
                    LastSentUtc: last.SentUtc,
                    LastFromMe: last.SenderUserId == viewerUserId,
                    UnreadCount: kv.Value.Count(m => m.RecipientUserId == viewerUserId && m.ReadUtc is null));
            })
            .OrderByDescending(c => c.LastSentUtc)
            .ThenBy(c => c.PeerUserId)
            .ToList();
    }
}
