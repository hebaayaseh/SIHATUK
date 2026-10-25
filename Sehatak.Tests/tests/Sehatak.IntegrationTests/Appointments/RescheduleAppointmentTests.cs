using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.RescheduleAppointmentAsync (patient side).</summary>
[Collection(SqlServerCollection.Name)]
public class RescheduleAppointmentTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private RescheduleAppointmentRequest Move(int appointmentId, TimeOnly slot, DateOnly? date = null, int? sub = null) =>
        new() { appointmentId = appointmentId, timeSlot = slot, date = date ?? Date, SubPatientId = sub };

    private Task<BookAppointmentRespesponse> DoMove(int userId, RescheduleAppointmentRequest req, int? doctorId = null, int? centerId = null) =>
        C.NewAppointmentService().RescheduleAppointmentAsync(centerId ?? C.Id, doctorId ?? Doc.DoctorId, userId, req);

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task Moves_to_a_free_slot_on_the_same_day()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        var res = await DoMove(Pat.UserId, Move(id, S1000));

        res.Success.ShouldBeTrue();
        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == id);
        appt.timeSlot.ShouldBe(S1000);
        appt.appointmentDate.ShouldBe(Date);
        appt.RescheduleCount.ShouldBe(1);
        appt.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == "RescheduleAppointment" && a.EntityId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task Moves_to_another_day()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        var nextMonday = Date.AddDays(7);

        var res = await DoMove(Pat.UserId, Move(id, S0930, nextMonday));

        res.Success.ShouldBeTrue();
        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == id);
        appt.appointmentDate.ShouldBe(nextMonday);
        appt.timeSlot.ShouldBe(S0930);
    }

    [Fact]
    public async Task The_old_slot_becomes_bookable_again()
    {
        var other = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await DoMove(Pat.UserId, Move(id, S1000));

        var res = await C.NewAppointmentService().BookAppointmentAsync(C.Id, Doc.DoctorId, other.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = Date });

        res.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Rescheduling_a_sub_patient_appointment_works()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S0900);

        var res = await DoMove(Pat.UserId, Move(id, S1030, sub: sub.PatientId));

        res.Success.ShouldBeTrue();
    }

    // ------------------------------------------------------------------ slot not available / limits

    [Fact]
    public async Task Taken_slot_is_refused_with_alternatives_and_nothing_changes()
    {
        var other = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1000);

        var res = await DoMove(Pat.UserId, Move(id, S1000));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots.ShouldNotBeNull();
        res.AlternativeSlots!.ShouldNotContain(S1000);
        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == id);
        appt.timeSlot.ShouldBe(S0900);
        appt.RescheduleCount.ShouldBe(0);
    }

    [Fact]
    public async Task Blocked_slot_is_refused()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.BlockAsync(Doc.DoctorId, Date, S1000);

        var res = await DoMove(Pat.UserId, Move(id, S1000));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots!.ShouldNotContain(S1000);
    }

    [Fact]
    public async Task No_free_slot_that_day_returns_no_alternatives()
    {
        var other = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        foreach (var s in new[] { S0930, S1000, S1030 })
            await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, s);

        var res = await DoMove(Pat.UserId, Move(id, S1000));

        res.Success.ShouldBeFalse();
        res.AlternativeSlots.ShouldBeNull();
    }

    [Fact]
    public async Task After_three_reschedules_the_fourth_is_refused()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await using (var db = Db())
        {
            (await db.Appointments.SingleAsync(a => a.Id == id)).RescheduleCount = 3;
            await db.SaveChangesAsync();
        }

        var res = await DoMove(Pat.UserId, Move(id, S1000));

        res.Success.ShouldBeFalse();
        await using var check = Db();
        var appt = await check.Appointments.SingleAsync(a => a.Id == id);
        appt.timeSlot.ShouldBe(S0900);
        appt.RescheduleCount.ShouldBe(3);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public async Task Date_Invalid_for_a_date_in_the_past()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await ShouldFailWith("Date.Invalid", () => DoMove(Pat.UserId, Move(id, S1000, ClinicClock.Today.AddDays(-2))));
    }

    [Fact]
    public async Task Appointment_NotFound_for_an_unknown_id()
    {
        await ShouldFailWith("Appointment.NotFound", () => DoMove(Pat.UserId, Move(999_999, S1000)));
    }

    [Fact]
    public async Task Appointment_NotFound_when_it_belongs_to_another_patient()
    {
        var other = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0900);

        await ShouldFailWith("Appointment.NotFound", () => DoMove(Pat.UserId, Move(id, S1000)));
    }

    [Fact]
    public async Task Appointment_NotFound_when_it_is_not_Confirmed()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.Cancelled);
        await ShouldFailWith("Appointment.NotFound", () => DoMove(Pat.UserId, Move(id, S1000)));
    }

    [Fact]
    public async Task Doctor_DayBlocked_when_the_target_day_is_blocked()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.BlockAsync(Doc.DoctorId, Date.AddDays(7), slot: null);

        await ShouldFailWith("Doctor.DayBlocked", () => DoMove(Pat.UserId, Move(id, S1000, Date.AddDays(7))));
    }

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        var id = await C.AddAppointmentAsync(Doc.DoctorId, child.PatientId, Date, S0900);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => DoMove(Pat.UserId, Move(id, S1000, sub: child.PatientId)));
    }

    [Fact]
    public Task Patient_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Patient.NotFound", () => DoMove(999_999, Move(1, S1000)));

    [Fact]
    public async Task Doctor_NotFound_for_an_unknown_doctor()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await ShouldFailWith("Doctor.NotFound", () => DoMove(Pat.UserId, Move(id, S1000), doctorId: 999_999));
    }

    [Fact]
    public async Task Center_NotFound_for_an_unknown_center()
    {
        await ShouldFailWith("Center.NotFound", () => DoMove(Pat.UserId, Move(1, S1000), centerId: 999_999));
    }

    [Fact]
    public async Task Schedule_SlotDurationMissing_when_the_target_day_has_no_duration()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await C.AddScheduleAsync(Doc.DoctorId, DayOfWeek.Wednesday, S0900, new TimeOnly(11, 0), slotMinutes: null);

        await ShouldFailWith("Schedule.SlotDurationMissing",
            () => DoMove(Pat.UserId, Move(id, S1000, TestCenter.FutureDate(DayOfWeek.Wednesday))));
    }

    // ------------------------------------------------------------------ concurrency

    [Fact]
    public async Task Two_patients_moving_into_the_same_free_slot_at_once_end_with_one_appointment_there()
    {
        var other = await C.AddPatientAsync();
        var a1 = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        var a2 = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0930);

        var outcomes = await Task.WhenAll(
            Attempt(() => DoMove(Pat.UserId, Move(a1, S1030))),
            Attempt(() => DoMove(other.UserId, Move(a2, S1030))));

        outcomes.Count(o => o.Success).ShouldBe(1);
        foreach (var o in outcomes.Where(o => o.Error != null))
            o.Error.ShouldBeOfType<DbUpdateException>();   // would be an HTTP 500 unless the API maps it

        await using var db = Db();
        (await db.Appointments.CountAsync(x => x.timeSlot == S1030 && x.appointmentDate == Date)).ShouldBe(1);
    }

    private static Task<(bool Success, Exception? Error)> Attempt(Func<Task<BookAppointmentRespesponse>> act) =>
        Task.Run(async () =>
        {
            try { return ((await act()).Success, (Exception?)null); }
            catch (Exception ex) { return (false, ex); }
        });

    // ------------------------------------------------------------------ KNOWN INCONSISTENCY (expected to FAIL until decided)

    /// <summary>
    /// When the doctor has no schedule on the target day, RescheduleAppointmentAsync throws "Doctor.NotFound"
    /// (copy/paste?) while BookAppointmentAsync throws "Schedule.NotFound" for the same situation.
    /// If "Doctor.NotFound" is intentional for the mobile app, change the expected code here instead.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Schedule_NotFound_when_the_doctor_does_not_work_on_the_target_day()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        await ShouldFailWith("Schedule.NotFound",
            () => DoMove(Pat.UserId, Move(id, S1000, TestCenter.FutureDate(DayOfWeek.Tuesday))));
    }
    [Fact]
    public async Task Rescheduling_frees_the_old_slot_but_does_not_promote_anyone_from_the_waitlist()
    {
        var filler = await C.AddPatientAsync();
        var waiting = await C.AddPatientAsync();
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        foreach (var s in new[] { S0930, S1000, S1030 })
            await C.AddAppointmentAsync(Doc.DoctorId, filler.PatientId, Date, s);
        await using (var seed = Db())
        {
            seed.Waitlists.Add(new Waitlist
            {
                PatientId = waiting.PatientId,
                DoctorId = Doc.DoctorId,
                PreferredDate = Date,
                Status = WaitlistStatus.Waiting,
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }

    (await DoMove(Pat.UserId, Move(id, S0900, Date.AddDays(7)))).Success.ShouldBeTrue();

        await using var db = Db();
        (await db.Waitlists.SingleAsync()).Status.ShouldBe(WaitlistStatus.Waiting);
        (await db.Appointments.CountAsync(a => a.appointmentDate == Date && a.timeSlot == S0900
            && a.appointmentStatus == AppointmentStatus.Confirmed)).ShouldBe(0);
    }
}

