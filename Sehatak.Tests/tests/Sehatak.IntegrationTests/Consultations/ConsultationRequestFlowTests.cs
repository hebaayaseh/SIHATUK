using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Consultations;

/// <summary>
/// First half of the online-consultation life cycle:
/// request (patient) -> schedule / reject (doctor), and cancel (patient).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ConsultationRequestFlowTests(SqlServerFixture fx) : ConsultationTestBase(fx)
{
    // ------------------------------------------------------------------ ConsultationRequest

    [Fact]
    public async Task Request_creates_a_Pending_consultation_notifies_the_patient_and_audits()
    {
        var msg = await NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null);

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        var c = await db.Consultations.SingleAsync();
        c.PatientId.ShouldBe(Pat.PatientId);
        c.DoctorId.ShouldBe(Doc.DoctorId);
        c.Status.ShouldBe(ConsultationStatus.Pending);
        c.ScheduledAt.ShouldBeNull();
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == AuditRequest && a.EntityId == c.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Request_for_a_sub_patient_is_stored_for_the_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);

        await NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, sub.PatientId);

        await using var db = Db();
        (await db.Consultations.SingleAsync()).PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task Parent_and_child_can_each_have_a_pending_request_with_the_same_doctor()
    {
        var sub = await C.AddSubPatientAsync(Pat);

        await NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null);
        await NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, sub.PatientId);

        await using var db = Db();
        (await db.Consultations.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Same_patient_can_ask_two_different_doctors()
    {
        var doc2 = await C.AddDoctorAsync();
        await SetOnlineAsync(doc2.DoctorId, true);

        await NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null);
        await NewConsultService().ConsultationRequest(C.Id, doc2.DoctorId, Pat.UserId, null);

        await using var db = Db();
        (await db.Consultations.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Consultation_AlreadyRequested_while_a_request_is_still_pending()
    {
        await NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null);

        await ShouldFailWith("Consultation.AlreadyRequested",
            () => NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null));

        await using var db = Db();
        (await db.Consultations.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(ConsultationStatus.Rejected)]
    [InlineData(ConsultationStatus.Completed)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task A_new_request_is_allowed_once_the_previous_one_is_finished(ConsultationStatus previous)
    {
        await AddConsultationAsync(Pat.PatientId, previous);

        await NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null);

        await using var db = Db();
        (await db.Consultations.CountAsync(c => c.Status == ConsultationStatus.Pending)).ShouldBe(1);
    }

    [Fact]
    public async Task Doctor_NotFound_when_the_doctor_is_not_enabled_for_online_consultations()
    {
        await SetOnlineAsync(Doc.DoctorId, false);
        await ShouldFailWith("Doctor.NotFound", () => NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null));
    }

    [Fact]
    public async Task Doctor_NotFound_when_the_doctor_is_inactive()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await SetOnlineAsync(inactive.DoctorId, true);
        await ShouldFailWith("Doctor.NotFound", () => NewConsultService().ConsultationRequest(C.Id, inactive.DoctorId, Pat.UserId, null));
    }

    [Fact]
    public Task Doctor_NotFound_for_an_unknown_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().ConsultationRequest(C.Id, 999_999, Pat.UserId, null));

    [Fact]
    public Task Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, 999_999, null));

    [Fact]
    public async Task SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => NewConsultService().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, child.PatientId));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().ConsultationRequest(999_999, Doc.DoctorId, Pat.UserId, null));

    // ------------------------------------------------------------------ DoctorScheduleConsultationAsync

    [Fact]
    public async Task Doctor_schedules_a_pending_request_and_the_patient_is_told()
    {
        var id = await AddConsultationAsync(Pat.PatientId);
        var when = InTwoDays;

        var msg = await NewConsultService().DoctorScheduleConsultationAsync(C.Id, Doc.UserId, id, when);

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        var c = await db.Consultations.SingleAsync(x => x.Id == id);
        c.ScheduledAt.ShouldBe(when);
        c.Status.ShouldBe(ConsultationStatus.Pending);       // still waiting for the payment
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Appointment)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == AuditSchedule && a.EntityId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task Schedule_notifies_the_parent_when_the_patient_is_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await AddConsultationAsync(sub.PatientId);

        await NewConsultService().DoctorScheduleConsultationAsync(C.Id, Doc.UserId, id, InTwoDays);

        await using var db = Db();
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId)).ShouldBe(1);
    }

    [Fact]
    public async Task Consultation_AlreadyScheduled_when_it_has_a_date_already()
    {
        var id = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);

        await ShouldFailWith("Consultation.AlreadyScheduled",
            () => NewConsultService().DoctorScheduleConsultationAsync(C.Id, Doc.UserId, id, InTwoDays.AddDays(1)));
    }

    [Theory]
    [InlineData(ConsultationStatus.Accepted)]
    [InlineData(ConsultationStatus.Rejected)]
    [InlineData(ConsultationStatus.Completed)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task Schedule_Consultation_NotFound_unless_it_is_Pending(ConsultationStatus status)
    {
        var id = await AddConsultationAsync(Pat.PatientId, status);
        await ShouldFailWith("Consultation.NotFound",
            () => NewConsultService().DoctorScheduleConsultationAsync(C.Id, Doc.UserId, id, InTwoDays));
    }

    [Fact]
    public async Task Schedule_Consultation_NotFound_for_another_doctors_request()
    {
        var doc2 = await C.AddDoctorAsync();
        var id = await AddConsultationAsync(Pat.PatientId, doctorId: doc2.DoctorId);
        await ShouldFailWith("Consultation.NotFound",
            () => NewConsultService().DoctorScheduleConsultationAsync(C.Id, Doc.UserId, id, InTwoDays));
    }

    [Fact]
    public Task Schedule_Doctor_NotFound_when_the_user_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().DoctorScheduleConsultationAsync(C.Id, Pat.UserId, 1, InTwoDays));

    [Fact]
    public Task Schedule_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().DoctorScheduleConsultationAsync(999_999, Doc.UserId, 1, InTwoDays));

    /// <summary>
    /// Nothing stops the doctor from "scheduling" a consultation in the past (there is no date validation and no
    /// "Date.Invalid" like the appointment services have). Confirm the rule, then add the check in DoctorScheduleConsultationAsync.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Cannot_schedule_a_consultation_in_the_past()
    {
        var id = await AddConsultationAsync(Pat.PatientId);

        await ShouldFailWith("Date.Invalid",
            () => NewConsultService().DoctorScheduleConsultationAsync(C.Id, Doc.UserId, id, DateTime.UtcNow.AddDays(-2)));
    }

    // ------------------------------------------------------------------ RejectConsultationRequestAsync

    [Fact]
    public async Task Doctor_rejects_a_pending_request_with_a_reason()
    {
        var id = await AddConsultationAsync(Pat.PatientId);

        var msg = await NewConsultService().RejectConsultationRequestAsync(C.Id, id, Doc.UserId, "fully booked");

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        var c = await db.Consultations.SingleAsync(x => x.Id == id);
        c.Status.ShouldBe(ConsultationStatus.Rejected);
        c.Notes.ShouldBe("fully booked");
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == AuditRejectRequest && a.EntityId == id)).ShouldBe(1);
    }

    [Theory]
    [InlineData(ConsultationStatus.Accepted)]
    [InlineData(ConsultationStatus.Rejected)]
    [InlineData(ConsultationStatus.Completed)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task Reject_Consultation_NotFound_unless_it_is_Pending(ConsultationStatus status)
    {
        var id = await AddConsultationAsync(Pat.PatientId, status);
        await ShouldFailWith("Consultation.NotFound",
            () => NewConsultService().RejectConsultationRequestAsync(C.Id, id, Doc.UserId, "x"));
    }

    [Fact]
    public async Task Reject_Consultation_NotFound_for_another_doctors_request()
    {
        var doc2 = await C.AddDoctorAsync();
        var id = await AddConsultationAsync(Pat.PatientId, doctorId: doc2.DoctorId);
        await ShouldFailWith("Consultation.NotFound",
            () => NewConsultService().RejectConsultationRequestAsync(C.Id, id, Doc.UserId, "x"));
    }

    [Fact]
    public Task Reject_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().RejectConsultationRequestAsync(C.Id, 1, Pat.UserId, "x"));

    [Fact]
    public Task Reject_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().RejectConsultationRequestAsync(999_999, 1, Doc.UserId, "x"));

    // ------------------------------------------------------------------ CancelConsultaion (patient)

    [Fact]
    public async Task Patient_cancels_a_pending_request()
    {
        var id = await AddConsultationAsync(Pat.PatientId);

        var msg = await NewConsultService().CancelConsultaion(C.Id, Pat.UserId, id, null);

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        (await db.Consultations.SingleAsync(x => x.Id == id)).Status.ShouldBe(ConsultationStatus.Cancelled);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Cancellation)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == AuditCancel && a.EntityId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task Patient_cancels_for_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await AddConsultationAsync(sub.PatientId);

        await NewConsultService().CancelConsultaion(C.Id, Pat.UserId, id, sub.PatientId);

        await using var db = Db();
        (await db.Consultations.SingleAsync(x => x.Id == id)).Status.ShouldBe(ConsultationStatus.Cancelled);
    }

    [Theory]
    [InlineData(ConsultationStatus.Accepted)]
    [InlineData(ConsultationStatus.Rejected)]
    [InlineData(ConsultationStatus.Completed)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task Cancel_is_refused_unless_the_request_is_Pending(ConsultationStatus status)
    {
        var id = await AddConsultationAsync(Pat.PatientId, status);

        await ShouldFailWith("Consultation.CannotCancelAfterConfirmed",
            () => NewConsultService().CancelConsultaion(C.Id, Pat.UserId, id, null));

        await using var db = Db();
        (await db.Consultations.SingleAsync(x => x.Id == id)).Status.ShouldBe(status);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Paid)]
    public async Task Cancel_is_refused_once_a_payment_was_submitted(PaymentStatus paymentStatus)
    {
        var id = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        await AddConsultationPaymentAsync(id, Pat.PatientId, paymentStatus);

        await ShouldFailWith("Consultation.CannotCancelAfterPaymentSubmitted",
            () => NewConsultService().CancelConsultaion(C.Id, Pat.UserId, id, null));

        await using var db = Db();
        (await db.Consultations.SingleAsync(x => x.Id == id)).Status.ShouldBe(ConsultationStatus.Pending);
    }

    [Fact]
    public Task Cancel_Consultation_NotFound_for_an_unknown_id() =>
        ShouldFailWith("Consultation.NotFound", () => NewConsultService().CancelConsultaion(C.Id, Pat.UserId, 999_999, null));

    [Fact]
    public async Task Cancel_Consultation_NotFound_for_another_patients_request()
    {
        var other = await C.AddPatientAsync();
        var id = await AddConsultationAsync(other.PatientId);
        await ShouldFailWith("Consultation.NotFound", () => NewConsultService().CancelConsultaion(C.Id, Pat.UserId, id, null));
    }

    [Fact]
    public Task Cancel_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewConsultService().CancelConsultaion(C.Id, 999_999, 1, null));

    [Fact]
    public async Task Cancel_SubPatient_NotFoundOrNotOwned_for_someone_elses_child()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => NewConsultService().CancelConsultaion(C.Id, Pat.UserId, 1, child.PatientId));
    }

    [Fact]
    public Task Cancel_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().CancelConsultaion(999_999, Pat.UserId, 1, null));
}
