using Microsoft.EntityFrameworkCore;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Svc = Sehatak.Infrastructure.Services.ChatHistoryService;

namespace Sehatak.IntegrationTests.Chat;

[Collection(SqlServerCollection.Name)]
public class ChatHistoryServiceTests(SqlServerFixture fx) : ChatTestBase(fx)
{
    private const string DeletedText = "تم حذف هذه الرسالة";
    private Svc NewSvc() => new(Fx.Harness.TenantFactory);

    // ------------------------------------------------------------------ conversation

    [Fact]
    public async Task A_conversation_has_both_directions_in_time_order_and_nobody_elses_messages()
    {
        await AddChatAsync(B, A, "second", minutesAgo: 20);
        await AddChatAsync(A, B, "first", minutesAgo: 30);
        await AddChatAsync(A, B, "third", minutesAgo: 10);
        await AddChatAsync(A, DocUser, "other chat", minutesAgo: 5);
        await AddChatAsync(DocUser, B, "not mine", minutesAgo: 4);

        var list = await NewSvc().GetConversationAsync(C.Id, A, B);

        list.Select(m => m.Message).ToArray().ShouldBe(new[] { "first", "second", "third" });
        list[0].SenterId.ShouldBe(A);
        list[0].ReceiverId.ShouldBe(B);
        list[1].SenterId.ShouldBe(B);
    }

    [Fact]
    public async Task A_deleted_message_shows_a_placeholder_never_its_text()
    {
        await AddChatAsync(A, B, "secret text", deleted: true);
        var m = (await NewSvc().GetConversationAsync(C.Id, A, B)).ShouldHaveSingleItem();
        m.Message.ShouldBe(DeletedText);
        m.IsDeleted.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ inbox

    [Fact]
    public async Task Inbox_has_one_row_per_person_newest_first_with_unread_counts()
    {
        await AddChatAsync(DocUser, A, "d1", minutesAgo: 50);                       // unread from the doctor
        await AddChatAsync(A, DocUser, "mine", minutesAgo: 40);                     // my own: never unread
        await AddChatAsync(DocUser, A, "d2 gone", minutesAgo: 30, deleted: true);   // deleted: not counted, masked
        await AddChatAsync(B, A, "b1", minutesAgo: 20, read: true);
        await AddChatAsync(B, A, "b2", minutesAgo: 5);

        var inbox = await NewSvc().GetInboxAsync(C.Id, A);

        inbox.Select(i => i.OtherUserId).ToArray().ShouldBe(new[] { B, DocUser });

        var withB = inbox[0];
        withB.LastMessage.ShouldBe("b2");
        withB.LastMessageSenderId.ShouldBe(B);
        withB.LastMessageIsRead.ShouldBeFalse();
        withB.UnreadCount.ShouldBe(1);

        var withDoc = inbox[1];
        withDoc.LastMessage.ShouldBe(DeletedText);
        withDoc.UnreadCount.ShouldBe(1);
    }

    [Fact]
    public async Task Inbox_of_someone_with_no_messages_is_empty()
        => (await NewSvc().GetInboxAsync(C.Id, A)).ShouldBeEmpty();

    // ------------------------------------------------------------------ search

    private async Task NameAsync(int userId, string first, string last, string email)
    {
        await using var db = Db();
        var u = await db.Users.SingleAsync(x => x.Id == userId);
        u.firstName = first; u.lastName = last; u.email = email;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Search_finds_active_people_by_name_or_email_and_never_the_searcher()
    {
        var off = await C.AddReceptionistAsync(active: false);
        await NameAsync(B, "Zaid", "Hamdan", "zaid.hamdan@clinic.test");
        await NameAsync(off, "Zaid", "Inactive", "zaid.off@clinic.test");
        await NameAsync(A, "Zaid", "Myself", "zaid.me@clinic.test");

        var byName = await NewSvc().SearchUsersAsync(C.Id, A, "Zaid Ham");
        byName.Select(u => u.UserId).ToArray().ShouldBe(new[] { B });

        var byEmail = await NewSvc().SearchUsersAsync(C.Id, A, "zaid.hamdan@");
        byEmail.Select(u => u.UserId).ToArray().ShouldBe(new[] { B });

        var both = await NewSvc().SearchUsersAsync(C.Id, A, "Zaid");
        both.Select(u => u.UserId).ToArray().ShouldBe(new[] { B });   // not the inactive one, not me
        both[0].FullName.ShouldBe("Zaid Hamdan");
        both[0].Role.ShouldBe("Receptionist");
    }

    [Fact]
    public async Task An_empty_search_lists_at_most_20_people()
    {
        for (var i = 0; i < 22; i++) await C.AddReceptionistAsync();
        (await NewSvc().SearchUsersAsync(C.Id, A, "")).Count.ShouldBe(20);
        (await NewSvc().SearchUsersAsync(C.Id, A, "   ")).Count.ShouldBe(20);
    }

    [Fact]
    public async Task A_searcher_who_does_not_exist_gets_nothing()
        => (await NewSvc().SearchUsersAsync(C.Id, 999999, "a")).ShouldBeEmpty();
}
