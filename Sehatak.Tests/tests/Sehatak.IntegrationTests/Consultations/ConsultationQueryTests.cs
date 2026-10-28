using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Consultations;

/// <summary>Read-only endpoints: doctor directory, doctor's schedule, pending payments, patient views.</summary>
[Collection(SqlServerCollection.Name)]
public class ConsultationQueryTests(SqlServerFixture fx) : ConsultationTestBase(fx)
{
    private static PagedRequest Page(int n = 1, int size = 10) => new() { PageNumber = n, PageSize = size };

    // ------------------------------------------------------------------ GetDoctorEnableConsultation

    [Fact]
    public async Task Directory_lists_only_active_online_enabled_doctors()
    {
        var offline = await C.AddDoctorAsync();                    // OnlineEnabled stays false
        var inactive = await C.AddDoctorAsync(active: false);
        await SetOnlineAsync(inactive.DoctorId, true);

        var r = await NewConsultService().GetDoctorEnableConsultation(C.Id, Page());

        r.TotalCount.ShouldBe(1);
        r.Items.Single().doctorId.ShouldBe(Doc.DoctorId);
    }

    [Fact]
    public async Task Directory_pages_results()
    {
        for (var i = 0; i < 4; i++) await SetOnlineAsync((await C.AddDoctorAsync()).DoctorId, true);

        var r = await NewConsultService().GetDoctorEnableConsultation(C.Id, Page(2, 2));

        r.TotalCount.ShouldBe(5);
        r.Items.Count().ShouldBe(2);
        r.TotalPages.ShouldBe(3);
    }

    [Fact]
    public Task Directory_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().GetDoctorEnableConsultation(999_999, Page()));

    // ------------------------------------------------------------------ GetConsultationsScheduale (doctor, Accepted only)

    [Fact]
    public async Task Doctor_schedule_shows_only_own_Accepted_consultations_newest_first()
    {
        var older = await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Accepted, DateTime.UtcNow.AddDays(1), videoLink: "l1");
        var newer = await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Accepted, DateTime.UtcNow.AddDays(3));
        await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Pending, DateTime.UtcNow.AddDays(2));
        await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Completed, DateTime.UtcNow.AddDays(2));
        var doc2 = await C.AddDoctorAsync();
        await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Accepted, DateTime.UtcNow.AddDays(2), doctorId: doc2.DoctorId);

        var r = await NewConsultService().GetConsultationsScheduale(C.Id, Doc.UserId, Page());

        r.Items.Select(i => i.ConsultationId).ToArray().ShouldBe(new[] { newer, older });
        r.Items.Last().VideoLink.ShouldBe("l1");
        r.Items.First().patientName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public Task Doctor_schedule_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().GetConsultationsScheduale(C.Id, Pat.UserId, Page()));

    [Fact]
    public Task Doctor_schedule_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().GetConsultationsScheduale(999_999, Doc.UserId, Page()));

    // ------------------------------------------------------------------ GetPaymentPinding (list)

    [Fact]
    public async Task Pending_payments_list_has_only_this_doctors_pending_payments_on_pending_consultations()
    {
        var c1 = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        var p1 = await AddConsultationPaymentAsync(c1, Pat.PatientId, receiptUrl: "/uploads/receipts/a.png");
        var c2 = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        await AddConsultationPaymentAsync(c2, Pat.PatientId, PaymentStatus.Failed);
        var c3 = await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Accepted, InTwoDays);
        await AddConsultationPaymentAsync(c3, Pat.PatientId, PaymentStatus.Paid);
        var doc2 = await C.AddDoctorAsync();
        var c4 = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays, doctorId: doc2.DoctorId);
        await AddConsultationPaymentAsync(c4, Pat.PatientId);

        var r = await NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, Page());

        r.TotalCount.ShouldBe(1);
        var item = r.Items.Single();
        item.Id.ShouldBe(p1);
        item.ReceiptImageUrl.ShouldBe("/uploads/receipts/a.png");
        item.ReferenceNumber.ShouldBe("REF-1");
        item.PatientName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Pending_payments_list_is_newest_first()
    {
        var c1 = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        var old = await AddConsultationPaymentAsync(c1, Pat.PatientId, paidAtUtc: DateTime.UtcNow.AddHours(-3));
        var other = await C.AddPatientAsync();
        var c2 = await AddConsultationAsync(other.PatientId, scheduledAt: InTwoDays);
        var recent = await AddConsultationPaymentAsync(c2, other.PatientId, paidAtUtc: DateTime.UtcNow.AddHours(-1));

        var r = await NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, Page());

        r.Items.Select(i => i.Id).ToArray().ShouldBe(new[] { recent, old });
    }

    [Fact]
    public Task Pending_payments_list_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().GetPaymentPinding(C.Id, Pat.UserId, Page()));

    [Fact]
    public Task Pending_payments_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().GetPaymentPinding(999_999, Doc.UserId, Page()));

    // ------------------------------------------------------------------ GetPaymentPinding (single)

    private async Task<int> PendingPaymentAsync()
    {
        var c = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        return await AddConsultationPaymentAsync(c, Pat.PatientId, receiptUrl: "/uploads/receipts/z.png");
    }

    [Fact]
    public async Task Single_pending_payment_returns_its_details()
    {
        var pid = await PendingPaymentAsync();

        var dto = await NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, pid);

        dto.Id.ShouldBe(pid);
        dto.patientId.ShouldBe(Pat.PatientId);
        dto.ReferenceNumber.ShouldBe("REF-1");
        dto.ReceiptImageUrl.ShouldBe("/uploads/receipts/z.png");
    }

    /// <summary>The list returns PatientName, the single-item version forgets to fill it (the DTO field stays null).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Single_pending_payment_includes_the_patient_name()
    {
        var pid = await PendingPaymentAsync();
        (await NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, pid)).PatientName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Single_pending_payment_Forbidden_for_another_doctor()
    {
        var pid = await PendingPaymentAsync();
        var doc2 = await C.AddDoctorAsync();
        await ShouldFailWith("Auth.Forbidden", () => NewConsultService().GetPaymentPinding(C.Id, doc2.UserId, pid));
    }

    [Fact]
    public async Task Single_payment_AlreadyProcessed()
    {
        var c = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays);
        var pid = await AddConsultationPaymentAsync(c, Pat.PatientId, PaymentStatus.Failed);
        await ShouldFailWith("Payment.AlreadyProcessed", () => NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, pid));
    }

    [Fact]
    public async Task Single_payment_Consultation_NotPending()
    {
        var c = await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Accepted, InTwoDays);
        var pid = await AddConsultationPaymentAsync(c, Pat.PatientId);
        await ShouldFailWith("Consultation.NotPending", () => NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, pid));
    }

    [Fact]
    public async Task Single_payment_Consultation_NotFound_for_a_non_consultation_payment()
    {
        var pid = await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Appointment);
        await ShouldFailWith("Consultation.NotFound", () => NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, pid));
    }

    [Fact]
    public Task Single_payment_Payment_NotFound() =>
        ShouldFailWith("Payment.NotFound", () => NewConsultService().GetPaymentPinding(C.Id, Doc.UserId, 999_999));

    [Fact]
    public Task Single_payment_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().GetPaymentPinding(C.Id, Pat.UserId, 1));

    // ------------------------------------------------------------------ ViewConsultation (patient, one)

    [Fact]
    public async Task View_returns_status_and_payment_status()
    {
        var cid = await AddConsultationAsync(Pat.PatientId, scheduledAt: InTwoDays, videoLink: "v");
        await AddConsultationPaymentAsync(cid, Pat.PatientId, PaymentStatus.Pending);

        var v = await NewConsultService().ViewConsultation(C.Id, Doc.DoctorId, Pat.UserId, cid, null);

        v.ShouldNotBeNull();
        v.Id.ShouldBe(cid);
        v.Status.ShouldBe(ConsultationStatus.Pending);
        v.PaymentStatus.ShouldBe(PaymentStatus.Pending);
        v.VideoLink.ShouldBe("v");
        v.ScheduledAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task View_has_null_payment_status_before_paying()
    {
        var cid = await AddConsultationAsync(Pat.PatientId);
        (await NewConsultService().ViewConsultation(C.Id, Doc.DoctorId, Pat.UserId, cid, null))!.PaymentStatus.ShouldBeNull();
    }

    [Fact]
    public async Task View_returns_null_for_someone_elses_consultation_or_the_wrong_doctor()
    {
        var other = await C.AddPatientAsync();
        var theirs = await AddConsultationAsync(other.PatientId);
        var mine = await AddConsultationAsync(Pat.PatientId);
        var doc2 = await C.AddDoctorAsync();

        (await NewConsultService().ViewConsultation(C.Id, Doc.DoctorId, Pat.UserId, theirs, null)).ShouldBeNull();
        (await NewConsultService().ViewConsultation(C.Id, doc2.DoctorId, Pat.UserId, mine, null)).ShouldBeNull();
    }

    [Fact]
    public async Task View_for_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var cid = await AddConsultationAsync(sub.PatientId);
        (await NewConsultService().ViewConsultation(C.Id, Doc.DoctorId, Pat.UserId, cid, sub.PatientId))!.patientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task View_SubPatient_NotFoundOrNotOwned()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewConsultService().ViewConsultation(C.Id, Doc.DoctorId, Pat.UserId, 1, child.PatientId));
    }

    [Fact]
    public Task View_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewConsultService().ViewConsultation(C.Id, 999_999, Pat.UserId, 1, null));

    [Fact]
    public Task View_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewConsultService().ViewConsultation(C.Id, Doc.DoctorId, 999_999, 1, null));

    [Fact]
    public Task View_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().ViewConsultation(999_999, Doc.DoctorId, Pat.UserId, 1, null));

    // ------------------------------------------------------------------ ViewConsultations (patient, list by status)

    [Fact]
    public async Task List_filters_by_status_and_owner_newest_first()
    {
        var a = await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Completed, DateTime.UtcNow.AddDays(-5));
        var b = await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Completed, DateTime.UtcNow.AddDays(-1));
        await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Pending);
        var other = await C.AddPatientAsync();
        await AddConsultationAsync(other.PatientId, ConsultationStatus.Completed, DateTime.UtcNow.AddDays(-2));
        var sub = await C.AddSubPatientAsync(Pat);
        await AddConsultationAsync(sub.PatientId, ConsultationStatus.Completed, DateTime.UtcNow.AddDays(-3));

        var r = await NewConsultService().ViewConsultations(C.Id, Pat.UserId, ConsultationStatus.Completed, Page(), null);

        r.Items.Select(i => i.Id).ToArray().ShouldBe(new[] { b, a });
    }

    [Fact]
    public async Task List_for_a_child_returns_only_the_childs()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Pending);
        var kid = await AddConsultationAsync(sub.PatientId, ConsultationStatus.Pending);

        var r = await NewConsultService().ViewConsultations(C.Id, Pat.UserId, ConsultationStatus.Pending, Page(), sub.PatientId);

        r.Items.Single().Id.ShouldBe(kid);
    }

    [Fact]
    public async Task List_pages_results()
    {
        for (var i = 0; i < 5; i++) await AddConsultationAsync(Pat.PatientId, ConsultationStatus.Cancelled, DateTime.UtcNow.AddDays(i));

        var r = await NewConsultService().ViewConsultations(C.Id, Pat.UserId, ConsultationStatus.Cancelled, Page(3, 2), null);

        r.TotalCount.ShouldBe(5);
        r.Items.Count().ShouldBe(1);
    }

    [Fact]
    public Task List_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewConsultService().ViewConsultations(C.Id, 999_999, ConsultationStatus.Pending, Page(), null));

    [Fact]
    public async Task List_SubPatient_NotFoundOrNotOwned()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => NewConsultService().ViewConsultations(C.Id, Pat.UserId, ConsultationStatus.Pending, Page(), child.PatientId));
    }

    [Fact]
    public Task List_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewConsultService().ViewConsultations(999_999, Pat.UserId, ConsultationStatus.Pending, Page(), null));
}
