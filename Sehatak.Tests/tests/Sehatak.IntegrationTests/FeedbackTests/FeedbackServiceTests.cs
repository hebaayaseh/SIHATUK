using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.FeedbackDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.FeedbackTests;

[Collection(SqlServerCollection.Name)]
public class FeedbackServiceTests(SqlServerFixture fx) : FeedbackTestBase(fx)
{
    // ---------- submit (signed in) ----------

    [Fact]
    public async Task Submit_stores_the_sender_from_the_token_and_trims_the_text()
    {
        var r = await NewFeedback().SubmitAsync(C.Id, Pat.UserId, "Patient", "Pat Test", "pat@test.local",
            Req("  Printer problem  ", "  It does not print  "));

        var row = await RowAsync(r.FeedbackId);
        row.CenterId.ShouldBe(C.Id);
        row.SenderUserId.ShouldBe(Pat.UserId);
        row.SenderRole.ShouldBe("Patient");
        row.SenderName.ShouldBe("Pat Test");
        row.SenderEmail.ShouldBe("pat@test.local");
        row.Subject.ShouldBe("Printer problem");
        row.Message.ShouldBe("It does not print");
        row.Status.ShouldBe(FeedbackStatus.New);
        row.Type.ShouldBe(FeedbackType.Bug);
        r.Status.ShouldBe("New");
        r.Type.ShouldBe("Bug");
        r.Reply.ShouldBeNull();
    }

    [Fact]
    public async Task Submit_cuts_very_long_sender_fields_instead_of_failing()
    {
        var r = await NewFeedback().SubmitAsync(C.Id, Pat.UserId, new string('r', 100), new string('n', 300), new string('e', 300), Req());

        var row = await RowAsync(r.FeedbackId);
        row.SenderRole!.Length.ShouldBe(30);
        row.SenderName.Length.ShouldBe(200);
        row.SenderEmail.Length.ShouldBe(150);
    }

    [Fact]
    public async Task Submit_to_an_unknown_center_fails_and_saves_nothing()
    {
        var subject = $"nocenter-{Guid.NewGuid():N}";

        await ShouldFailWith("Center.NotFound",
            () => NewFeedback().SubmitAsync(999_999, Pat.UserId, "Patient", "P", "p@test.local", Req(subject)));

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.Feedbacks.CountAsync(f => f.Subject == subject)).ShouldBe(0);
    }

    [Fact]
    public async Task Every_active_super_admin_is_notified_and_inactive_ones_are_not()
    {
        var a1 = await AddSuperAdminAsync();
        var a2 = await AddSuperAdminAsync();
        var off = await AddSuperAdminAsync(active: false);

        var r = await SubmitAsPatientAsync(Req("Cannot login", "help"));

        foreach (var id in new[] { a1, a2 })
        {
            var n = (await AdminNotificationsAsync(id)).ShouldHaveSingleItem();
            n.FeedbackId.ShouldBe(r.FeedbackId);
            n.IsRead.ShouldBeFalse();
            n.Message.ShouldContain("Cannot login");
            n.Message.ShouldContain("Pat Test");
        }
        (await AdminNotificationsAsync(off)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_notification_text_is_never_longer_than_300_characters()
    {
        var admin = await AddSuperAdminAsync();

        await NewFeedback().SubmitAsync(C.Id, Pat.UserId, "Patient", new string('n', 200), "p@test.local", Req(new string('s', 200), "m"));

        (await AdminNotificationsAsync(admin)).ShouldHaveSingleItem().Message.Length.ShouldBeLessThanOrEqualTo(300);
    }

    // ---------- submit (guest) ----------

    [Fact]
    public async Task A_guest_message_is_saved_without_a_user_and_with_trimmed_contact_details()
    {
        var req = GuestReq();
        req.Name = "  Visitor  "; req.Email = " visitor@test.local "; req.Phone = " 0591234567 ";

        var r = await NewFeedback().SubmitGuestAsync(req);

        var row = await RowAsync(r.FeedbackId);
        row.SenderUserId.ShouldBeNull();
        row.SenderRole.ShouldBeNull();
        row.CenterId.ShouldBeNull();
        row.SenderName.ShouldBe("Visitor");
        row.SenderEmail.ShouldBe("visitor@test.local");
        row.SenderPhone.ShouldBe("0591234567");
        row.Status.ShouldBe(FeedbackStatus.New);
    }

    [Fact]
    public async Task A_guest_may_name_a_center_but_it_must_exist()
    {
        var ok = await NewFeedback().SubmitGuestAsync(GuestReq(C.Id));
        (await RowAsync(ok.FeedbackId)).CenterId.ShouldBe(C.Id);

        await ShouldFailWith("Center.NotFound", () => NewFeedback().SubmitGuestAsync(GuestReq(999_999)));
    }

    [Fact]
    public async Task A_guest_message_also_notifies_the_super_admins()
    {
        var admin = await AddSuperAdminAsync();

        var r = await NewFeedback().SubmitGuestAsync(GuestReq(subject: "Guest question"));

        (await AdminNotificationsAsync(admin)).ShouldHaveSingleItem().FeedbackId.ShouldBe(r.FeedbackId);
    }

    // ---------- my messages ----------

    [Fact]
    public async Task My_messages_are_only_mine_newest_first()
    {
        var other = await C.AddPatientAsync();
        var first = await SubmitAsPatientAsync(Req("first"));
        await Task.Delay(30);
        var second = await SubmitAsPatientAsync(Req("second"));
        await NewFeedback().SubmitAsync(C.Id, other.UserId, "Patient", "Other", "o@test.local", Req("not mine"));

        var page = await NewFeedback().GetMyFeedbacksAsync(C.Id, Pat.UserId, new PagedRequest());

        page.Items.Select(i => i.FeedbackId).ShouldBe(new[] { second.FeedbackId, first.FeedbackId });
        page.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task User_ids_are_per_center_so_the_same_number_in_another_center_sees_nothing()
    {
        await SubmitAsPatientAsync();
        var otherCenter = await Fx.Harness.CreateCenterAsync();

        var page = await NewFeedback().GetMyFeedbacksAsync(otherCenter.Id, Pat.UserId, new PagedRequest());

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task My_messages_are_paged_and_show_the_reply()
    {
        var admin = await AddSuperAdminAsync();
        var a = await SubmitAsPatientAsync(Req("a"));
        await SubmitAsPatientAsync(Req("b"));
        await SubmitAsPatientAsync(Req("c"));
        await NewFeedback().ReplyAsync(a.FeedbackId, admin, new ReplyFeedbackRequestDto { Reply = "Done" });

        var p1 = await NewFeedback().GetMyFeedbacksAsync(C.Id, Pat.UserId, new PagedRequest { PageNumber = 1, PageSize = 2 });
        var p2 = await NewFeedback().GetMyFeedbacksAsync(C.Id, Pat.UserId, new PagedRequest { PageNumber = 2, PageSize = 2 });

        p1.Items.Count.ShouldBe(2);
        p2.Items.Count.ShouldBe(1);
        p2.Items.Single().Reply.ShouldBe("Done");
        p2.Items.Single().RepliedAt.ShouldNotBeNull();
    }

    // ---------- super admin: list / status ----------

    [Fact]
    public async Task The_admin_list_filters_by_status_type_and_center_and_shows_who_sent_it()
    {
        var bug = await SubmitAsPatientAsync(Req("bug", type: FeedbackType.Bug));
        var help = await SubmitAsPatientAsync(Req("help", type: FeedbackType.Help));
        await NewFeedback().UpdateStatusAsync(help.FeedbackId, FeedbackStatus.InProgress);
        var guest = await NewFeedback().SubmitGuestAsync(GuestReq(C.Id));

        var all = await NewFeedback().GetFeedbacksAsync(new PagedRequest(), null, null, C.Id);
        var inProgress = await NewFeedback().GetFeedbacksAsync(new PagedRequest(), FeedbackStatus.InProgress, null, C.Id);
        var bugs = await NewFeedback().GetFeedbacksAsync(new PagedRequest(), null, FeedbackType.Bug, C.Id);
        var elsewhere = await NewFeedback().GetFeedbacksAsync(new PagedRequest(), null, null, 999_999);

        all.Items.Select(i => i.FeedbackId).ShouldBe(new[] { bug.FeedbackId, help.FeedbackId, guest.FeedbackId }, ignoreOrder: true);
        all.Items.ShouldAllBe(i => i.CenterName == "Test Center");
        all.Items.Single(i => i.FeedbackId == guest.FeedbackId).IsGuest.ShouldBeTrue();
        all.Items.Single(i => i.FeedbackId == bug.FeedbackId).IsGuest.ShouldBeFalse();
        all.Items.Single(i => i.FeedbackId == bug.FeedbackId).SenderEmail.ShouldBe("pat@test.local");
        inProgress.Items.Select(i => i.FeedbackId).ShouldBe(new[] { help.FeedbackId });
        bugs.Items.Select(i => i.FeedbackId).ShouldBe(new[] { bug.FeedbackId });
        elsewhere.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_admin_list_is_newest_first_and_paged()
    {
        var a = await SubmitAsPatientAsync(Req("a"));
        await Task.Delay(30);
        var b = await SubmitAsPatientAsync(Req("b"));
        await Task.Delay(30);
        var c = await SubmitAsPatientAsync(Req("c"));

        var p1 = await NewFeedback().GetFeedbacksAsync(new PagedRequest { PageNumber = 1, PageSize = 2 }, null, null, C.Id);
        var p2 = await NewFeedback().GetFeedbacksAsync(new PagedRequest { PageNumber = 2, PageSize = 2 }, null, null, C.Id);

        p1.Items.Select(i => i.FeedbackId).ShouldBe(new[] { c.FeedbackId, b.FeedbackId });
        p2.Items.Select(i => i.FeedbackId).ShouldBe(new[] { a.FeedbackId });
        p1.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task Changing_the_status_stamps_the_update_time()
    {
        var r = await SubmitAsPatientAsync();

        var dto = await NewFeedback().UpdateStatusAsync(r.FeedbackId, FeedbackStatus.InProgress);

        dto.Status.ShouldBe("InProgress");
        dto.UpdatedAt.ShouldNotBeNull();
        var row = await RowAsync(r.FeedbackId);
        row.Status.ShouldBe(FeedbackStatus.InProgress);
        row.UpdatedAt.ShouldNotBeNull();
        row.Reply.ShouldBeNull();
    }

    [Fact]
    public async Task Status_and_reply_on_an_unknown_message_fail()
    {
        await ShouldFailWith("Feedback.NotFound", () => NewFeedback().UpdateStatusAsync(999_999, FeedbackStatus.Resolved));
        await ShouldFailWith("Feedback.NotFound", () => NewFeedback().ReplyAsync(999_999, 1, new ReplyFeedbackRequestDto { Reply = "x" }));
    }

    // ---------- super admin: reply ----------

    [Fact]
    public async Task A_reply_is_saved_with_who_and_when_and_resolves_the_message_by_default()
    {
        var admin = await AddSuperAdminAsync();
        var r = await SubmitAsPatientAsync();

        var dto = await NewFeedback().ReplyAsync(r.FeedbackId, admin, new ReplyFeedbackRequestDto { Reply = "  Fixed, thanks  " });

        dto.Reply.ShouldBe("Fixed, thanks");
        dto.Status.ShouldBe("Resolved");
        var row = await RowAsync(r.FeedbackId);
        row.Reply.ShouldBe("Fixed, thanks");
        row.RepliedBySuperAdminId.ShouldBe(admin);
        row.RepliedAt.ShouldNotBeNull();
        row.Status.ShouldBe(FeedbackStatus.Resolved);
    }

    [Fact]
    public async Task A_reply_may_keep_the_message_open()
    {
        var admin = await AddSuperAdminAsync();
        var r = await SubmitAsPatientAsync();

        await NewFeedback().ReplyAsync(r.FeedbackId, admin, new ReplyFeedbackRequestDto { Reply = "Looking", Status = FeedbackStatus.InProgress });

        (await RowAsync(r.FeedbackId)).Status.ShouldBe(FeedbackStatus.InProgress);
    }

    [Fact]
    public async Task A_signed_in_sender_gets_an_in_app_notification_and_no_email()
    {
        var admin = await AddSuperAdminAsync();
        var r = await SubmitAsPatientAsync(Req("Printer problem"));

        await NewFeedback().ReplyAsync(r.FeedbackId, admin, new ReplyFeedbackRequestDto { Reply = "Fixed" });

        await using var db = Db();
        var n = await db.Notifications.AsNoTracking().SingleAsync(x => x.UserId == Pat.UserId);
        n.Type.ShouldBe(NotificationType.Feedback);
        n.IsRead.ShouldBeFalse();
        n.Message.ShouldContain("Printer problem");
        await Email.DidNotReceiveWithAnyArgs().SendCustomMessageAsync(default!, default!, default!);
    }

    [Fact]
    public async Task A_guest_gets_the_reply_by_email_and_no_notification_row()
    {
        var admin = await AddSuperAdminAsync();
        var r = await NewFeedback().SubmitGuestAsync(GuestReq(C.Id, "Guest question"));

        await NewFeedback().ReplyAsync(r.FeedbackId, admin, new ReplyFeedbackRequestDto { Reply = "Use the register button" });

        await Email.Received(1).SendCustomMessageAsync("visitor@test.local",
            Arg.Is<string>(s => s.Contains("Guest question")), "Use the register button");
        await using var db = Db();
        (await db.Notifications.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_delivery_failure_does_not_lose_the_reply()
    {
        Email.SendCustomMessageAsync(default!, default!, default!)
             .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("smtp down")));
        var admin = await AddSuperAdminAsync();
        var r = await NewFeedback().SubmitGuestAsync(GuestReq());

        var dto = await NewFeedback().ReplyAsync(r.FeedbackId, admin, new ReplyFeedbackRequestDto { Reply = "Hello" });

        dto.Reply.ShouldBe("Hello");
        (await RowAsync(r.FeedbackId)).Reply.ShouldBe("Hello");
    }

    // ---------- super admin: notifications ----------

    [Fact]
    public async Task Each_admin_sees_only_their_own_notifications_with_a_correct_unread_count()
    {
        var a = await AddSuperAdminAsync();
        var b = await AddSuperAdminAsync();
        await SubmitAsPatientAsync(Req("one"));
        await SubmitAsPatientAsync(Req("two"));

        var page = await NewFeedback().GetSuperAdminNotificationsAsync(a, new PagedRequest());

        page.Items.Count.ShouldBe(2);
        page.Items.ShouldAllBe(n => !n.IsRead && n.FeedbackId != null);
        (await NewFeedback().GetUnreadSuperAdminNotificationsCountAsync(a)).ShouldBe(2);
        (await NewFeedback().GetUnreadSuperAdminNotificationsCountAsync(b)).ShouldBe(2);
        (await NewFeedback().GetUnreadSuperAdminNotificationsCountAsync(999_999)).ShouldBe(0);
    }

    [Fact]
    public async Task Marking_one_notification_read_lowers_the_count_by_one()
    {
        var a = await AddSuperAdminAsync();
        await SubmitAsPatientAsync(Req("one"));
        await SubmitAsPatientAsync(Req("two"));
        var first = (await AdminNotificationsAsync(a)).First();

        await NewFeedback().MarkSuperAdminNotificationReadAsync(a, first.Id);

        (await NewFeedback().GetUnreadSuperAdminNotificationsCountAsync(a)).ShouldBe(1);
    }

    [Fact]
    public async Task An_admin_cannot_mark_another_admins_notification()
    {
        var a = await AddSuperAdminAsync();
        var b = await AddSuperAdminAsync();
        await SubmitAsPatientAsync();
        var aNotification = (await AdminNotificationsAsync(a)).Single();

        await ShouldFailWith("Notification.NotFound", () => NewFeedback().MarkSuperAdminNotificationReadAsync(b, aNotification.Id));
        await ShouldFailWith("Notification.NotFound", () => NewFeedback().MarkSuperAdminNotificationReadAsync(a, 999_999));

        (await AdminNotificationsAsync(a)).Single().IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task Mark_all_read_touches_only_that_admin()
    {
        var a = await AddSuperAdminAsync();
        var b = await AddSuperAdminAsync();
        await SubmitAsPatientAsync(Req("one"));
        await SubmitAsPatientAsync(Req("two"));

        await NewFeedback().MarkAllSuperAdminNotificationsReadAsync(a);

        (await NewFeedback().GetUnreadSuperAdminNotificationsCountAsync(a)).ShouldBe(0);
        (await NewFeedback().GetUnreadSuperAdminNotificationsCountAsync(b)).ShouldBe(2);
    }
}
