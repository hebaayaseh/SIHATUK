using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>E2E batch 8b: the notification list, marking as read, and device tokens, as the apps use them.</summary>
[Collection(SqlServerCollection.Name)]
public class NotificationsFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private string PatientToken(PatientInfo p) => Token("Patient", C.Id, p.UserId);
    private string ListUrl(string q = "PageNumber=1&PageSize=10") => $"/Notification/get-notifications/{C.Id}?{q}";

    private async Task<int> SeedNotificationAsync(int userId, string message, DateTime? at = null, bool read = false)
    {
        await using var db = Db();
        var n = new Notification { UserId = userId, Message = message, Type = NotificationType.Appointment, IsRead = read, CreatedAt = at ?? DateTime.UtcNow };
        db.Notifications.Add(n);
        await db.SaveChangesAsync();
        return n.Id;
    }

    private static IEnumerable<JsonElement> Items(JsonElement list) => list.GetProperty("items").EnumerateArray();

    [Fact]
    public async Task A_real_event_reaches_the_patients_notification_list_and_can_be_marked_read()
    {
        var p = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, p.PatientId, Date, new TimeOnly(9, 0), AppointmentStatus.Confirmed);

        // the doctor blocks that slot: the patient must hear about it
        var block = await PostAsync($"/Appointment/doctor-cancel-slot/{C.Id}", new { timeSlot = "09:00:00", date = DateStr, reason = "emergency" }, DoctorToken);
        block.StatusCode.ShouldBe(HttpStatusCode.OK, await block.Content.ReadAsStringAsync());

        var list = await OkJsonAsync(await GetAsync(ListUrl(), PatientToken(p)));
        var cancellation = Items(list).Single(i => i.GetProperty("type").GetString() == "Cancellation");
        cancellation.GetProperty("isRead").GetBoolean().ShouldBeFalse();
        var id = cancellation.GetProperty("notificationId").GetInt32();

        var read = await OkJsonAsync(await SendAsync(HttpMethod.Put, $"/Notification/read-notifications/{C.Id}/{id}", PatientToken(p)));
        read.GetProperty("isRead").GetBoolean().ShouldBeTrue();

        var again = await OkJsonAsync(await GetAsync(ListUrl(), PatientToken(p)));
        Items(again).Single(i => i.GetProperty("notificationId").GetInt32() == id).GetProperty("isRead").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Everybody_sees_only_his_own_notifications_and_can_mark_only_his_own()
    {
        var a = await C.AddPatientAsync();
        var b = await C.AddPatientAsync();
        await SeedNotificationAsync(a.UserId, "for A only");
        var bId = await SeedNotificationAsync(b.UserId, "for B only");

        var listA = await OkJsonAsync(await GetAsync(ListUrl(), PatientToken(a)));
        JsonContains(listA, "for A only").ShouldBeTrue();
        JsonContains(listA, "for B only").ShouldBeFalse();

        // A cannot mark B's notification
        ((int)(await SendAsync(HttpMethod.Put, $"/Notification/read-notifications/{C.Id}/{bId}", PatientToken(a))).StatusCode).ShouldBe(400);
        await using var db = Db();
        (await db.Notifications.SingleAsync(n => n.Id == bId)).IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_is_newest_first_and_paged()
    {
        var p = await C.AddPatientAsync();
        for (var i = 1; i <= 5; i++)
            await SeedNotificationAsync(p.UserId, $"note-{i}", DateTime.UtcNow.AddMinutes(-60 + i));

        var first = await OkJsonAsync(await GetAsync(ListUrl("PageNumber=1&PageSize=2"), PatientToken(p)));
        Items(first).Select(i => i.GetProperty("message").GetString()).ToList().ShouldBe(new[] { "note-5", "note-4" });
        first.GetProperty("totalCount").GetInt32().ShouldBe(5);

        var third = await OkJsonAsync(await GetAsync(ListUrl("PageNumber=3&PageSize=2"), PatientToken(p)));
        Items(third).Select(i => i.GetProperty("message").GetString()).ToList().ShouldBe(new[] { "note-1" });
    }

    [Fact]
    public async Task Marking_twice_or_marking_a_missing_notification_is_safe()
    {
        var p = await C.AddPatientAsync();
        var id = await SeedNotificationAsync(p.UserId, "x");

        (await SendAsync(HttpMethod.Put, $"/Notification/read-notifications/{C.Id}/{id}", PatientToken(p))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Put, $"/Notification/read-notifications/{C.Id}/{id}", PatientToken(p))).StatusCode.ShouldBe(HttpStatusCode.OK);
        ((int)(await SendAsync(HttpMethod.Put, $"/Notification/read-notifications/{C.Id}/99999999", PatientToken(p))).StatusCode).ShouldBe(400);
    }

    [Fact]
    public async Task Staff_read_their_own_notifications_too()
    {
        await SeedNotificationAsync(Doc.UserId, "for the doctor");
        await SeedNotificationAsync(C.AdminUserId, "for the admin");

        JsonContains(await OkJsonAsync(await GetAsync(ListUrl(), DoctorToken)), "for the doctor").ShouldBeTrue();
        var admin = await OkJsonAsync(await GetAsync(ListUrl(), AdminToken));
        JsonContains(admin, "for the admin").ShouldBeTrue();
        JsonContains(admin, "for the doctor").ShouldBeFalse();
    }

    [Fact]
    public async Task A_device_token_belongs_to_one_user_at_a_time()
    {
        var a = await C.AddPatientAsync();
        var b = await C.AddPatientAsync();
        const string token = "fcm-device-token-123";

        (await PutToken(a, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using (var db = Db()) (await db.Users.SingleAsync(u => u.Id == a.UserId)).FcmToken.ShouldBe(token);

        // the same phone, a different account logs in: the token moves, so A stops getting B's pushes
        (await PutToken(b, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db2 = Db();
        (await db2.Users.SingleAsync(u => u.Id == b.UserId)).FcmToken.ShouldBe(token);
        (await db2.Users.SingleAsync(u => u.Id == a.UserId)).FcmToken.ShouldNotBe(token);
    }

    private Task<HttpResponseMessage> PutToken(PatientInfo p, string token) =>
        SendAsync(HttpMethod.Put, $"/Notification/register-device-token/{C.Id}", PatientToken(p), token);

    [Fact]
    public async Task Notification_doors_need_a_token_for_the_same_center()
    {
        var p = await C.AddPatientAsync();

        (await GetAsync(ListUrl())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync($"/Notification/get-notifications/{C.Id + 1}?PageNumber=1&PageSize=10", PatientToken(p))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"/Notification/register-device-token/{C.Id}", null, "x")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
