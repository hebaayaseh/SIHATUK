using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>AppointmentService.ReceptionistCancelAppointmentAsync.</summary>
[Collection(SqlServerCollection.Name)]
public class ReceptionistCancelAppointmentTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private int _rec;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _rec = await C.AddReceptionistAsync();
    }

    private ReceptionistCancelAppointmentRequest Cancel(TimeOnly slot, int? patientId = null, int? sub = null, string reason = "patient called") =>
        new() { PatientId = patientId ?? Pat.PatientId, timeSlot = slot, date = Date, Resone = reason, SubPatientId = sub };

    private Task<string> DoCancel(ReceptionistCancelAppointmentRequest req, int? userId = null, int? doctorId = null, int? centerId = null) =>
        C.NewAppointmentService().ReceptionistCancelAppointmentAsync(centerId ?? C.Id, doctorId ?? Doc.DoctorId, userId ?? _rec, req);

    [Fact]
    public async Task Cancels_the_appointment_and_records_reason_notification_and_audit()
    {
        var id = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        (await DoCancel(Cancel(S0900))).ShouldNotBeNullOrWhiteSpace();

        await using var db = Db();
        var appt = await db.Appointments.SingleAsync(a => a.Id == id);
        appt.appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        appt.cancellationReason.ShouldBe("patient called");
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Cancellation)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == "CancelAppointment" && a.EntityId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_for_a_sub_patient_works()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Date, S1000);

        await DoCancel(Cancel(S1000, sub: sub.PatientId));

        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == id)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Cancelling_a_follow_up_appointment_cancels_the_follow_up()
    {
        var followUpId = await C.AddFollowUpAsync(Doc.DoctorId, Pat.PatientId, allowUntil: Date.AddDays(1));
        (await C.NewAppointmentService().BookAppointmentAsync(C.Id, Doc.DoctorId, Pat.UserId,
            new BookAppointmentRequest { timeSlot = S0900, dateOnly = Date, FollowUpId = followUpId })).Success.ShouldBeTrue();

        await DoCancel(Cancel(S0900));

        await using var db = Db();
        (await db.FollowUps.SingleAsync(f => f.Id == followUpId)).Status.ShouldBe(FollowUpStatus.Cancelled);
    }

    [Fact]
    public async Task Cancelling_promotes_the_oldest_waiting_patient_and_credits_the_receptionist()
    {
        var first = await C.AddPatientAsync();
        var second = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
        int firstWaitId, secondWaitId;
        await using (var db = Db())
        {
            var w2 = Wait(second.PatientId, DateTime.UtcNow.AddHours(-1));
            var w1 = Wait(first.PatientId, DateTime.UtcNow.AddHours(-2));
            db.Waitlists.AddRange(w2, w1);
            await db.SaveChangesAsync();
            (firstWaitId, secondWaitId) = (w1.Id, w2.Id);
        }

        await DoCancel(Cancel(S0900));

        await using var check = Db();
        (await check.Waitlists.SingleAsync(w => w.Id == firstWaitId)).Status.ShouldBe(WaitlistStatus.Entered);
        (await check.Waitlists.SingleAsync(w => w.Id == secondWaitId)).Status.ShouldBe(WaitlistStatus.Waiting);
        var promoted = await check.Appointments.SingleAsync(a => a.patientId == first.PatientId && a.timeSlot == S0900);
        promoted.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        promoted.ReceptionistId.ShouldBe(_rec);
        (await check.Notifications.CountAsync(n => n.UserId == first.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public Task Appointment_NotFound_when_nothing_is_booked_there() =>
        ShouldFailWith("Appointment.NotFound", () => DoCancel(Cancel(S0900)));

    [Fact]
    public async Task Appointment_NotFound_when_it_belongs_to_another_patient()
    {
        var other = await C.AddPatientAsync();
        await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S0900);

        await ShouldFailWith("Appointment.NotFound", () => DoCancel(Cancel(S0900)));
    }

    [Fact]
    public Task Patient_NotFound_for_an_unknown_patient() =>
        ShouldFailWith("Patient.NotFound", () => DoCancel(Cancel(S0900, patientId: 999_999)));

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => DoCancel(Cancel(S0900, sub: child.PatientId)));
    }

    [Fact]
    public Task Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => DoCancel(Cancel(S0900), doctorId: 999_999));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => DoCancel(Cancel(S0900), centerId: 999_999));

    /// <summary>
    /// Receptionist book and receptionist reschedule both verify that userId is an active Receptionist
    /// ("Receptionist.NotFound"); receptionist cancel does not, so any user id is accepted and, if somebody is
    /// promoted from the waitlist, it is stored in Appointment.ReceptionistId. Confirm this is intended.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Receptionist_NotFound_when_the_user_is_not_a_receptionist()
    {
        await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);

        await ShouldFailWith("Receptionist.NotFound", () => DoCancel(Cancel(S0900), userId: C.AdminUserId));
    }

    private Waitlist Wait(int patientId, DateTime createdAtUtc) => new()
    {
        PatientId = patientId, DoctorId = Doc.DoctorId, PreferredDate = Date,
        Status = WaitlistStatus.Waiting, CreatedAt = createdAtUtc
    };
}
