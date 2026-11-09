using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using NotificationSvc = Sehatak.Infrastructure.Services.NotificationService.NotificationService;

namespace Sehatak.IntegrationTests.Notifications;

/// <summary>The in-app inbox (list, mark as read) and the phone's push token.</summary>
[Collection(SqlServerCollection.Name)]
public class NotificationServiceTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static readonly PagedRequest Page = new() { PageNumber = 1, PageSize = 20 };

    private NotificationSvc NewSvc() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);

    private async Task<int> AddAsync(int userId, string message, DateTime createdAt, bool isRead = false,
                                     NotificationType type = NotificationType.System)
    {
        await using var db = Db();
        var n = new Notification { UserId = userId, Message = message, Type = type, CreatedAt = createdAt, IsRead = isRead };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    private async Task<Notification> RowAsync(int id)
    {
        await using var db = Db();
        return await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id);
    }

    private async Task<string?> TokenOfAsync(int userId)
    {
        await using var db = Db();
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).FcmToken;
    }

    // ---------- inbox ----------

    [Fact]
    public async Task The_inbox_has_only_my_notifications_newest_first_with_their_type_and_read_flag()
    {
        var now = DateTime.UtcNow;
        await AddAsync(Pat.UserId, "old", now.AddHours(-3), isRead: true, type: NotificationType.Reminder);
        await AddAsync(Pat.UserId, "new", now.AddHours(-1), type: NotificationType.Appointment);
        await AddAsync(Doc.UserId, "not mine", now);

        var r = await NewSvc().GetNotificatiosAsync(C.Id, Pat.UserId, Page);

        r.Items.Select(i => i.Message).ShouldBe(new[] { "new", "old" });
        r.Items[0].Type.ShouldBe("Appointment");
        r.Items[0].isRead.ShouldBeFalse();
        r.Items[1].Type.ShouldBe("Reminder");
        r.Items[1].isRead.ShouldBeTrue();
        r.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_inbox_is_paged()
    {
        var now = DateTime.UtcNow;
        for (var i = 1; i <= 5; i++) await AddAsync(Pat.UserId, $"n{i}", now.AddMinutes(-i));

        var p1 = await NewSvc().GetNotificatiosAsync(C.Id, Pat.UserId, new PagedRequest { PageNumber = 1, PageSize = 2 });
        var p3 = await NewSvc().GetNotificatiosAsync(C.Id, Pat.UserId, new PagedRequest { PageNumber = 3, PageSize = 2 });

        p1.Items.Select(i => i.Message).ShouldBe(new[] { "n1", "n2" });
        p3.Items.Select(i => i.Message).ShouldBe(new[] { "n5" });
        p1.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task An_empty_inbox_is_an_empty_page()
    {
        (await NewSvc().GetNotificatiosAsync(C.Id, Pat.UserId, Page)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_same_user_number_in_another_center_has_its_own_inbox()
    {
        var other = await Fx.Harness.CreateCenterAsync();
        await using (var db = Fx.Harness.Tenant(other.Id))
        {
            db.Notifications.Add(new Notification { UserId = other.AdminUserId, Message = "other center", Type = NotificationType.System });
            await db.SaveChangesAsync();
        }

        (await NewSvc().GetNotificatiosAsync(C.Id, C.AdminUserId, Page)).Items.ShouldBeEmpty();
        (await NewSvc().GetNotificatiosAsync(other.Id, other.AdminUserId, Page)).Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_inbox_needs_an_active_user_in_an_available_center()
    {
        var gone = await C.AddPatientAsync(active: false);
        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);

        await ShouldFailWith("User.NotFound", () => NewSvc().GetNotificatiosAsync(C.Id, gone.UserId, Page));
        await ShouldFailWith("User.NotFound", () => NewSvc().GetNotificatiosAsync(C.Id, 999_999, Page));
        await ShouldFailWith("Center.NotFound", () => NewSvc().GetNotificatiosAsync(999_999, Pat.UserId, Page));
        await ShouldFailWith("Center.NotFound", () => NewSvc().GetNotificatiosAsync(suspended.Id, suspended.AdminUserId, Page));
    }

    // ---------- mark as read ----------

    [Fact]
    public async Task Marking_one_notification_reads_only_that_one_and_returns_it()
    {
        var created = DateTime.UtcNow.AddHours(-1);
        var target = await AddAsync(Pat.UserId, "read me", created, type: NotificationType.LabResult);
        var sibling = await AddAsync(Pat.UserId, "leave me", created.AddMinutes(1));

        var r = await NewSvc().MarkNotificationReadAsync(C.Id, Pat.UserId, target);

        r.NotificationId.ShouldBe(target);
        r.isRead.ShouldBeTrue();
        r.Message.ShouldBe("read me");
        r.Type.ShouldBe("LabResult");
        (await RowAsync(target)).IsRead.ShouldBeTrue();
        (await RowAsync(sibling)).IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task Marking_an_already_read_notification_is_fine()
    {
        var id = await AddAsync(Pat.UserId, "x", DateTime.UtcNow, isRead: true);

        (await NewSvc().MarkNotificationReadAsync(C.Id, Pat.UserId, id)).isRead.ShouldBeTrue();
    }

    [Fact]
    public async Task Nobody_can_mark_someone_elses_notification()
    {
        var theirs = await AddAsync(Doc.UserId, "private", DateTime.UtcNow);

        await ShouldFailWith("Notification.NotFound", () => NewSvc().MarkNotificationReadAsync(C.Id, Pat.UserId, theirs));

        (await RowAsync(theirs)).IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task Marking_fails_for_an_unknown_notification_user_or_center()
    {
        var gone = await C.AddPatientAsync(active: false);
        var id = await AddAsync(gone.UserId, "x", DateTime.UtcNow);

        await ShouldFailWith("Notification.NotFound", () => NewSvc().MarkNotificationReadAsync(C.Id, Pat.UserId, 999_999));
        await ShouldFailWith("User.NotFound", () => NewSvc().MarkNotificationReadAsync(C.Id, gone.UserId, id));
        await ShouldFailWith("Center.NotFound", () => NewSvc().MarkNotificationReadAsync(999_999, Pat.UserId, id));
    }

    // ---------- push token ----------

    [Fact]
    public async Task The_phone_token_is_stored_and_a_new_one_replaces_it()
    {
        await NewSvc().RegisterDeviceTokenAsync(C.Id, Pat.UserId, "token-1");
        (await TokenOfAsync(Pat.UserId)).ShouldBe("token-1");

        await NewSvc().RegisterDeviceTokenAsync(C.Id, Pat.UserId, "token-2");
        (await TokenOfAsync(Pat.UserId)).ShouldBe("token-2");
    }

    [Fact]
    public async Task Registering_a_token_does_not_touch_other_users_with_other_tokens()
    {
        await NewSvc().RegisterDeviceTokenAsync(C.Id, Doc.UserId, "doctor-phone");

        await NewSvc().RegisterDeviceTokenAsync(C.Id, Pat.UserId, "patient-phone");

        (await TokenOfAsync(Doc.UserId)).ShouldBe("doctor-phone");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task One_phone_token_belongs_to_one_user_when_a_second_person_signs_in_on_the_same_phone()
    {
        // user A signs in on the phone, later user B signs in on the same phone: the token is now on both rows,
        // so everything meant for A (medication reminders, appointment news) is also pushed to B's screen
        await NewSvc().RegisterDeviceTokenAsync(C.Id, Pat.UserId, "shared-phone");

        await NewSvc().RegisterDeviceTokenAsync(C.Id, Doc.UserId, "shared-phone");

        (await TokenOfAsync(Doc.UserId)).ShouldBe("shared-phone");
        (await TokenOfAsync(Pat.UserId)).ShouldBeNull();
    }

    [Fact]
    public async Task Registering_a_token_fails_for_an_unknown_or_deactivated_user_and_unavailable_center()
    {
        var gone = await C.AddPatientAsync(active: false);

        await ShouldFailWith("User.NotFound", () => NewSvc().RegisterDeviceTokenAsync(C.Id, gone.UserId, "t"));
        await ShouldFailWith("User.NotFound", () => NewSvc().RegisterDeviceTokenAsync(C.Id, 999_999, "t"));
        await ShouldFailWith("Center.NotFound", () => NewSvc().RegisterDeviceTokenAsync(999_999, Pat.UserId, "t"));
    }
}
