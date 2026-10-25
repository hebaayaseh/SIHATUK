using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.DeleteDoctorSlotAsync: the doctor blocks one slot (cancelling a booking if there is one).</summary>
[Collection(SqlServerCollection.Name)]
public class DoctorBlockSlotTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private Task<string> Block(TimeOnly slot, string? reason = null, DateOnly? date = null, int? userId = null, int? centerId = null) =>
        C.NewAppointmentService().DeleteDoctorSlotAsync(centerId ?? C.Id, userId ?? Doc.UserId,
            new DeleteDoctorSlotRequest { timeSlot = slot, date = date ?? Date, Reason = reason });

    [Fact]
    public async Task Blocks_a_free_slot_and_it_disappears_from_availability()
    {
        var msg = await Block(S1000, "meeting");

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        var b = await db.DoctorBlockedDays.SingleAsync();
        b.doctorId.ShouldBe(Doc.DoctorId);
        b.date.ShouldBe(Date);
        b.timeSlot.ShouldBe(S1000);
        b.isBlocked.ShouldBeTrue();
        b.Reason.ShouldBe("meeting");
        (await db.AuditLogs.CountAsync(a => a.Action == "BlockDoctorSlot")).ShouldBe(1);

        var avail = await C.NewAppointmentService().GetAvailableDoctorSlot(C.Id, Doc.DoctorId, Date);
        avail.dateAvailable.ShouldNotContain(S1000);
    }

    [Fact]
    public async Task Blocked_slot_cannot_be_booked()
    {
        await Block(S1000);

        var res = await C.NewAppointmentService().BookAppointmentAsync(C.Id, Doc.DoctorId, Pat.UserId,
            new BookAppointmentRequest { timeSlot = S1000, dateOnly = Date });

        res.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Blocking_a_booked_slot_cancels_the_appointment_postpones_the_service_and_notifies_the_patient()
    {
        var apptId = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        await Block(S0900, "emergency");

        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == apptId);
        appt.appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        appt.cancellationReason.ShouldBe("emergency");

        var postponed = await db.PostponedServices.SingleAsync();
        postponed.PatientId.ShouldBe(Pat.PatientId);
        postponed.AppointmentId.ShouldBe(apptId);
        postponed.Type.ShouldBe(PostponeType.DoctorAppointment);
        postponed.Status.ShouldBe(PostponeStatus.Active);
        postponed.CreatedByUserId.ShouldBe(Doc.UserId);

        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Cancellation)).ShouldBe(1);
        (await db.DoctorBlockedDays.CountAsync(b => b.timeSlot == S0900)).ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_reason_the_cancelled_appointment_gets_a_default_one()
    {
        var apptId = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        await Block(S0900, reason: null);

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == apptId)).cancellationReason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_sub_patients_appointment_notifies_the_parent()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S0900);

        await Block(S0900);

        await using var db = Db();
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Cancellation)).ShouldBe(1);
    }

    [Fact]
    public async Task A_booked_follow_up_goes_back_to_Pending_and_gets_eight_more_days()
    {
        var allowUntil = Date.AddDays(1);
        var followUpId = await C.AddFollowUpAsync(Doc.DoctorId, Pat.PatientId, allowUntil);
        (await C.NewAppointmentService().BookAppointmentAsync(C.Id, Doc.DoctorId, Pat.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = Date, FollowUpId = followUpId })).Success.ShouldBeTrue();

        await Block(S0900);

        await using var db = Db();
        var fu = await db.FollowUps.SingleAsync(f => f.Id == followUpId);
        fu.Status.ShouldBe(FollowUpStatus.Pending);
        fu.AllowFollowUpDate.ShouldBe(allowUntil.AddDays(8));
    }

    [Fact]
    public async Task Other_slots_other_days_and_other_doctors_are_not_touched()
    {
        var doc2 = await C.AddDoctorAsync();
        var keepSameDay = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0930);
        var keepNextWeek = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date.AddDays(7), S0900);
        var other = await C.AddPatientAsync();
        var keepOtherDoctor = await C.AddAppointmentAsync(doc2.DoctorId, other.PatientId, Date, S0900);
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0900);

        await Block(S0900);

        await using var db = Db();
        foreach (var id in new[] { keepSameDay, keepNextWeek, keepOtherDoctor })
            (await db.Appointments.SingleAsync(a => a.Id == id)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public async Task Slot_AlreadyBlocked_when_blocked_twice()
    {
        await Block(S1000);

        await ShouldFailWith("Slot.AlreadyBlocked", () => Block(S1000));

        await using var db = Db();
        (await db.DoctorBlockedDays.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public Task Date_Invalid_for_the_past() =>
        ShouldFailWith("Date.Invalid", () => Block(S1000, date: ClinicClock.Today.AddDays(-2)));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => Block(S1000, centerId: 999_999));

    [Fact]
    public Task Doctor_NotFound_when_the_user_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Block(S1000, userId: Pat.UserId));

    [Fact]
    public async Task Doctor_NotFound_when_the_doctor_is_inactive()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => Block(S1000, userId: inactive.UserId));
    }

    [Fact]
    public Task Schedule_NotFound_on_a_non_working_day() =>
        ShouldFailWith("Schedule.NotFound", () => Block(S1000, date: TestCenter.FutureDate(DayOfWeek.Tuesday)));
}
