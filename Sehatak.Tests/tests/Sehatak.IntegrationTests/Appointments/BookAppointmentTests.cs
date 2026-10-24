using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>
/// AppointmentService.BookAppointmentAsync against a real SQL Server.
/// Scenario per test: Monday schedule 09:00-11:00, 30 min slots => 09:00, 09:30, 10:00, 10:30.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class BookAppointmentTests(SqlServerFixture fx) : IAsyncLifetime
{
    private static readonly TimeOnly S0900 = new(9, 0);
    private static readonly TimeOnly S0930 = new(9, 30);
    private static readonly TimeOnly S1000 = new(10, 0);
    private static readonly TimeOnly S1030 = new(10, 30);
    private static readonly TimeOnly[] AllSlots = { S0900, S0930, S1000, S1030 };

    private TestCenter _c = null!;
    private DoctorInfo _doc = null!;
    private PatientInfo _pat = null!;
    private DateOnly _date;

    public async Task InitializeAsync()
    {
        _c = await fx.Harness.CreateCenterAsync();
        _doc = await _c.AddDoctorAsync();
        _pat = await _c.AddPatientAsync();
        _date = TestCenter.FutureDate(DayOfWeek.Monday);
        await _c.AddScheduleAsync(_doc.DoctorId, DayOfWeek.Monday, S0900, new TimeOnly(11, 0), 30);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private BookAppointmentRequest Req(TimeOnly slot, DateOnly? date = null) =>
        new() { timeSlot = slot, dateOnly = date ?? _date };

    private Task<BookAppointmentRespesponse> Book(int userId, BookAppointmentRequest req, int? doctorId = null, int? centerId = null) =>
        _c.NewAppointmentService().BookAppointmentAsync(centerId ?? _c.Id, doctorId ?? _doc.DoctorId, userId, req);

    private static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task Books_a_free_slot_and_writes_appointment_notification_and_audit()
    {
        var res = await Book(_pat.UserId, Req(S0930));

        res.Success.ShouldBeTrue();
        res.AlternativeSlots.ShouldBeNull();

        await using var db = fx.Harness.Tenant(_c.Id);
        var appt = await db.Appointments.SingleAsync();
        appt.patientId.ShouldBe(_pat.PatientId);
        appt.doctorId.ShouldBe(_doc.DoctorId);
        appt.appointmentDate.ShouldBe(_date);
        appt.timeSlot.ShouldBe(S0930);
        appt.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        appt.IsFollowUp.ShouldBeFalse();

        var note = await db.Notifications.SingleAsync(n => n.UserId == _pat.UserId);
        note.Type.ShouldBe(NotificationType.Appointment);

        (await db.AuditLogs.CountAsync(a => a.Action == "BookAppointment" && a.EntityId == appt.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Booking_for_a_sub_patient_stores_the_child_and_notifies_the_parent()
    {
        var sub = await _c.AddSubPatientAsync(_pat);

        var res = await Book(_pat.UserId, new BookAppointmentRequest { timeSlot = S1000, dateOnly = _date, SubPatientId = sub.PatientId });

        res.Success.ShouldBeTrue();
        await using var db = fx.Harness.Tenant(_c.Id);
        var appt = await db.Appointments.SingleAsync();
        appt.patientId.ShouldBe(sub.PatientId);
        (await db.Notifications.CountAsync(n => n.UserId == _pat.UserId)).ShouldBe(1);
    }

    [Fact]
    public async Task Booking_a_follow_up_marks_it_Booked_and_links_the_appointment()
    {
        var followUpId = await _c.AddFollowUpAsync(_doc.DoctorId, _pat.PatientId, allowUntil: _date.AddDays(1));

        var res = await Book(_pat.UserId, new BookAppointmentRequest { timeSlot = S0900, dateOnly = _date, FollowUpId = followUpId });

        res.Success.ShouldBeTrue();
        await using var db = fx.Harness.Tenant(_c.Id);
        var appt = await db.Appointments.SingleAsync(a => a.timeSlot == S0900);
        appt.IsFollowUp.ShouldBeTrue();
        var fu = await db.FollowUps.SingleAsync(f => f.Id == followUpId);
        fu.Status.ShouldBe(FollowUpStatus.Booked);
        fu.ScheduledAppointmentId.ShouldBe(appt.Id);
    }

    // ------------------------------------------------------------------ slot not available

    [Fact]
    public async Task Taken_slot_returns_the_remaining_slots_and_creates_nothing()
    {
        var other = await _c.AddPatientAsync();
        (await Book(_pat.UserId, Req(S0900))).Success.ShouldBeTrue();

        var res = await Book(other.UserId, Req(S0900));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots.ShouldNotBeNull();
        res.AlternativeSlots!.ShouldNotContain(S0900);
        res.AlternativeSlots.Count.ShouldBe(3);
        await using var db = fx.Harness.Tenant(_c.Id);
        (await db.Appointments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Slot_outside_the_schedule_grid_is_rejected_with_alternatives()
    {
        var res = await Book(_pat.UserId, Req(new TimeOnly(9, 15)));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots.ShouldNotBeNull();
        res.AlternativeSlots!.Count.ShouldBe(4);
        await using var db = fx.Harness.Tenant(_c.Id);
        (await db.Appointments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Blocked_slot_is_not_bookable_and_not_offered()
    {
        await _c.BlockAsync(_doc.DoctorId, _date, S0930);

        var res = await Book(_pat.UserId, Req(S0930));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots!.ShouldNotContain(S0930);
        res.AlternativeSlots.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Fully_booked_day_puts_the_patient_on_the_waitlist()
    {
        var other = await _c.AddPatientAsync();
        foreach (var s in AllSlots)
            await _c.AddAppointmentAsync(_doc.DoctorId, other.PatientId, _date, s);

        var res = await Book(_pat.UserId, Req(S0900));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots.ShouldBeNull();
        await using var db = fx.Harness.Tenant(_c.Id);
        var w = await db.Waitlists.SingleAsync();
        w.PatientId.ShouldBe(_pat.PatientId);
        w.DoctorId.ShouldBe(_doc.DoctorId);
        w.PreferredDate.ShouldBe(_date);
        w.Status.ShouldBe(WaitlistStatus.Waiting);
        (await db.Appointments.CountAsync()).ShouldBe(AllSlots.Length);
    }

    // ------------------------------------------------------------------ business errors (one per code)

    [Fact]
    public Task Date_Invalid_when_the_date_is_in_the_past() =>
        ShouldFailWith("Date.Invalid", () => Book(_pat.UserId, Req(S0900, ClinicClock.Today.AddDays(-2))));

    [Fact]
    public async Task Center_NotFound_when_the_center_is_not_active()
    {
        var suspended = await fx.Harness.CreateCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => Book(_pat.UserId, Req(S0900), centerId: suspended.Id));
    }

    [Fact]
    public Task Center_NotFound_when_the_center_does_not_exist() =>
        ShouldFailWith("Center.NotFound", () => Book(_pat.UserId, Req(S0900), centerId: 999_999));

    [Fact]
    public Task Doctor_NotFound_when_the_doctor_does_not_exist() =>
        ShouldFailWith("Doctor.NotFound", () => Book(_pat.UserId, Req(S0900), doctorId: 999_999));

    [Fact]
    public async Task Doctor_NotFound_when_the_doctor_account_is_inactive()
    {
        var inactive = await _c.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => Book(_pat.UserId, Req(S0900), doctorId: inactive.DoctorId));
    }

    [Fact]
    public Task Schedule_NotFound_on_a_day_the_doctor_does_not_work() =>
        ShouldFailWith("Schedule.NotFound",
            () => Book(_pat.UserId, Req(S0900, TestCenter.FutureDate(DayOfWeek.Tuesday))));

    [Fact]
    public async Task Schedule_NotFound_when_the_schedule_is_inactive()
    {
        var doc2 = await _c.AddDoctorAsync();
        await _c.AddScheduleAsync(doc2.DoctorId, DayOfWeek.Monday, S0900, new TimeOnly(11, 0), 30, isActive: false);
        await ShouldFailWith("Schedule.NotFound", () => Book(_pat.UserId, Req(S0900), doctorId: doc2.DoctorId));
    }

    [Fact]
    public Task Patient_NotFound_when_the_user_has_no_patient_profile() =>
        ShouldFailWith("Patient.NotFound", () => Book(999_999, Req(S0900)));

    [Fact]
    public async Task Patient_NotFound_when_the_patient_account_is_inactive()
    {
        var inactive = await _c.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => Book(inactive.UserId, Req(S0900)));
    }

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_when_the_child_belongs_to_someone_else()
    {
        var stranger = await _c.AddPatientAsync();
        var strangersChild = await _c.AddSubPatientAsync(stranger);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => Book(_pat.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = _date, SubPatientId = strangersChild.PatientId }));
    }

    [Fact]
    public Task FollowUp_NotFoundOrNotPending_for_an_unknown_follow_up() =>
        ShouldFailWith("FollowUp.NotFoundOrNotPending", () => Book(_pat.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = _date, FollowUpId = 999_999 }));

    [Fact]
    public async Task FollowUp_DateNotAllowed_when_booking_after_the_allowed_date()
    {
        var followUpId = await _c.AddFollowUpAsync(_doc.DoctorId, _pat.PatientId, allowUntil: _date.AddDays(-1));

        await ShouldFailWith("FollowUp.DateNotAllowed", () => Book(_pat.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = _date, FollowUpId = followUpId }));
    }

    [Fact]
    public async Task Appointment_AlreadyExists_for_the_same_patient_doctor_and_day()
    {
        await _c.AddAppointmentAsync(_doc.DoctorId, _pat.PatientId, _date, S0900);

        await ShouldFailWith("Appointment.AlreadyExists", () => Book(_pat.UserId, Req(S1000)));
    }

    [Fact]
    public async Task Doctor_DayBlocked_when_the_whole_day_is_blocked()
    {
        await _c.BlockAsync(_doc.DoctorId, _date, slot: null);

        await ShouldFailWith("Doctor.DayBlocked", () => Book(_pat.UserId, Req(S0900)));
    }

    [Fact]
    public async Task Schedule_SlotDurationMissing_when_the_schedule_has_no_duration()
    {
        var doc2 = await _c.AddDoctorAsync();
        await _c.AddScheduleAsync(doc2.DoctorId, DayOfWeek.Monday, S0900, new TimeOnly(11, 0), slotMinutes: null);

        await ShouldFailWith("Schedule.SlotDurationMissing", () => Book(_pat.UserId, Req(S0900), doctorId: doc2.DoctorId));
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task Concurrent_bookings_of_the_same_slot_produce_exactly_one_appointment()
    {
        // Eight patients race for 09:00. Every check in the service passes for all of them;
        // only the unique filtered index on Appointments(doctorId, date, timeSlot) can stop the others.
        var patients = new List<PatientInfo>();
        for (var i = 0; i < 8; i++) patients.Add(await _c.AddPatientAsync());

        var outcomes = await Task.WhenAll(patients.Select(p => Task.Run(async () =>
        {
            try { return (Success: (await Book(p.UserId, Req(S0900))).Success, Error: (Exception?)null); }
            catch (Exception ex) { return (Success: false, Error: ex); }
        })));

        outcomes.Count(o => o.Success).ShouldBe(1, "exactly one booking may win the slot");

        // Losers may either get Success=false or the DB rejection. Anything else is unexpected.
        // NOTE: a DbUpdateException here would surface as an HTTP 500 unless the API maps it (see API tests).
        foreach (var o in outcomes.Where(o => o.Error != null))
            o.Error.ShouldBeOfType<DbUpdateException>();

        await using var db = fx.Harness.Tenant(_c.Id);
        (await db.Appointments.CountAsync(a => a.timeSlot == S0900 && a.appointmentDate == _date)).ShouldBe(1);
    }

    // ------------------------------------------------------------------ KNOWN BUG (expected to FAIL until fixed)

    /// <summary>
    /// BookAppointmentAsync decides "today" and "now" from DateTime.UtcNow, while GetAvailableDoctorSlot
    /// uses ClinicClock (Palestine time, UTC+2/+3). So during the day a slot that already passed in clinic
    /// time is still bookable (e.g. local 14:30, UTC 11:30 => 13:00 looks like the future).
    /// Fix: use ClinicClock.Today / TimeOnly.FromDateTime(ClinicClock.Now) in BookAppointmentAsync.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Cannot_book_a_slot_that_already_passed_today_in_clinic_time()
    {
        var local = ClinicClock.Now;
        if (local.Hour < 4) return;   // before ~04:00 local the UTC date differs; scenario needs its own test

        var today = ClinicClock.Today;
        var past = new TimeOnly(local.Hour - 1, 0);       // 1-2h ago in clinic time, but still "future" in UTC time
        var doc2 = await _c.AddDoctorAsync();
        await _c.AddScheduleAsync(doc2.DoctorId, today.DayOfWeek, past, past.AddHours(1), 30);

        var res = await Book(_pat.UserId, Req(past, today), doctorId: doc2.DoctorId);

        res.Success.ShouldBeFalse($"slot {past} already passed (clinic time now {local:HH:mm}) but it was booked");
    }
}
