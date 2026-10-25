using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.ReceptionistBookAppointmentAsync (receptionist books on behalf of a patient).</summary>
[Collection(SqlServerCollection.Name)]
public class ReceptionistBookAppointmentTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private int _rec;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _rec = await C.AddReceptionistAsync();      // see the TestCenter patch
    }

    private ReceptionistBookRequestDto Req(TimeOnly slot, int? patientId = null, DateOnly? date = null, int? sub = null, int? followUp = null) =>
        new() { PatientId = patientId ?? Pat.PatientId, timeSlot = slot, dateOnly = date ?? Date, SubPatientId = sub, FollowUpId = followUp };

    private Task<BookAppointmentRespesponse> Book(ReceptionistBookRequestDto req, int? userId = null, int? doctorId = null, int? centerId = null) =>
        C.NewAppointmentService().ReceptionistBookAppointmentAsync(centerId ?? C.Id, userId ?? _rec, doctorId ?? Doc.DoctorId, req);

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task Books_for_the_patient_and_records_the_receptionist()
    {
        var res = await Book(Req(S0930));

        res.Success.ShouldBeTrue();
        await using var db = Db();
        var appt = await db.Appointments.SingleAsync();
        appt.patientId.ShouldBe(Pat.PatientId);
        appt.doctorId.ShouldBe(Doc.DoctorId);
        appt.timeSlot.ShouldBe(S0930);
        appt.appointmentDate.ShouldBe(Date);
        appt.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        appt.ReceptionistId.ShouldBe(_rec);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == "BookAppointment" && a.EntityId == appt.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Books_for_a_sub_patient()
    {
        var sub = await C.AddSubPatientAsync(Pat);

        var res = await Book(Req(S1000, sub: sub.PatientId));

        res.Success.ShouldBeTrue();
        await using var db = Db();
        (await db.Appointments.SingleAsync()).patientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task Books_a_follow_up()
    {
        var followUpId = await C.AddFollowUpAsync(Doc.DoctorId, Pat.PatientId, allowUntil: Date.AddDays(1));

        var res = await Book(Req(S0900, followUp: followUpId));

        res.Success.ShouldBeTrue();
        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.timeSlot == S0900);
        appt.IsFollowUp.ShouldBeTrue();
        var fu = await db.FollowUps.SingleAsync(f => f.Id == followUpId);
        fu.Status.ShouldBe(FollowUpStatus.Booked);
        fu.ScheduledAppointmentId.ShouldBe(appt.Id);
    }

    // ------------------------------------------------------------------ slot not available

    [Fact]
    public async Task Taken_slot_returns_alternatives_and_creates_nothing()
    {
        var other = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0900);

        var res = await Book(Req(S0900));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots!.ShouldNotContain(S0900);
        res.AlternativeSlots.Count.ShouldBe(3);
        await using var db = Db();
        (await db.Appointments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Full_day_adds_the_patient_to_the_waitlist()
    {
        var other = await C.AddPatientAsync();
        foreach (var s in new[] { S0900, S0930, S1000, S1030 })
            await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, s);

        var res = await Book(Req(S0900));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots.ShouldBeNull();
        await using var db = Db();
        var w = await db.Waitlists.SingleAsync();
        w.PatientId.ShouldBe(Pat.PatientId);
        w.Status.ShouldBe(WaitlistStatus.Waiting);
    }

    /// <summary>
    /// This path stores Waitlist.CreatedAt = ClinicClock.Now (local time) while everything else stores UTC.
    /// DelayMonitorJobs / Cancel pick the next waiting patient with OrderBy(CreatedAt), so an entry added here
    /// sorts 2-3 hours "later" than it should. Fix: CreatedAt = DateTime.UtcNow.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Waitlist_entry_created_by_the_receptionist_is_stored_in_UTC()
    {
        var other = await C.AddPatientAsync();
        foreach (var s in new[] { S0900, S0930, S1000, S1030 })
            await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, s);

        await Book(Req(S0900));

        await using var db = Db();
        var w = await db.Waitlists.SingleAsync();
        Math.Abs((w.CreatedAt - DateTime.UtcNow).TotalMinutes).ShouldBeLessThan(5, $"CreatedAt={w.CreatedAt:O}, UTC now={DateTime.UtcNow:O}");
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public Task Date_Invalid_for_the_past() =>
        ShouldFailWith("Date.Invalid", () => Book(Req(S0900, date: ClinicClock.Today.AddDays(-2))));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => Book(Req(S0900), centerId: 999_999));

    [Fact]
    public Task Receptionist_NotFound_when_the_user_is_not_a_receptionist() =>
        ShouldFailWith("Receptionist.NotFound", () => Book(Req(S0900), userId: C.AdminUserId));

    [Fact]
    public Task Receptionist_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Receptionist.NotFound", () => Book(Req(S0900), userId: 999_999));

    [Fact]
    public Task Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => Book(Req(S0900), doctorId: 999_999));

    [Fact]
    public Task Patient_NotFound_for_an_unknown_patient() =>
        ShouldFailWith("Patient.NotFound", () => Book(Req(S0900, patientId: 999_999)));

    [Fact]
    public async Task Patient_NotFound_when_the_patient_account_is_inactive()
    {
        var inactive = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => Book(Req(S0900, patientId: inactive.PatientId)));
    }

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => Book(Req(S0900, sub: child.PatientId)));
    }

    [Fact]
    public Task Schedule_NotFound_on_a_non_working_day() =>
        ShouldFailWith("Schedule.NotFound", () => Book(Req(S0900, date: TestCenter.FutureDate(DayOfWeek.Tuesday))));

    [Fact]
    public Task FollowUp_NotFoundOrNotPending_for_an_unknown_follow_up() =>
        ShouldFailWith("FollowUp.NotFoundOrNotPending", () => Book(Req(S0900, followUp: 999_999)));

    [Fact]
    public async Task FollowUp_DateNotAllowed_after_the_allowed_date()
    {
        var followUpId = await C.AddFollowUpAsync(Doc.DoctorId, Pat.PatientId, allowUntil: Date.AddDays(-1));
        await ShouldFailWith("FollowUp.DateNotAllowed", () => Book(Req(S0900, followUp: followUpId)));
    }

    [Fact]
    public async Task Appointment_AlreadyExists_for_the_same_patient_doctor_and_day()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await ShouldFailWith("Appointment.AlreadyExists", () => Book(Req(S1000)));
    }

    [Fact]
    public async Task Doctor_DayBlocked_when_the_day_is_blocked()
    {
        await C.BlockAsync(Doc.DoctorId, Date, slot: null);
        await ShouldFailWith("Doctor.DayBlocked", () => Book(Req(S0900)));
    }

    [Fact]
    public async Task Schedule_SlotDurationMissing_when_duration_is_not_set()
    {
        var doc2 = await C.AddDoctorAsync();
        await C.AddScheduleAsync(doc2.DoctorId, DayOfWeek.Monday, S0900, new TimeOnly(11, 0), slotMinutes: null);

        await ShouldFailWith("Schedule.SlotDurationMissing", () => Book(Req(S0900), doctorId: doc2.DoctorId));
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task Receptionist_and_patient_racing_for_the_same_slot_end_with_one_appointment()
    {
        var other = await C.AddPatientAsync();

        var outcomes = await Task.WhenAll(
            Attempt(() => Book(Req(S0900))),
            Attempt(() => C.NewAppointmentService().BookAppointmentAsync(C.Id, Doc.DoctorId, other.UserId,
                new BookAppointmentRequest { timeSlot = S0900, dateOnly = Date })));

        outcomes.Count(o => o.Success).ShouldBe(1);
        foreach (var o in outcomes.Where(o => o.Error != null))
            o.Error.ShouldBeOfType<DbUpdateException>();

        await using var db = Db();
        (await db.Appointments.CountAsync(a => a.timeSlot == S0900 && a.appointmentDate == Date)).ShouldBe(1);
    }

    private static Task<(bool Success, Exception? Error)> Attempt(Func<Task<BookAppointmentRespesponse>> act) =>
        Task.Run(async () =>
        {
            try { return ((await act()).Success, (Exception?)null); }
            catch (Exception ex) { return (false, ex); }
        });
}
