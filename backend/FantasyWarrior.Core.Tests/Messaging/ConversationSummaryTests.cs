using FantasyWarrior.Core.Messaging;

namespace FantasyWarrior.Core.Tests.Messaging;

public class ConversationSummaryTests
{
    private const int Me = 1;
    private const int Bob = 2;
    private const int Ann = 3;

    private static readonly DateTime Start = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);

    private static MessageRow M(
        long id, int from, int to, string body, int minute, DateTime? read = null) =>
        new(id, from, to, body, Start.AddMinutes(minute), read);

    [Fact]
    public void Build_ReturnsNothing_ForAnEmptyHistory()
    {
        Assert.Empty(ConversationSummary.Build([], Me));
    }

    [Fact]
    public void Build_GroupsBothDirections_IntoOneConversation()
    {
        // The peer is the other person, whichever end of the message they are.
        var rows = new[]
        {
            M(1, Me, Bob, "you around?", 0),
            M(2, Bob, Me, "yeah", 1),
        };

        var conversation = Assert.Single(ConversationSummary.Build(rows, Me));
        Assert.Equal(Bob, conversation.PeerUserId);
    }

    [Fact]
    public void Build_TakesThePreviewFromTheNewestMessage()
    {
        var rows = new[]
        {
            M(1, Me, Bob, "you around?", 0),
            M(2, Bob, Me, "yeah", 1),
        };

        var conversation = Assert.Single(ConversationSummary.Build(rows, Me));
        Assert.Equal("yeah", conversation.Preview);
        Assert.False(conversation.LastFromMe);
    }

    [Fact]
    public void Build_FlagsLastFromMe_WhenTheViewerSpokeLast()
    {
        var rows = new[] { M(1, Bob, Me, "yeah", 0), M(2, Me, Bob, "cool", 1) };

        var conversation = Assert.Single(ConversationSummary.Build(rows, Me));
        Assert.True(conversation.LastFromMe);
    }

    [Fact]
    public void Build_BreaksATieOnMessageId_NotOnEnumerationOrder()
    {
        // Two messages can share a SentUtc down to the tick. Without the id
        // tiebreak the preview flickers between them on every refetch.
        var rows = new[]
        {
            M(7, Bob, Me, "first", 0),
            M(8, Bob, Me, "second", 0),
        };

        Assert.Equal("second", Assert.Single(ConversationSummary.Build(rows, Me)).Preview);
        Assert.Equal("second", Assert.Single(ConversationSummary.Build(rows.Reverse(), Me)).Preview);
    }

    [Fact]
    public void Build_CountsOnlyInboundUnread_NotTheViewersOwnUnreadMessages()
    {
        // The viewer's own sent messages are unread *by the peer*. Counting
        // them would badge the viewer for his own talking.
        var rows = new[]
        {
            M(1, Bob, Me, "one", 0),
            M(2, Bob, Me, "two", 1),
            M(3, Me, Bob, "mine", 2),
        };

        Assert.Equal(2, Assert.Single(ConversationSummary.Build(rows, Me)).UnreadCount);
    }

    [Fact]
    public void Build_DoesNotCountAMessageThatHasBeenRead()
    {
        var rows = new[]
        {
            M(1, Bob, Me, "one", 0, read: Start.AddMinutes(5)),
            M(2, Bob, Me, "two", 1),
        };

        Assert.Equal(1, Assert.Single(ConversationSummary.Build(rows, Me)).UnreadCount);
    }

    [Fact]
    public void Build_OrdersNewestConversationFirst()
    {
        var rows = new[]
        {
            M(1, Ann, Me, "older", 0),
            M(2, Bob, Me, "newer", 10),
        };

        var conversations = ConversationSummary.Build(rows, Me);
        Assert.Equal([Bob, Ann], conversations.Select(c => c.PeerUserId));
    }

    [Fact]
    public void Build_IsDeterministic_WhenTwoConversationsShareATimestamp()
    {
        var rows = new[] { M(1, Ann, Me, "a", 0), M(2, Bob, Me, "b", 0) };

        Assert.Equal([Bob, Ann], ConversationSummary.Build(rows, Me).Select(c => c.PeerUserId));
        Assert.Equal([Bob, Ann], ConversationSummary.Build(rows.Reverse(), Me).Select(c => c.PeerUserId));
    }

    [Fact]
    public void Build_IgnoresMessagesTheViewerIsNotPartyTo()
    {
        // Defensive: the caller filters by league, but a leak here would show
        // one GM another pair's private thread.
        var rows = new[] { M(1, Bob, Ann, "not yours", 0), M(2, Bob, Me, "yours", 1) };

        var conversation = Assert.Single(ConversationSummary.Build(rows, Me));
        Assert.Equal("yours", conversation.Preview);
    }

    [Fact]
    public void Build_IgnoresAMessageFromTheViewerToHimself()
    {
        Assert.Empty(ConversationSummary.Build([M(1, Me, Me, "note to self", 0)], Me));
    }

    [Fact]
    public void Build_TruncatesALongPreview()
    {
        var conversation = Assert.Single(
            ConversationSummary.Build([M(1, Bob, Me, new string('a', 500), 0)], Me));
        Assert.EndsWith("…", conversation.Preview);
        Assert.True(conversation.Preview.Length < 100);
    }
}
