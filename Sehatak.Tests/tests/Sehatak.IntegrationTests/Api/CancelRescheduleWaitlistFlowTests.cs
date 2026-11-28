using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// E2E batch 4: when plans change. The doctor blocks a slot or a whole day, patients cancel and reschedule,
/// the waiting list fills the gaps. Everything over real HTTP; the starting situation is seeded in the database.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CancelRescheduleWaitlistFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private static DateOnly Today => ClinicClock.Today;
    private int _receptionistId;
    private string RecToken => ReceptionistToken(_receptionistId);
    private string PatientToken(PatientInfo p) => Token("Patient", C.Id, p.UserId);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _receptionistId = await C.AddReceptionistAsync();
    }

    private static async Task Ok(HttpResponseMessage r) => r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
    private static void Rejected(HttpResponseMessage r) => ((int)r.StatusCode).ShouldBeInRange(400, 499);

    private async Task<(PatientInfo P, int Appt)> SeedAsync(TimeOnly slot, AppointmentStatus s = AppointmentStatus.Confirmed, DoctorInfo? doctor = null, DateOnly? date = null)
    {
        var p = await C.AddPatientAsync();
        return (p, await C.AddAppointmentAsync((doctor ?? Doc).DoctorId, p.PatientId, date ?? Date, slot, s));
    }

    private Task<HttpResponseMessage> BlockSlot(string slot, string? date = null, string? token = null, string? reason = "doctor is away") =>
        PostAsync($"/Appointment/doctor-cancel-slot/{C.Id}", new { timeSlot = slot, date = date ?? DateStr, reason }, token ?? DoctorToken);

    private Task<HttpResponseMessage> CancelDay(string? date = null, string? token = null) =>
        SendAsync(HttpMethod.Put, $"/DoctorDailyHours/doctor-Cancel-doctor-day/{C.Id}?date={date ?? DateStr}", token ?? DoctorToken);

    private Task<HttpResponseMessage> PatientCancel(PatientInfo p, string slot, string? date = null) =>
        PostAsync($"/Appointment/patient-cancel-appointment/{C.Id}/{Doc.DoctorId}", new { timeSlot = slot, date = date ?? DateStr }, PatientToken(p));

    private Task<HttpResponseMessage> PatientMove(PatientInfo p, int appt, string slot, string? date = null) =>
        PostAsync($"/Appointment/patient-reschedule-appointment/{C.Id}/{Doc.DoctorId}",
            new { appointmentId = appt, timeSlot = slot, date = date ?? DateStr }, PatientToken(p));

    private async Task FillWithFillersAsync(params TimeOnly[] slots)
    {
        foreach (var s in slots) await SeedAsync(s);
    }

    private static async Task<bool> SucceededAsync(HttpResponseMessage r) =>
        r.StatusCode == HttpStatusCode.OK && JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("success").GetBoolean();

    // ---------- doctor blocks one slot ----------

    [Fact]
    public async Task Blocking_a_booked_slot_cancels_it_tells_the_patient_and_keeps_waiters_waiting()
    {
        var (p, appt) = await SeedAsync(S0900);
        await FillWithFillersAsync(S0930, S1000, S1030);
        var waiter = await C.AddPatientAsync();
        await Ok(await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(waiter), DateStr));

        await Ok(await BlockSlot("09:00:00", reason: "emergency"));

        await using var db = Db();
        var a = await db.Appointments.SingleAsync(x => x.Id == appt);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        a.cancellationReason.ShouldBe("emergency");
        (await db.Notifications.AnyAsync(n => n.UserId == p.UserId && n.Type == NotificationType.Cancellation)).ShouldBeTrue();
        var postponed = await db.PostponedServices.SingleAsync(x => x.AppointmentId == appt);
        postponed.Status.ShouldBe(PostponeStatus.Active);
        postponed.PatientId.ShouldBe(p.PatientId);

        // a blocked slot is not offered to the waiting list and cannot be booked
        (await db.Waitlists.SingleAsync(w => w.PatientId == waiter.PatientId)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await FreeSlotsAsync()).ShouldNotContain("09:00");
        var rebook = await BookRaw(PatientToken(await C.AddPatientAsync()), "09:00:00");
        (await SucceededAsync(rebook)).ShouldBeFalse();
    }

    [Fact]
    public async Task Blocking_a_slot_has_rules()
    {
        await Ok(await BlockSlot("10:00:00"));
        Rejected(await BlockSlot("10:00:00"));                                              // already blocked
        Rejected(await BlockSlot("10:30:00", date: Today.AddDays(-2).ToString("yyyy-MM-dd"))); // past
        Rejected(await BlockSlot("10:30:00", date: Date.AddDays(1).ToString("yyyy-MM-dd")));   // doctor does not work that day

        (await BlockSlot("10:30:00", token: RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await BlockSlot("10:30:00", token: PatientToken(await C.AddPatientAsync()))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PostAsync($"/Appointment/doctor-cancel-slot/{C.Id}", new { timeSlot = "10:30:00", date = DateStr })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var db = Db();
        (await db.DoctorBlockedDays.CountAsync(b => b.doctorId == Doc.DoctorId && b.date == Date)).ShouldBe(1);
    }

    // ---------- doctor cancels the whole day ----------

    [Fact]
    public async Task Cancelling_the_day_cancels_the_waiting_patients_and_closes_the_day()
    {
        var other = await C.AddDoctorAsync();
        await C.AddScheduleAsync(other.DoctorId, Date.DayOfWeek, S0900, new TimeOnly(11, 0), 30);
        var one = await SeedAsync(S0900);
        var two = await SeedAsync(S0930);
        var done = await SeedAsync(S1000, AppointmentStatus.Completed);
        var othersPatient = await SeedAsync(S0900, doctor: other);
        await FillWithFillersAsync(S1030);
        var waiter = await C.AddPatientAsync();
        await Ok(await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(waiter), DateStr));

        await Ok(await CancelDay());

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == one.Appt)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await db.Appointments.SingleAsync(a => a.Id == two.Appt)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await db.Appointments.SingleAsync(a => a.Id == done.Appt)).appointmentStatus.ShouldBe(AppointmentStatus.Completed);
        (await db.Appointments.SingleAsync(a => a.Id == othersPatient.Appt)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);

        foreach (var p in new[] { one.P, two.P })
        {
            (await db.Notifications.AnyAsync(n => n.UserId == p.UserId && n.Type == NotificationType.Cancellation)).ShouldBeTrue();
            (await db.PostponedServices.AnyAsync(x => x.PatientId == p.PatientId && x.Status == PostponeStatus.Active)).ShouldBeTrue();
        }
        (await db.PostponedServices.AnyAsync(x => x.PatientId == done.P.PatientId)).ShouldBeFalse();

        (await db.Waitlists.SingleAsync(w => w.PatientId == waiter.PatientId)).Status.ShouldBe(WaitlistStatus.Exited);
        (await db.Notifications.AnyAsync(n => n.UserId == waiter.UserId)).ShouldBeTrue();

        // the day is closed for this doctor only
        (await FreeSlotsAsync()).ShouldBeEmpty();
        var late = await BookRaw(PatientToken(await C.AddPatientAsync()), "10:00:00");
        (await SucceededAsync(late)).ShouldBeFalse();
        (await db.DoctorBlockedDays.CountAsync(b => b.doctorId == other.DoctorId)).ShouldBe(0);

        Rejected(await CancelDay());                                                        // twice
    }

    [Fact]
    public async Task Cancelling_the_day_has_rules()
    {
        Rejected(await CancelDay(date: Today.AddDays(-1).ToString("yyyy-MM-dd")));
        Rejected(await CancelDay(date: Date.AddDays(1).ToString("yyyy-MM-dd")));            // no schedule that day
        (await CancelDay(token: RecToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await CancelDay(token: PatientToken(await C.AddPatientAsync()))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Put, $"/DoctorDailyHours/doctor-Cancel-doctor-day/{C.Id}?date={DateStr}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var db = Db();
        (await db.DoctorBlockedDays.CountAsync(b => b.doctorId == Doc.DoctorId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_booked_follow_up_goes_back_to_pending_with_more_time_when_the_doctor_cancels_the_day()
    {
        var p = await C.AddPatientAsync();
        var allowUntil = Date.AddDays(5);
        var followUpId = await C.AddFollowUpAsync(Doc.DoctorId, p.PatientId, allowUntil);
        await Ok(await PostAsync($"/Appointment/patient-book/{C.Id}/{Doc.DoctorId}",
            new { timeSlot = "09:00:00", dateOnly = DateStr, followUpId }, PatientToken(p)));
        await using (var db0 = Db())
            (await db0.FollowUps.SingleAsync(f => f.Id == followUpId)).Status.ShouldBe(FollowUpStatus.Booked);

        await Ok(await CancelDay());

        await using var db = Db();
        var f = await db.FollowUps.SingleAsync(x => x.Id == followUpId);
        f.Status.ShouldBe(FollowUpStatus.Pending);
        f.AllowFollowUpDate.ShouldBe(allowUntil.AddDays(8));
    }

    // ---------- patient cancels ----------

    [Fact]
    public async Task A_patient_can_only_cancel_a_confirmed_appointment_that_exists_once()
    {
        var upcoming = await SeedAsync(S0900);
        var finished = await SeedAsync(S0930, AppointmentStatus.Completed);

        Rejected(await PatientCancel(finished.P, "09:30:00"));                             // already done
        Rejected(await PatientCancel(upcoming.P, "10:00:00"));                             // wrong slot
        Rejected(await PatientCancel(upcoming.P, "09:00:00", date: Today.AddDays(-5).ToString("yyyy-MM-dd")));

        await Ok(await PatientCancel(upcoming.P, "09:00:00"));
        Rejected(await PatientCancel(upcoming.P, "09:00:00"));                             // twice

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == finished.Appt)).appointmentStatus.ShouldBe(AppointmentStatus.Completed);
        (await db.Notifications.CountAsync(n => n.UserId == upcoming.P.UserId && n.Type == NotificationType.Cancellation)).ShouldBe(1);
    }

    // ---------- patient reschedules ----------

    [Fact]
    public async Task Rescheduling_keeps_the_same_appointment_and_tells_the_patient()
    {
        var (p, appt) = await SeedAsync(S0900);

        var res = await PatientMove(p, appt, "10:30:00");
        (await SucceededAsync(res)).ShouldBeTrue();

        await using var db = Db();
        var a = await db.Appointments.SingleAsync(x => x.Id == appt);
        a.timeSlot.ShouldBe(S1030);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await db.Appointments.CountAsync(x => x.patientId == p.PatientId)).ShouldBe(1);
        (await db.Notifications.AnyAsync(n => n.UserId == p.UserId && n.Type == NotificationType.Appointment)).ShouldBeTrue();
    }

    [Fact]
    public async Task Rescheduling_to_a_taken_or_blocked_slot_is_refused_and_nothing_moves()
    {
        var (p, appt) = await SeedAsync(S0900);
        await SeedAsync(S0930);                                                              // taken
        await Ok(await BlockSlot("10:00:00"));                                               // blocked

        foreach (var slot in new[] { "09:30:00", "10:00:00" })
        {
            var res = await PatientMove(p, appt, slot);
            if (res.StatusCode == HttpStatusCode.OK) (await SucceededAsync(res)).ShouldBeFalse();
            else Rejected(res);
        }

        await using var db = Db();
        (await db.Appointments.SingleAsync(x => x.Id == appt)).timeSlot.ShouldBe(S0900);
    }

    [Fact]
    public async Task Rescheduling_has_rules()
    {
        var (p, appt) = await SeedAsync(S0900);
        var finished = await SeedAsync(S0930, AppointmentStatus.Completed);
        var cancelled = await SeedAsync(S1000, AppointmentStatus.Cancelled);

        Rejected(await PatientMove(p, appt, "10:30:00", date: Date.AddDays(1).ToString("yyyy-MM-dd")));   // doctor does not work that day
        Rejected(await PatientMove(p, appt, "10:30:00", date: Today.AddDays(-3).ToString("yyyy-MM-dd")));  // past
        Rejected(await PatientMove(finished.P, finished.Appt, "10:30:00"));                                // already completed
        Rejected(await PatientMove(cancelled.P, cancelled.Appt, "10:30:00"));                              // cancelled

        await Ok(await CancelDay());
        Rejected(await PatientMove(p, appt, "10:30:00"));                                                  // day is closed (and the appointment is cancelled)

        await using var db = Db();
        (await db.Appointments.SingleAsync(x => x.Id == finished.Appt)).timeSlot.ShouldBe(S0930);
    }

    // ---------- waiting list fills the gaps ----------

    [Fact]
    public async Task The_receptionist_cancelling_gives_the_gap_to_the_oldest_waiting_patient_who_is_free()
    {
        var holder = await SeedAsync(S0900);
        var alreadyHere = await SeedAsync(S0930);                                            // waits too, but already has a place this day
        await FillWithFillersAsync(S1000, S1030);
        var first = await C.AddPatientAsync();
        var second = await C.AddPatientAsync();

        foreach (var w in new[] { alreadyHere.P, first, second })
            await Ok(await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(w), DateStr));

        await Ok(await PostAsync($"/Appointment/receptionist-cancel-appointment/{C.Id}/{Doc.DoctorId}",
            new { patientId = holder.P.PatientId, timeSlot = "09:00:00", date = DateStr, resone = "called" }, RecToken));

        await using var db = Db();
        var got = await db.Appointments.SingleAsync(a => a.patientId == first.PatientId);
        got.timeSlot.ShouldBe(S0900);
        got.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await db.Waitlists.SingleAsync(w => w.PatientId == first.PatientId)).Status.ShouldBe(WaitlistStatus.Entered);
        (await db.Waitlists.SingleAsync(w => w.PatientId == second.PatientId)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await db.Waitlists.SingleAsync(w => w.PatientId == alreadyHere.P.PatientId)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await db.Appointments.CountAsync(a => a.patientId == alreadyHere.P.PatientId && a.appointmentStatus == AppointmentStatus.Confirmed)).ShouldBe(1);
        (await db.Notifications.AnyAsync(n => n.UserId == first.UserId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Each_cancellation_serves_one_waiting_patient_in_order()
    {
        var a = await SeedAsync(S0900);
        var b = await SeedAsync(S0930);
        await FillWithFillersAsync(S1000, S1030);
        var first = await C.AddPatientAsync();
        var second = await C.AddPatientAsync();
        foreach (var w in new[] { first, second })
            await Ok(await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(w), DateStr));

        await Ok(await PatientCancel(a.P, "09:00:00"));
        await using (var db1 = Db())
        {
            (await db1.Waitlists.SingleAsync(w => w.PatientId == first.PatientId)).Status.ShouldBe(WaitlistStatus.Entered);
            (await db1.Waitlists.SingleAsync(w => w.PatientId == second.PatientId)).Status.ShouldBe(WaitlistStatus.Waiting);
        }

        await Ok(await PatientCancel(b.P, "09:30:00"));
        await using var db = Db();
        (await db.Waitlists.SingleAsync(w => w.PatientId == second.PatientId)).Status.ShouldBe(WaitlistStatus.Entered);
        (await db.Appointments.SingleAsync(x => x.patientId == second.PatientId)).timeSlot.ShouldBe(S0930);
    }

    [Fact]
    public async Task A_patient_sees_their_own_waiting_entry_and_others_have_none()
    {
        await FillWithFillersAsync(S0900, S0930, S1000, S1030);
        var waiting = await C.AddPatientAsync();
        var bystander = await C.AddPatientAsync();
        await Ok(await SendAsync(HttpMethod.Post, $"/Appointment/patient-join-waitlist/{C.Id}/{Doc.DoctorId}", PatientToken(waiting), DateStr));
        var url = $"/Appointment/patient-view-my-waitlist/{C.Id}/{Doc.DoctorId}?date={DateStr}";

        var mine = await OkJsonAsync(await GetAsync(url, PatientToken(waiting)));
        mine.ToString().ShouldContain($"\"patientId\":{waiting.PatientId}");

        // someone who never joined gets "not found", not somebody else's entry
        var theirs = await GetAsync(url, PatientToken(bystander));
        theirs.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
