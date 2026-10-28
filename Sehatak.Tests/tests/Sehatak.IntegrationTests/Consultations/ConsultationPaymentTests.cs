using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.PaymentDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Consultations;

/// <summary>Second half of the life cycle: patient pays -> doctor confirms / rejects -> doctor completes.</summary>
[Collection(SqlServerCollection.Name)]
public class ConsultationPaymentTests(SqlServerFixture fx) : ConsultationTestBase(fx)
{
    private static PaymentRequestDto Req(string? reference = "REF-9", string? notes = "n", Microsoft.AspNetCore.Http.IFormFile? file = null)
        => new() { ReferenceNumber = reference, Notes = notes, ReceiptImageUrl = file };

    private Task<string> Pay(int consultationId, PaymentRequestDto? req = null, int? userId = null, int? sub = null)
        => NewConsultService().ConsultationRecordPayment(C.Id, consultationId, userId ?? Pat.UserId, req ?? Req(), sub);

    private Task<int> Scheduled(int? patientId = null, ConsultationStatus s = ConsultationStatus.Pending)
        => AddConsultationAsync(patientId ?? Pat.PatientId, s, scheduledAt: InTwoDays);

    // ------------------------------------------------------------------ ConsultationRecordPayment

    [Fact]
    public async Task RecordPayment_creates_a_Pending_payment_with_the_current_consultation_price()
    {
        var id = await Scheduled();

        (await Pay(id)).ShouldNotBeNullOrWhiteSpace();

        await using var db = Db();
        var p = await db.Payments.SingleAsync();
        p.ConsultationId.ShouldBe(id);
        p.PatientId.ShouldBe(Pat.PatientId);
        p.Amount.ShouldBe(60m);
        p.Type.ShouldBe(PaymentType.Consultation);
        p.Status.ShouldBe(PaymentStatus.Pending);
        p.Method.ShouldBe(PaymentMethod.online);
        p.ReferenceNumber.ShouldBe("REF-9");
        p.Notes.ShouldBe("n");
        p.RecordedByStaffId.ShouldBeNull();
        p.ReceiptImageUrl.ShouldBeNull();
        p.PaidAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1)); // stored as UTC
        (await db.AuditLogs.CountAsync(a => a.Action == AuditRecordPayment && a.EntityId == p.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task RecordPayment_uses_the_active_price_only()
    {
        await DeactivateConsultationPricesAsync();
        await AddConsultationPriceAsync(75m);
        var id = await Scheduled();

        await Pay(id);

        await using var db = Db();
        (await db.Payments.SingleAsync()).Amount.ShouldBe(75m);
    }

    [Fact]
    public async Task RecordPayment_for_a_child_is_stored_on_the_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await Scheduled(sub.PatientId);

        await Pay(id, sub: sub.PatientId);

        await using var db = Db();
        (await db.Payments.SingleAsync()).PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task RecordPayment_saves_a_valid_receipt_and_stores_its_url()
    {
        var id = await Scheduled();

        await Pay(id, Req(file: Upload("receipt.PNG")));

        await using var db = Db();
        var url = (await db.Payments.SingleAsync()).ReceiptImageUrl;
        url.ShouldNotBeNull();
        url.ShouldStartWith("/uploads/receipts/");
        url.ShouldEndWith(".PNG");
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("page.html")]
    [InlineData("noextension")]
    public async Task Validation_InvalidFileType_for_other_extensions(string name)
    {
        var id = await Scheduled();
        await ShouldFailWith("Validation.InvalidFileType", () => Pay(id, Req(file: Upload(name))));
        await using var db = Db();
        (await db.Payments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Validation_FileTooLarge_above_5MB()
    {
        var id = await Scheduled();
        await ShouldFailWith("Validation.FileTooLarge", () => Pay(id, Req(file: Upload("big.pdf", 5 * 1024 * 1024 + 1))));
    }

    [Fact]
    public async Task A_receipt_of_exactly_5MB_is_accepted()
    {
        var id = await Scheduled();
        await Pay(id, Req(file: Upload("edge.jpg", 5 * 1024 * 1024)));
        await using var db = Db();
        (await db.Payments.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Paid)]
    public async Task Payment_Exists_when_a_live_payment_is_already_there(PaymentStatus existing)
    {
        var id = await Scheduled();
        await AddConsultationPaymentAsync(id, Pat.PatientId, existing);

        await ShouldFailWith("Payment.Exists", () => Pay(id));

        await using var db = Db();
        (await db.Payments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Double_submit_creates_only_one_payment()
    {
        var id = await Scheduled();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await Pay(id); return true; } catch { return false; }
        }));

        results.Count(r => r).ShouldBe(1);
        await using var db = Db();
        (await db.Payments.CountAsync(p => p.ConsultationId == id)).ShouldBe(1);
    }

    /// <summary>
    /// After the doctor rejects a receipt the app tells the patient "please resend", and the service lets a new
    /// payment through (the Exists check ignores Failed) - but the unique index IX_payments_ConsultationId only
    /// allows ONE payment row per consultation, so the second insert throws DbUpdateException.
    /// Fix: when the old payment is Failed, update it back to Pending instead of inserting a new row.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_rejected_payment_can_be_resubmitted()
    {
        var id = await Scheduled();
        await AddConsultationPaymentAsync(id, Pat.PatientId, PaymentStatus.Failed, recordedByStaffId: Doc.UserId);

        await Pay(id, Req("REF-NEW"));

        await using var db = Db();
        (await db.Payments.Where(p => p.ConsultationId == id && p.Status == PaymentStatus.Pending).CountAsync()).ShouldBe(1);
    }

    [Fact]
    public Task RecordPayment_NotScheduledYet_when_the_doctor_has_not_picked_a_date()
    {
        return Task.Run(async () =>
        {
            var id = await AddConsultationAsync(Pat.PatientId);
            await ShouldFailWith("Consultation.NotScheduledYet", () => Pay(id));
        });
    }

    [Theory]
    [InlineData(ConsultationStatus.Accepted)]
    [InlineData(ConsultationStatus.Rejected)]
    [InlineData(ConsultationStatus.Completed)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task RecordPayment_NotScheduledYet_unless_the_request_is_Pending(ConsultationStatus status)
    {
        var id = await Scheduled(s: status);
        await ShouldFailWith("Consultation.NotScheduledYet", () => Pay(id));
    }

    [Fact]
    public async Task RecordPayment_NotScheduledYet_for_someone_elses_consultation()
    {
        var other = await C.AddPatientAsync();
        var id = await Scheduled(other.PatientId);
        await ShouldFailWith("Consultation.NotScheduledYet", () => Pay(id));
    }

    [Fact]
    public async Task ServicePrice_NotFound_without_an_active_consultation_price()
    {
        var id = await Scheduled();
        await DeactivateConsultationPricesAsync();
        await ShouldFailWith("ServicePrice.NotFound", () => Pay(id));
    }

    [Fact]
    public Task RecordPayment_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => Pay(1, userId: 999_999));

    [Fact]
    public async Task RecordPayment_SubPatient_NotFoundOrNotOwned()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => Pay(1, sub: child.PatientId));
    }

    [Fact]
    public Task RecordPayment_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().ConsultationRecordPayment(999_999, 1, Pat.UserId, Req(), null));

    // ------------------------------------------------------------------ ConfirmPaymentAsync (doctor)

    private async Task<(int consultation, int payment)> Submitted(DateTime? when = null)
    {
        var cid = await AddConsultationAsync(Pat.PatientId, scheduledAt: when ?? InTwoDays);
        var pid = await AddConsultationPaymentAsync(cid, Pat.PatientId);
        return (cid, pid);
    }

    [Fact]
    public async Task Confirm_marks_paid_accepts_the_consultation_and_notifies_with_the_link()
    {
        var (cid, pid) = await Submitted();

        (await NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, "https://meet.example/abc")).ShouldBeTrue();

        await using var db = Db();
        var p = await db.Payments.SingleAsync(x => x.Id == pid);
        p.Status.ShouldBe(PaymentStatus.Paid);
        p.RecordedByStaffId.ShouldBe(Doc.UserId);
        var c = await db.Consultations.SingleAsync(x => x.Id == cid);
        c.Status.ShouldBe(ConsultationStatus.Accepted);
        c.VideoLink.ShouldBe("https://meet.example/abc");
        var n = await db.Notifications.SingleAsync(x => x.UserId == Pat.UserId);
        n.Message.ShouldContain("https://meet.example/abc");
        (await db.AuditLogs.CountAsync(a => a.Action == AuditConfirmPayment && a.EntityId == pid)).ShouldBe(1);
    }

    [Fact]
    public async Task Confirm_without_a_link_keeps_an_existing_link_and_still_works()
    {
        var cid = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays, videoLink: "https://old");
        var pid = await AddConsultationPaymentAsync(cid, Pat.PatientId);

        await NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null);

        await using var db = Db();
        (await db.Consultations.SingleAsync(x => x.Id == cid)).VideoLink.ShouldBe("https://old");
    }

    [Fact]
    public async Task Confirm_without_any_link_sends_a_notification_without_a_link()
    {
        var (_, pid) = await Submitted();

        await NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null);

        await using var db = Db();
        (await db.Notifications.SingleAsync(x => x.UserId == Pat.UserId)).Message.ShouldNotContain("http");
    }

    [Fact]
    public async Task Confirm_notifies_the_parent_for_a_childs_consultation()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var cid = await AddConsultationAsync(sub.PatientId, scheduledAt: InTwoDays);
        var pid = await AddConsultationPaymentAsync(cid, sub.PatientId);

        await NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null);

        await using var db = Db();
        (await db.Notifications.CountAsync(x => x.UserId == Pat.UserId)).ShouldBe(1);
    }

    [Fact]
    public async Task Confirm_twice_second_call_is_refused()
    {
        var (_, pid) = await Submitted();
        await NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null);

        await ShouldFailWith("Payment.AlreadyConfirmed", () => NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null));
    }

    [Fact]
    public async Task Confirm_AlreadyProcessed_when_the_payment_is_not_Pending()
    {
        var cid = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        var pid = await AddConsultationPaymentAsync(cid, Pat.PatientId, PaymentStatus.Failed);

        await ShouldFailWith("Payment.AlreadyProcessed", () => NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null));
    }

    [Fact]
    public async Task Confirm_NotScheduledYet_when_the_date_is_missing()
    {
        var cid = await AddConsultationAsync(Pat.PatientId);
        var pid = await AddConsultationPaymentAsync(cid, Pat.PatientId);
        await ShouldFailWith("Consultation.NotScheduledYet", () => NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null));
    }

    [Fact]
    public async Task Confirm_Forbidden_for_another_doctors_consultation()
    {
        var (_, pid) = await Submitted();
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Auth.Forbidden", () => NewConsultService().ConfirmPaymentAsync(C.Id, pid, doc2.UserId, null));
        await using var db = Db();
        (await db.Payments.SingleAsync(x => x.Id == pid)).Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact]
    public async Task Confirm_Consultation_NotFound_for_a_payment_that_is_not_a_consultation_payment()
    {
        var pid = await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Appointment);
        await ShouldFailWith("Consultation.NotFound", () => NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null));
    }

    [Fact]
    public Task Confirm_Payment_NotFound() =>
        ShouldFailWith("Payment.NotFound", () => NewConsultService().ConfirmPaymentAsync(C.Id, 999_999, Doc.UserId, null));

    [Fact]
    public Task Confirm_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().ConfirmPaymentAsync(C.Id, 1, Pat.UserId, null));

    [Fact]
    public Task Confirm_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().ConfirmPaymentAsync(999_999, 1, Doc.UserId, null));

    [Fact]
    public async Task Parallel_confirms_only_one_wins()
    {
        var (cid, pid) = await Submitted();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { return await NewConsultService().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, null); } catch { return false; }
        }));

        // The service has no concurrency guard: if this goes red, add a RowVersion / conditional update on the payment.
        results.Count(r => r).ShouldBe(1);
        await using var db = Db();
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId)).ShouldBe(1);
        (await db.Consultations.SingleAsync(x => x.Id == cid)).Status.ShouldBe(ConsultationStatus.Accepted);
    }

    // ------------------------------------------------------------------ RejectConsultationPaymentAsync

    [Fact]
    public async Task Reject_payment_marks_Failed_keeps_the_consultation_Pending_and_notifies()
    {
        var (cid, pid) = await Submitted();

        (await NewConsultService().RejectConsultationPaymentAsync(C.Id, pid, Doc.UserId, "blurry receipt")).ShouldNotBeNullOrWhiteSpace();

        await using var db = Db();
        var p = await db.Payments.SingleAsync(x => x.Id == pid);
        p.Status.ShouldBe(PaymentStatus.Failed);
        p.Notes.ShouldBe("blurry receipt");
        p.RecordedByStaffId.ShouldBe(Doc.UserId);
        (await db.Consultations.SingleAsync(x => x.Id == cid)).Status.ShouldBe(ConsultationStatus.Pending);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == AuditRejectPayment && a.EntityId == pid)).ShouldBe(1);
    }

    [Theory]
    [InlineData(PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Failed)]
    public async Task Reject_payment_AlreadyProcessed(PaymentStatus status)
    {
        var cid = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        var pid = await AddConsultationPaymentAsync(cid, Pat.PatientId, status);
        await ShouldFailWith("Payment.AlreadyProcessed", () => NewConsultService().RejectConsultationPaymentAsync(C.Id, pid, Doc.UserId, "x"));
    }

    [Fact]
    public async Task Reject_payment_Forbidden_for_another_doctor()
    {
        var (_, pid) = await Submitted();
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Auth.Forbidden", () => NewConsultService().RejectConsultationPaymentAsync(C.Id, pid, doc2.UserId, "x"));
    }

    [Fact]
    public async Task Reject_payment_Consultation_NotFound_for_a_non_consultation_payment()
    {
        var pid = await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Appointment);
        await ShouldFailWith("Consultation.NotFound", () => NewConsultService().RejectConsultationPaymentAsync(C.Id, pid, Doc.UserId, "x"));
    }

    [Fact]
    public Task Reject_payment_Payment_NotFound() =>
        ShouldFailWith("Payment.NotFound", () => NewConsultService().RejectConsultationPaymentAsync(C.Id, 999_999, Doc.UserId, "x"));

    [Fact]
    public Task Reject_payment_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().RejectConsultationPaymentAsync(C.Id, 1, Pat.UserId, "x"));

    [Fact]
    public Task Reject_payment_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().RejectConsultationPaymentAsync(999_999, 1, Doc.UserId, "x"));

    // ------------------------------------------------------------------ CompleteConsultation

    [Fact]
    public async Task Doctor_completes_an_accepted_consultation()
    {
        var id = await Scheduled(s: ConsultationStatus.Accepted);

        (await NewConsultService().CompleteConsultation(C.Id, Doc.UserId, id)).ShouldNotBeNullOrWhiteSpace();

        await using var db = Db();
        (await db.Consultations.SingleAsync(x => x.Id == id)).Status.ShouldBe(ConsultationStatus.Completed);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == AuditComplete && a.EntityId == id)).ShouldBe(1);
    }

    [Theory]
    [InlineData(ConsultationStatus.Pending)]
    [InlineData(ConsultationStatus.Rejected)]
    [InlineData(ConsultationStatus.Completed)]
    [InlineData(ConsultationStatus.Cancelled)]
    public async Task Complete_Consultation_NotFound_unless_Accepted(ConsultationStatus status)
    {
        var id = await Scheduled(s: status);
        await ShouldFailWith("Consultation.NotFound", () => NewConsultService().CompleteConsultation(C.Id, Doc.UserId, id));
    }

    [Fact]
    public async Task Complete_Consultation_NotFound_for_another_doctor()
    {
        var id = await Scheduled(s: ConsultationStatus.Accepted);
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Consultation.NotFound", () => NewConsultService().CompleteConsultation(C.Id, doc2.UserId, id));
    }

    [Fact]
    public Task Complete_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().CompleteConsultation(C.Id, Pat.UserId, 1));

    [Fact]
    public Task Complete_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().CompleteConsultation(999_999, Doc.UserId, 1));

    // ------------------------------------------------------------------ Full journey

    [Fact]
    public async Task Full_flow_request_schedule_pay_reject_is_blocked_then_confirm_complete()
    {
        var svc = () => NewConsultService();
        await svc().ConsultationRequest(C.Id, Doc.DoctorId, Pat.UserId, null);
        int cid; await using (var db = Db()) cid = (await db.Consultations.SingleAsync()).Id;

        await svc().DoctorScheduleConsultationAsync(C.Id, Doc.UserId, cid, InTwoDays);
        await svc().ConsultationRecordPayment(C.Id, cid, Pat.UserId, Req(), null);
        int pid; await using (var db = Db()) pid = (await db.Payments.SingleAsync()).Id;

        await svc().ConfirmPaymentAsync(C.Id, pid, Doc.UserId, "https://meet.example/x");
        await svc().CompleteConsultation(C.Id, Doc.UserId, cid);

        await using var end = Db();
        (await end.Consultations.SingleAsync()).Status.ShouldBe(ConsultationStatus.Completed);
        (await end.Payments.SingleAsync()).Status.ShouldBe(PaymentStatus.Paid);
    }
}
