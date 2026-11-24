using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>
/// The receptionist brings a waiting-list patient in: the entry becomes a Confirmed appointment (a free slot, or after the last
/// booked one when the day is full). Each test has its OWN doctor and schedule (09:00-12:00, 60-minute slots = 09:00, 10:00, 11:00)
/// on a day in the future, so the tests never meet each other's appointments. The "today" tests use the real clinic clock and
/// skip themselves close to midnight.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ReceptionistWaitlistTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static readonly TimeOnly T09 = new(9, 0), T10 = new(10, 0), T11 = new(11, 0), T12 = new(12, 0);

    private static DateOnly Today => ClinicClock.Today;
    private static DateOnly Future => ClinicClock.Today.AddDays(3);
    private static TimeOnly NowTime => TimeOnly.FromDateTime(ClinicClock.Now);
    private static bool NearMidnight => NowTime > new TimeOnly(21, 30) || NowTime < new TimeOnly(0, 40);

    private static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    /// <summary>A new doctor with a 09:00-12:00 schedule (60-minute slots) on the weekday of <paramref name="date"/>.</summary>
    private async Task<int> NewDoctorAsync(DateOnly? date = null, bool withSchedule = true)
    {
        var d = await C.AddDoctorAsync();
        if (withSchedule)
            await C.AddScheduleAsync(d.DoctorId, (date ?? Future).DayOfWeek, T09, T12, 60);
        return d.DoctorId;
    }

    private async Task<int> AddWaitlistAsync(int doctorId, int patientId, DateOnly date,
        WaitlistStatus status = WaitlistStatus.Waiting, DateTime? createdAt = null)
    {
        await using var db = Db();
        var w = new Waitlist
        {
            DoctorId = doctorId, PatientId = patientId, PreferredDate = date, Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        db.Waitlists.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    private Task<ReceptionistEnterFromWaitlistResponse> EnterAsync(int receptionistUserId, int doctorId, int waitListId, TimeOnly? slot = null) =>
        C.NewAppointmentService().ReceptionistEnterFromWaitlistAsync(C.Id, doctorId, receptionistUserId,
            new ReceptionistEnterFromWaitlistRequest { WaitListId = waitListId, TimeSlot = slot });

    private async Task<Waitlist> WaitAsync(int id)
    {
        await using var db = Db();
        return await db.Waitlists.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    private async Task<List<Appointment>> AppointmentsAsync(int doctorId, DateOnly date)
    {
        await using var db = Db();
        return await db.Appointments.AsNoTracking().Where(a => a.doctorId == doctorId && a.appointmentDate == date).ToListAsync();
    }

    private async Task BlockSlotAsync(int doctorId, DateOnly date, TimeOnly? slot)
    {
        await using var db = Db();
        db.DoctorBlockedDays.Add(new DoctorBlockedDay { doctorId = doctorId, date = date, timeSlot = slot, isBlocked = true });
        await db.SaveChangesAsync();
    }

    // ---------- choosing the time ----------

    [Fact]
    public async Task Without_a_chosen_time_the_patient_takes_the_first_free_slot_and_everything_is_recorded()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var p = await C.AddPatientAsync();
        await C.AddAppointmentAsync(doc, (await C.AddPatientAsync()).PatientId, Future, T09);
        var wid = await AddWaitlistAsync(doc, p.PatientId, Future);

        var r = await EnterAsync(rec, doc, wid);

        r.Success.ShouldBeTrue();
        r.TimeSlot.ShouldBe(T10);
        r.Date.ShouldBe(Future);
        r.IsOverflow.ShouldBeFalse();
        r.CheckedIn.ShouldBeFalse("the visit is on another day, so nobody is in the waiting room yet");

        await using var db = Db();
        var a = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == r.AppointmentId);
        a.patientId.ShouldBe(p.PatientId);
        a.doctorId.ShouldBe(doc);
        a.timeSlot.ShouldBe(T10);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        a.ReceptionistId.ShouldBe(rec);
        a.CheckInTime.ShouldBeNull();

        var w = await WaitAsync(wid);
        w.Status.ShouldBe(WaitlistStatus.Entered);
        w.PromotedAppointmentId.ShouldBe(r.AppointmentId);
        w.ReceptionistId.ShouldBe(rec);

        (await db.Notifications.CountAsync(n => n.UserId == p.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(l => l.Action == "EnterFromWaitlist" && l.EntityId == r.AppointmentId)).ShouldBe(1);
    }

    [Fact]
    public async Task A_cancelled_appointment_does_not_hold_its_slot()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        await C.AddAppointmentAsync(doc, (await C.AddPatientAsync()).PatientId, Future, T09, AppointmentStatus.Cancelled);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        (await EnterAsync(rec, doc, wid)).TimeSlot.ShouldBe(T09);
    }

    [Fact]
    public async Task A_slot_blocked_by_the_doctor_is_skipped()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        await BlockSlotAsync(doc, Future, T09);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        (await EnterAsync(rec, doc, wid)).TimeSlot.ShouldBe(T10);
    }

    [Fact]
    public async Task The_receptionist_can_choose_the_time()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        var r = await EnterAsync(rec, doc, wid, T11);

        r.TimeSlot.ShouldBe(T11);
        r.IsOverflow.ShouldBeFalse();
    }

    [Fact]
    public async Task A_chosen_time_that_is_already_booked_is_refused_and_the_patient_stays_on_the_list()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        await C.AddAppointmentAsync(doc, (await C.AddPatientAsync()).PatientId, Future, T10);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("Appointment.SlotTaken", () => EnterAsync(rec, doc, wid, T10));

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await AppointmentsAsync(doc, Future)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_chosen_time_blocked_by_the_doctor_is_refused()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        await BlockSlotAsync(doc, Future, T10);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("Appointment.SlotTaken", () => EnterAsync(rec, doc, wid, T10));
    }

    [Theory]
    [InlineData(9, 7)]     // between two slots
    [InlineData(8, 0)]     // before the doctor starts
    [InlineData(12, 0)]    // after the last slot (a 12:00 slot would end at 13:00)
    public async Task A_chosen_time_outside_the_doctors_slots_is_refused(int hour, int minute)
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("Waitlist.InvalidSlot", () => EnterAsync(rec, doc, wid, new TimeOnly(hour, minute)));

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task When_the_day_is_full_the_patient_goes_after_the_last_booked_slot()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        foreach (var t in new[] { T09, T10, T11 })
            await C.AddAppointmentAsync(doc, (await C.AddPatientAsync()).PatientId, Future, t);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        var r = await EnterAsync(rec, doc, wid);

        r.TimeSlot.ShouldBe(T12);
        r.IsOverflow.ShouldBeTrue();
        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    [Fact]
    public async Task Two_patients_brought_in_one_after_the_other_on_a_full_day_get_two_different_overflow_times()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        foreach (var t in new[] { T09, T10, T11 })
            await C.AddAppointmentAsync(doc, (await C.AddPatientAsync()).PatientId, Future, t);
        var w1 = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);
        var w2 = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        var r1 = await EnterAsync(rec, doc, w1);
        var r2 = await EnterAsync(rec, doc, w2);

        r1.TimeSlot.ShouldBe(T12);
        r2.TimeSlot.ShouldBe(new TimeOnly(13, 0));
    }

    [Fact]
    public async Task When_every_slot_of_the_day_is_blocked_and_nothing_is_booked_the_overflow_skips_the_blocked_ones()
    {
        // 09:00, 10:00 and 11:00 are all blocked and nothing is booked
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        foreach (var t in new[] { T09, T10, T11 }) await BlockSlotAsync(doc, Future, t);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        var r = await EnterAsync(rec, doc, wid);

        r.TimeSlot.ShouldBe(T12, "09:00-11:00 are blocked, so the first open time after them is 12:00");
        r.IsOverflow.ShouldBeTrue();
    }

    // ---------- today ----------

    [Fact]
    public async Task A_waiting_list_patient_for_today_is_checked_in_at_once()
    {
        if (NearMidnight) return;
        var rec = await C.AddReceptionistAsync();
        var d = await C.AddDoctorAsync();
        await C.AddScheduleAsync(d.DoctorId, Today.DayOfWeek, new TimeOnly(0, 0), new TimeOnly(23, 59), 60);
        var p = await C.AddPatientAsync();
        var wid = await AddWaitlistAsync(d.DoctorId, p.PatientId, Today);
        var before = NowTime;

        var r = await EnterAsync(rec, d.DoctorId, wid);

        r.CheckedIn.ShouldBeTrue();
        r.IsOverflow.ShouldBeFalse();
        r.TimeSlot.ShouldBeGreaterThanOrEqualTo(before, "a slot that has already passed is not offered");
        await using var db = Db();
        (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == r.AppointmentId)).CheckInTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_chosen_time_that_has_already_passed_today_is_refused()
    {
        if (NearMidnight || NowTime < new TimeOnly(2, 0)) return;
        var rec = await C.AddReceptionistAsync();
        var d = await C.AddDoctorAsync();
        await C.AddScheduleAsync(d.DoctorId, Today.DayOfWeek, new TimeOnly(0, 0), new TimeOnly(23, 59), 60);
        var wid = await AddWaitlistAsync(d.DoctorId, (await C.AddPatientAsync()).PatientId, Today);

        await ShouldFailWith("Waitlist.InvalidSlot", () => EnterAsync(rec, d.DoctorId, wid, new TimeOnly(0, 0)));
    }

    // ---------- who and what ----------

    [Theory]
    [InlineData(WaitlistStatus.Entered)]
    [InlineData(WaitlistStatus.Cancelled)]
    [InlineData(WaitlistStatus.Exited)]
    public async Task Only_a_patient_who_is_still_waiting_can_be_brought_in(WaitlistStatus status)
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future, status);

        await ShouldFailWith("Waitlist.NotWaiting", () => EnterAsync(rec, doc, wid));

        (await AppointmentsAsync(doc, Future)).ShouldBeEmpty();
        (await WaitAsync(wid)).Status.ShouldBe(status);
    }

    [Fact]
    public async Task An_unknown_entry_or_an_entry_of_another_doctor_is_not_found()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var other = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(other, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("WaitList.NotFound", () => EnterAsync(rec, doc, 999_999));
        await ShouldFailWith("WaitList.NotFound", () => EnterAsync(rec, doc, wid));

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task An_entry_of_a_past_day_cannot_be_brought_in()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync(Today.AddDays(-1));
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Today.AddDays(-1));

        await ShouldFailWith("Date.Invalid", () => EnterAsync(rec, doc, wid));
    }

    [Fact]
    public async Task A_day_the_doctor_blocked_completely_is_refused()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        await BlockSlotAsync(doc, Future, null);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("Doctor.DayBlocked", () => EnterAsync(rec, doc, wid));

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task A_doctor_who_does_not_work_that_weekday_is_refused()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync(withSchedule: false);
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("Schedule.NotFound", () => EnterAsync(rec, doc, wid));
    }

    [Fact]
    public async Task A_patient_who_already_has_an_appointment_with_that_doctor_that_day_is_refused()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var p = await C.AddPatientAsync();
        await C.AddAppointmentAsync(doc, p.PatientId, Future, T09);
        var wid = await AddWaitlistAsync(doc, p.PatientId, Future);

        await ShouldFailWith("Appointment.AlreadyExists", () => EnterAsync(rec, doc, wid));

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await AppointmentsAsync(doc, Future)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_cancelled_appointment_of_the_same_patient_does_not_stop_him()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var p = await C.AddPatientAsync();
        await C.AddAppointmentAsync(doc, p.PatientId, Future, T09, AppointmentStatus.Cancelled);
        var wid = await AddWaitlistAsync(doc, p.PatientId, Future);

        (await EnterAsync(rec, doc, wid)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Only_an_active_receptionist_can_do_it()
    {
        var doc = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);
        var inactive = await C.AddReceptionistAsync();
        await using (var db = Db())
            await db.Users.Where(u => u.Id == inactive).ExecuteUpdateAsync(s => s.SetProperty(u => u.isActive, false));

        await ShouldFailWith("Receptionist.NotFound", () => EnterAsync(inactive, doc, wid));
        await ShouldFailWith("Receptionist.NotFound", () => EnterAsync(Doc.UserId, doc, wid));      // a doctor is not a receptionist

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task An_unknown_center_is_refused()
    {
        var rec = await C.AddReceptionistAsync();
        await ShouldFailWith("Center.NotFound", () => C.NewAppointmentService().ReceptionistEnterFromWaitlistAsync(
            999_999, Doc.DoctorId, rec, new ReceptionistEnterFromWaitlistRequest { WaitListId = 1 }));
    }

    // ---------- the same moment ----------

    [Fact]
    public async Task Two_receptionists_bringing_in_the_same_patient_at_once_create_one_appointment()
    {
        var rec1 = await C.AddReceptionistAsync();
        var rec2 = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        var results = await Task.WhenAll(new[] { rec1, rec2 }.Select(r => Task.Run(async () =>
        {
            try { await EnterAsync(r, doc, wid); return "ok"; }
            catch (BusinessException e) { return e.Message; }      // any other exception type = a crash, and fails the test
        })));

        results.Count(x => x == "ok").ShouldBe(1);
        (await AppointmentsAsync(doc, Future)).Count.ShouldBe(1);
        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    [Fact]
    public async Task Two_different_patients_brought_in_at_once_never_share_a_slot_and_the_loser_stays_on_the_list()
    {
        var rec1 = await C.AddReceptionistAsync();
        var rec2 = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var w1 = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);
        var w2 = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        var results = await Task.WhenAll(new[] { (rec1, w1), (rec2, w2) }.Select(x => Task.Run(async () =>
        {
            try { await EnterAsync(x.Item1, doc, x.Item2); return (x.Item2, "ok"); }
            catch (BusinessException e) { return (x.Item2, e.Message); }
        })));

        var appointments = await AppointmentsAsync(doc, Future);
        var winners = results.Where(r => r.Item2 == "ok").Select(r => r.Item1).ToList();
        appointments.Count.ShouldBe(winners.Count);
        appointments.Select(a => a.timeSlot).Distinct().Count().ShouldBe(appointments.Count, "two patients must never get the same slot");
        foreach (var loser in results.Where(r => r.Item2 != "ok"))
        {
            loser.Item2.ShouldBe("Appointment.SlotTaken");
            (await WaitAsync(loser.Item1)).Status.ShouldBe(WaitlistStatus.Waiting, "a lost race is undone completely");
        }
    }

    // ---------- removing from the list ----------

    [Fact]
    public async Task The_receptionist_removes_a_waiting_patient_from_the_list()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        var msg = await C.NewAppointmentService().ReceptionistRemoveFromWaitlistAsync(C.Id, doc, rec, wid);

        msg.ShouldNotBeNullOrWhiteSpace();
        var w = await WaitAsync(wid);
        w.Status.ShouldBe(WaitlistStatus.Cancelled);
        w.ReceptionistId.ShouldBe(rec);
        await using var db = Db();
        (await db.AuditLogs.CountAsync(l => l.Action == "RemoveFromWaitlist" && l.EntityId == wid)).ShouldBe(1);
    }

    [Fact]
    public async Task Removing_twice_or_removing_someone_who_already_entered_is_refused()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var gone = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);
        var entered = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future, WaitlistStatus.Entered);
        await C.NewAppointmentService().ReceptionistRemoveFromWaitlistAsync(C.Id, doc, rec, gone);

        await ShouldFailWith("Waitlist.NotWaiting", () => C.NewAppointmentService().ReceptionistRemoveFromWaitlistAsync(C.Id, doc, rec, gone));
        await ShouldFailWith("Waitlist.NotWaiting", () => C.NewAppointmentService().ReceptionistRemoveFromWaitlistAsync(C.Id, doc, rec, entered));

        (await WaitAsync(entered)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    [Fact]
    public async Task An_unknown_entry_or_an_entry_of_another_doctor_cannot_be_removed()
    {
        var rec = await C.AddReceptionistAsync();
        var doc = await NewDoctorAsync();
        var other = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(other, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("WaitList.NotFound", () => C.NewAppointmentService().ReceptionistRemoveFromWaitlistAsync(C.Id, doc, rec, 999_999));
        await ShouldFailWith("WaitList.NotFound", () => C.NewAppointmentService().ReceptionistRemoveFromWaitlistAsync(C.Id, doc, rec, wid));

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task Only_an_active_receptionist_can_remove_someone()
    {
        var doc = await NewDoctorAsync();
        var wid = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future);

        await ShouldFailWith("Receptionist.NotFound", () => C.NewAppointmentService().ReceptionistRemoveFromWaitlistAsync(C.Id, doc, Doc.UserId, wid));

        (await WaitAsync(wid)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    // ---------- the receptionist's list ----------

    /// <summary>The list shows the people who still wait first, in the order they joined; the finished ones come after them.</summary>
    [Fact]
    public async Task The_list_shows_who_is_still_waiting_first_in_the_order_they_joined()
    {
        var doc = await NewDoctorAsync();
        var now = DateTime.UtcNow;
        var cancelledOld = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future, WaitlistStatus.Cancelled, now.AddMinutes(-90));
        var second = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future, WaitlistStatus.Waiting, now.AddMinutes(-5));
        var first = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future, WaitlistStatus.Waiting, now.AddMinutes(-30));
        var entered = await AddWaitlistAsync(doc, (await C.AddPatientAsync()).PatientId, Future, WaitlistStatus.Entered, now.AddMinutes(-60));

        var list = await C.NewAppointmentService().GetPatientsWaitListsAsync(C.Id, doc, Future, new PagedRequest());

        list.Items.Select(i => i.WaitLisId).ToList().ShouldBe(new[] { first, second, cancelledOld, entered }.ToList(),
            "Waiting first (oldest first), then the rest by time; Status is stored as text so ordering by it directly would put Waiting last");
    }
}
