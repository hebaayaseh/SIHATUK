using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.CancelAppointmentAsync (patient side).</summary>
[Collection(SqlServerCollection.Name)]
public class CancelAppointmentTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private CancelAppointmentRequest Cancel(TimeOnly slot, string? reason = null, int? sub = null, DateOnly? date = null) =>
        new() { timeSlot = slot, date = date ?? Date, Resone = reason, SubPatientId = sub };

    private Task<string> DoCancel(int userId, CancelAppointmentRequest req, int? doctorId = null, int? centerId = null) =>
        C.NewAppointmentService().CancelAppointmentAsync(centerId ?? C.Id, doctorId ?? Doc.DoctorId, userId, req);

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task Cancels_a_confirmed_appointment_and_records_reason_notification_and_audit()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        var msg = await DoCancel(Pat.UserId, Cancel(S0900, "travelling"));

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == id);
        appt.appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        appt.cancellationReason.ShouldBe("travelling");
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Cancellation)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == "CancelAppointment" && a.EntityId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task Cancelled_slot_can_be_booked_by_someone_else()
    {
        var other = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await DoCancel(Pat.UserId, Cancel(S0900));

        var res = await C.NewAppointmentService().BookAppointmentAsync(C.Id, Doc.DoctorId, other.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = Date });

        res.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancelling_for_a_sub_patient_works_and_notifies_the_parent()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S1000);

        await DoCancel(Pat.UserId, Cancel(S1000, sub: sub.PatientId));

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == id)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Cancellation)).ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_a_follow_up_appointment_cancels_the_follow_up_too()
    {
        var followUpId = await C.AddFollowUpAsync(Doc.DoctorId, Pat.PatientId, allowUntil: Date.AddDays(1));
        (await C.NewAppointmentService().BookAppointmentAsync(C.Id, Doc.DoctorId, Pat.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = Date, FollowUpId = followUpId })).Success.ShouldBeTrue();

        await DoCancel(Pat.UserId, Cancel(S0900));

        await using var db = Db();
        (await db.FollowUps.SingleAsync(f => f.Id == followUpId)).Status.ShouldBe(FollowUpStatus.Cancelled);
    }

    // ------------------------------------------------------------------ waitlist promotion

    [Fact]
    public async Task Cancelling_promotes_the_oldest_waiting_patient_into_the_freed_slot()
    {
        var first = await C.AddPatientAsync();
        var second = await C.AddPatientAsync();
        var otherDay = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        // inserted newest-first on purpose: the order must come from CreatedAt, not from the Id
        int secondWaitId, firstWaitId, otherDayWaitId;
        await using (var db = Db())
        {
            var w2 = Wait(second.PatientId, Date, DateTime.UtcNow.AddHours(-1));
            var w1 = Wait(first.PatientId, Date, DateTime.UtcNow.AddHours(-2));
            var w3 = Wait(otherDay.PatientId, Date.AddDays(7), DateTime.UtcNow.AddHours(-5));   // other date: must be ignored
            db.Waitlists.AddRange(w2, w1, w3);
            await db.SaveChangesAsync();
            (secondWaitId, firstWaitId, otherDayWaitId) = (w2.Id, w1.Id, w3.Id);
        }

        await DoCancel(Pat.UserId, Cancel(S0900));

        await using var check = Db();
        (await check.Waitlists.SingleAsync(w => w.Id == firstWaitId)).Status.ShouldBe(WaitlistStatus.Entered);
        (await check.Waitlists.SingleAsync(w => w.Id == secondWaitId)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await check.Waitlists.SingleAsync(w => w.Id == otherDayWaitId)).Status.ShouldBe(WaitlistStatus.Waiting);

        var promoted = await check.Appointments.SingleAsync(a =>
            a.patientId == first.PatientId && a.appointmentDate == Date && a.timeSlot == S0900);
        promoted.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await check.Notifications.CountAsync(n => n.UserId == first.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
        (await check.Appointments.CountAsync(a => a.timeSlot == S0900 && a.appointmentStatus == AppointmentStatus.Confirmed)).ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_with_nobody_waiting_leaves_the_slot_free()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        await DoCancel(Pat.UserId, Cancel(S0900));

        await using var db = Db();
        (await db.Appointments.CountAsync(a => a.appointmentStatus == AppointmentStatus.Confirmed)).ShouldBe(0);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public Task Appointment_NotFound_when_there_is_no_appointment_at_that_slot() =>
        ShouldFailWith("Appointment.NotFound", () => DoCancel(Pat.UserId, Cancel(S0900)));

    [Fact]
    public async Task Appointment_NotFound_when_the_appointment_belongs_to_another_patient()
    {
        var other = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0900);

        await ShouldFailWith("Appointment.NotFound", () => DoCancel(Pat.UserId, Cancel(S0900)));

        await using var db = Db();
        (await db.Appointments.SingleAsync()).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
    }

    [Fact]
    public async Task Appointment_NotFound_when_it_was_already_cancelled()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        await DoCancel(Pat.UserId, Cancel(S0900));

        await ShouldFailWith("Appointment.NotFound", () => DoCancel(Pat.UserId, Cancel(S0900)));
    }

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var strangersChild = await C.AddSubPatientAsync(stranger);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => DoCancel(Pat.UserId, Cancel(S0900, sub: strangersChild.PatientId)));
    }

    [Fact]
    public Task Patient_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Patient.NotFound", () => DoCancel(999_999, Cancel(S0900)));

    [Fact]
    public Task Doctor_NotFound_for_an_unknown_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => DoCancel(Pat.UserId, Cancel(S0900), doctorId: 999_999));

    [Fact]
    public Task Center_NotFound_for_an_unknown_center() =>
        ShouldFailWith("Center.NotFound", () => DoCancel(Pat.UserId, Cancel(S0900), centerId: 999_999));

    private Waitlist Wait(int patientId, DateOnly date, DateTime createdAtUtc) => new()
    {
        PatientId = patientId, DoctorId = Doc.DoctorId, PreferredDate = date,
        Status = WaitlistStatus.Waiting, CreatedAt = createdAtUtc
    };
}
