using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Sehatak.API.Hubs;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Chat;

[Collection(SqlServerCollection.Name)]
public class ChatHubTests(SqlServerFixture fx) : ChatTestBase(fx)
{
    // ------------------------------------------------------------------ SendMessage

    [Fact]
    public async Task Sending_stores_the_message_and_tells_both_people()
    {
        var f = NewHub(A);
        await f.Hub.SendMessage(B, "  hello  ");

        await using var db = Db();
        var row = db.Chats.Single();
        row.SenderId.ShouldBe(A);
        row.ReceiverId.ShouldBe(B);
        row.Message.ShouldBe("hello");
        row.IsRead.ShouldBeFalse();
        row.IsDeleted.ShouldBeFalse();
        Math.Abs((DateTime.UtcNow - row.SentAt).TotalMinutes).ShouldBeLessThan(2);

        var toReceiver = f.Sent.Single(s => s.Method == "ReceiveMessage");
        var toSender = f.Sent.Single(s => s.Method == "MessageSent");
        IsTo(toReceiver, B).ShouldBeTrue();
        IsTo(toSender, A).ShouldBeTrue();
        IdOf(toReceiver.Payload).ShouldBe(row.Id);
    }

    [Fact]
    public async Task A_token_without_a_center_cannot_send()
    {
        var f = NewHub(A, centerClaim: false);
        await Should.ThrowAsync<HubException>(() => f.Hub.SendMessage(B, "hi"));
        (await ChatCountAsync()).ShouldBe(0);
    }

    /// <summary>
    /// User ids repeat between centers (every center has its own database). Addressing "5" reaches user 5 of EVERY connected center:
    /// a message meant for center A's user 5 is also delivered to center B's user 5.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Messages_are_addressed_to_center_and_user_never_to_the_bare_id()
    {
        var f = NewHub(A);
        await f.Hub.SendMessage(B, "hello");

        f.Sent.Single(s => s.Method == "ReceiveMessage").To.ShouldBe($"{C.Id}:{B}");
        f.Sent.Single(s => s.Method == "MessageSent").To.ShouldBe($"{C.Id}:{A}");
    }

    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_empty_message_is_refused(string? text)
    {
        var f = NewHub(A);
        await Should.ThrowAsync<HubException>(() => f.Hub.SendMessage(B, text!));
        (await ChatCountAsync()).ShouldBe(0);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_huge_message_is_refused()
    {
        var f = NewHub(A);
        await Should.ThrowAsync<HubException>(() => f.Hub.SendMessage(B, new string('x', 2001)));
        (await ChatCountAsync()).ShouldBe(0);

        await f.Hub.SendMessage(B, new string('x', 2000));                 // the limit itself is fine
        (await ChatCountAsync()).ShouldBe(1);
    }

    /// <summary>An unknown id ends as a raw foreign-key error; an inactive user or yourself is simply accepted.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_receiver_must_be_a_different_active_user_of_this_center()
    {
        var off = await C.AddReceptionistAsync(active: false);
        var f = NewHub(A);

        await Should.ThrowAsync<HubException>(() => f.Hub.SendMessage(999999, "hi"));
        await Should.ThrowAsync<HubException>(() => f.Hub.SendMessage(off, "hi"));
        await Should.ThrowAsync<HubException>(() => f.Hub.SendMessage(A, "note to self"));
        (await ChatCountAsync()).ShouldBe(0);
    }

    // ------------------------------------------------------------------ typing

    [Fact]
    public async Task Typing_signals_reach_only_the_other_person()
    {
        var f = NewHub(A);
        await f.Hub.Typing(B);
        await f.Hub.StopTyping(B);

        f.Sent.Select(s => s.Method).ToArray().ShouldBe(new[] { "UserTyping", "UserStoppedTyping" });
        f.Sent.ShouldAllBe(s => IsTo(s, B));
    }

    // ------------------------------------------------------------------ MarkAsRead

    [Fact]
    public async Task The_receiver_marks_a_message_as_read_and_the_sender_is_told()
    {
        var id = await AddChatAsync(A, B, "hi");
        var f = NewHub(B);

        await f.Hub.MarkAsRead(id);

        (await ChatAsync(id)).IsRead.ShouldBeTrue();
        var told = f.Sent.Single();
        told.Method.ShouldBe("MessageRead");
        IsTo(told, A).ShouldBeTrue();
        MessageIdOf(told.Payload).ShouldBe(id);
    }

    [Fact]
    public async Task Marking_an_unknown_message_fails()
    {
        var f = NewHub(B);
        await Should.ThrowAsync<HubException>(() => f.Hub.MarkAsRead(999999));
    }

    /// <summary>Any staff member can mark ANY message as read, which also fires a false "read" tick at the sender.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Only_the_receiver_can_mark_a_message_as_read()
    {
        var id = await AddChatAsync(A, B, "private");
        var stranger = NewHub(DocUser);
        var sender = NewHub(A);

        await Should.ThrowAsync<HubException>(() => stranger.Hub.MarkAsRead(id));
        await Should.ThrowAsync<HubException>(() => sender.Hub.MarkAsRead(id));

        (await ChatAsync(id)).IsRead.ShouldBeFalse();
        stranger.Sent.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ DeleteMessage

    [Fact]
    public async Task The_sender_deletes_his_message_and_both_people_are_told()
    {
        var id = await AddChatAsync(A, B, "oops");
        var f = NewHub(A);

        await f.Hub.DeleteMessage(id);

        (await ChatAsync(id)).IsDeleted.ShouldBeTrue();
        f.Sent.Count(s => s.Method == "MessageDeleted").ShouldBe(2);
        f.Sent.Any(s => IsTo(s, A)).ShouldBeTrue();
        f.Sent.Any(s => IsTo(s, B)).ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_an_unknown_message_fails()
    {
        var f = NewHub(A);
        await Should.ThrowAsync<HubException>(() => f.Hub.DeleteMessage(999999));
    }

    /// <summary>Any staff member can delete anyone's message (even a conversation they are not part of).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Nobody_but_the_sender_can_delete_a_message()
    {
        var id = await AddChatAsync(A, B, "evidence");

        await Should.ThrowAsync<HubException>(() => NewHub(DocUser).Hub.DeleteMessage(id));   // a stranger
        await Should.ThrowAsync<HubException>(() => NewHub(B).Hub.DeleteMessage(id));          // even the receiver

        (await ChatAsync(id)).IsDeleted.ShouldBeFalse();
    }

    // ------------------------------------------------------------------ presence

    /// <summary>"UserOnline" goes to Clients.Others: every connected user of EVERY center learns who is online in this one.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Presence_is_only_announced_inside_the_users_own_center()
    {
        var f = NewHub(A, connectionId: "c1");
        await f.Hub.OnConnectedAsync();

        f.OthersEvents.ShouldBeEmpty();
        f.GroupEvents.ShouldHaveSingleItem().ShouldBe(($"center-{C.Id}", "UserOnline"));
        await f.Groups.Received().AddToGroupAsync("c1", $"center-{C.Id}", Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Online_and_offline_are_announced_only_for_the_first_and_last_connection()
    {
        var tracker = new OnlineUserTracker();
        var phone = NewHub(A, tracker, connectionId: "phone");
        var laptop = NewHub(A, tracker, connectionId: "laptop");

        await phone.Hub.OnConnectedAsync();
        await laptop.Hub.OnConnectedAsync();
        phone.GroupEvents.Count(e => e.Method == "UserOnline").ShouldBe(1);
        laptop.GroupEvents.ShouldBeEmpty();

        await phone.Hub.OnDisconnectedAsync(null);
        phone.GroupEvents.Count(e => e.Method == "UserOffline").ShouldBe(0);
        await laptop.Hub.OnDisconnectedAsync(null);
        laptop.GroupEvents.Count(e => e.Method == "UserOffline").ShouldBe(1);
    }
}
