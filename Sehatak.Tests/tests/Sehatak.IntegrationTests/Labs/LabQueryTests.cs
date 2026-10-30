using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Labs;

/// <summary>Read-only endpoints: doctor, technician, receptionist and patient views.</summary>
[Collection(SqlServerCollection.Name)]
public class LabQueryTests(SqlServerFixture fx) : LabTestBase(fx)
{
    private static PagedRequest Page(int n = 1, int size = 10) => new() { PageNumber = n, PageSize = size };

    /// <summary>Gives every request a distinct, increasing RequstedAt so "newest first" is testable.</summary>
    private async Task StampAsync(params (int Id, int HoursAgo)[] stamps)
    {
        await using var db = Db();
        foreach (var (id, h) in stamps)
            (await db.LabRequests.SingleAsync(l => l.Id == id)).RequstedAt = DateTime.UtcNow.AddHours(-h);
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ DoctorGetLabResultsForPatient

    [Fact]
    public async Task Doctor_reads_the_results_of_a_Completed_request()
    {
        var (id, items) = await SeedLabAsync(LabRequestStatus.Completed);
        await using (var db = Db())
        {
            var it = await db.LabRequestItems.Where(i => i.LabRequestId == id).OrderBy(i => i.Id).ToListAsync();
            it[0].ResultValue = 4.5m; it[0].ResultFileUrl = "/uploads/labResult/x.pdf"; it[1].ResultValue = 90m;
            await db.SaveChangesAsync();
        }

        var r = await NewLabService().DoctorGetLabResultsForPatient(C.Id, Doc.UserId, Pat.PatientId, id);

        r.LabRequestId.ShouldBe(id);
        r.PatientId.ShouldBe(Pat.PatientId);
        r.AppointmentId.ShouldBe(Appt);
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.LabItems!.Count.ShouldBe(2);
        r.LabItems!.Select(i => i.ResultValue).ShouldBe(new decimal?[] { 4.5m, 90m }, ignoreOrder: true);
        r.LabItems!.Select(i => i.ServicePriceName).ShouldBe(new[] { "CBC", "Glucose" }, ignoreOrder: true);
    }

    [Theory]
    [InlineData(LabRequestStatus.Pending)]
    [InlineData(LabRequestStatus.Collected)]
    [InlineData(LabRequestStatus.Processing)]
    [InlineData(LabRequestStatus.Cancelled)]
    public async Task LabResult_NotFound_unless_the_request_is_Completed(LabRequestStatus status)
    {
        var (id, _) = await SeedLabAsync(status);
        await ShouldFailWith("LabResult.NotFound", () => NewLabService().DoctorGetLabResultsForPatient(C.Id, Doc.UserId, Pat.PatientId, id));
    }

    [Fact]
    public async Task LabResult_NotFound_when_the_request_belongs_to_another_patient()
    {
        var other = await C.AddPatientAsync();
        var (id, _) = await SeedLabAsync(LabRequestStatus.Completed);
        await ShouldFailWith("LabResult.NotFound", () => NewLabService().DoctorGetLabResultsForPatient(C.Id, Doc.UserId, other.PatientId, id));
    }

    [Fact]
    public Task Results_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewLabService().DoctorGetLabResultsForPatient(C.Id, Doc.UserId, 999_999, 1));

    [Fact]
    public async Task Results_Patient_NotFound_for_an_inactive_patient()
    {
        var gone = await C.AddPatientAsync(active: false);
        await ShouldFailWith("Patient.NotFound", () => NewLabService().DoctorGetLabResultsForPatient(C.Id, Doc.UserId, gone.PatientId, 1));
    }

    [Fact]
    public Task Results_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewLabService().DoctorGetLabResultsForPatient(C.Id, Rec, Pat.PatientId, 1));

    [Fact]
    public Task Results_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().DoctorGetLabResultsForPatient(999_999, Doc.UserId, Pat.PatientId, 1));

    // ------------------------------------------------------------------ GetLabRequestForPatientAsync (doctor)

    [Fact]
    public async Task Doctor_lists_a_patients_requests_newest_first_with_totals_of_available_tests()
    {
        var (old, _) = await SeedLabAsync(LabRequestStatus.Completed);
        var (recent, _) = await SeedLabAsync(LabRequestStatus.Collected, items: [(Svc1, 10m, true), (Svc2, 20m, false), (Svc3, 30m, true)]);
        var other = await C.AddPatientAsync();
        await SeedLabAsync(patientId: other.PatientId);
        await StampAsync((old, 5), (recent, 1));

        var r = await NewLabService().GetLabRequestForPatientAsync(C.Id, Doc.UserId, Pat.PatientId, Page());

        r.Items.Select(i => i.LabRequestId).ToArray().ShouldBe(new[] { recent, old });
        r.Items[0].TotalPrice.ShouldBe(40m);               // the unavailable test (20) is not counted
        r.Items[0].LabItems!.Count.ShouldBe(3);
        r.Items[0].LabStatus.ShouldBe("Collected");
        r.Items[1].TotalPrice.ShouldBe(30m);
    }

    [Fact]
    public async Task Doctor_list_pages_results()
    {
        for (var i = 0; i < 5; i++) await SeedLabAsync();
        var r = await NewLabService().GetLabRequestForPatientAsync(C.Id, Doc.UserId, Pat.PatientId, Page(3, 2));
        r.TotalCount.ShouldBe(5);
        r.Items.Count.ShouldBe(1);
    }

    [Fact]
    public Task Doctor_list_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewLabService().GetLabRequestForPatientAsync(C.Id, Doc.UserId, 999_999, Page()));

    [Fact]
    public Task Doctor_list_Doctor_NotFound() =>
        ShouldFailWith("Doctor.NotFound", () => NewLabService().GetLabRequestForPatientAsync(C.Id, Rec, Pat.PatientId, Page()));

    [Fact]
    public Task Doctor_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().GetLabRequestForPatientAsync(999_999, Doc.UserId, Pat.PatientId, Page()));

    // ------------------------------------------------------------------ Technician: LabGetPendingRequestsAsync / labGetRequestAsync

    [Fact]
    public async Task Technician_queue_has_Pending_Collected_and_Processing_newest_first()
    {
        var (a, _) = await SeedLabAsync(LabRequestStatus.Pending);
        var (b, _) = await SeedLabAsync(LabRequestStatus.Collected);
        var (c, _) = await SeedLabAsync(LabRequestStatus.Processing);
        await SeedLabAsync(LabRequestStatus.Completed);
        await SeedLabAsync(LabRequestStatus.Cancelled);
        await StampAsync((a, 3), (b, 2), (c, 1));

        var r = await NewLabService().LabGetPendingRequestsAsync(C.Id, Tech, Page());

        r.TotalCount.ShouldBe(3);
        r.Items.Select(i => i.LabRequestId).ToArray().ShouldBe(new[] { c, b, a });
        r.Items.ShouldAllBe(i => !string.IsNullOrWhiteSpace(i.PatientName));
    }

    [Fact]
    public Task Queue_Technician_NotFound() =>
        ShouldFailWith("Technician.NotFound", () => NewLabService().LabGetPendingRequestsAsync(C.Id, 999_999, Page()));

    [Fact]
    public Task Queue_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().LabGetPendingRequestsAsync(999_999, Tech, Page()));

    [Fact]
    public async Task Technician_opens_one_request_with_items_and_total()
    {
        var (id, items) = await SeedLabAsync(items: [(Svc1, 10m, true), (Svc2, 20m, false)]);

        var r = await NewLabService().labGetRequestAsync(C.Id, Tech, id);

        r.LabRequestId.ShouldBe(id);
        r.PatientId.ShouldBe(Pat.PatientId);
        r.LabStatus.ShouldBe("Pending");
        r.PatientName.ShouldNotBeNullOrWhiteSpace();
        r.LabItems!.Count.ShouldBe(2);
        r.TotalPrice.ShouldBe(10m);
        r.LabItems!.Single(i => i.LabRequestItemId == items[1]).IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public Task Open_LabRequest_NotFound() =>
        ShouldFailWith("LabRequest.NotFound", () => NewLabService().labGetRequestAsync(C.Id, Tech, 999_999));

    [Fact]
    public Task Open_Technician_NotFound() =>
        ShouldFailWith("Technician.NotFound", () => NewLabService().labGetRequestAsync(C.Id, 999_999, 1));

    [Fact]
    public Task Open_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().labGetRequestAsync(999_999, Tech, 1));

    // ------------------------------------------------------------------ Receptionist: awaiting payment

    [Fact]
    public async Task Receptionist_sees_only_Collected_requests_with_their_items()
    {
        var (collected, _) = await SeedLabAsync(LabRequestStatus.Collected, items: [(Svc1, 10m, true), (Svc3, 30m, false)]);
        await SeedLabAsync(LabRequestStatus.Pending);
        await SeedLabAsync(LabRequestStatus.Processing);
        await SeedLabAsync(LabRequestStatus.Completed);

        var r = await NewLabService().ReceptionistGetLabRequestsAwaitingPaymentAsync(C.Id, Rec, Page());

        r.TotalCount.ShouldBe(1);
        var item = r.Items.Single();
        item.LabRequestId.ShouldBe(collected);
        item.TotalPrice.ShouldBe(10m);
        item.LabItems!.Count.ShouldBe(2);
        item.PatientName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Awaiting_payment_is_newest_first()
    {
        var (a, _) = await SeedLabAsync(LabRequestStatus.Collected);
        var (b, _) = await SeedLabAsync(LabRequestStatus.Collected);
        await StampAsync((a, 4), (b, 1));

        var r = await NewLabService().ReceptionistGetLabRequestsAwaitingPaymentAsync(C.Id, Rec, Page());

        r.Items.Select(i => i.LabRequestId).ToArray().ShouldBe(new[] { b, a });
    }

    [Fact]
    public Task Awaiting_Receptionist_NotFound_for_a_doctor() =>
        ShouldFailWith("Receptionist.NotFound", () => NewLabService().ReceptionistGetLabRequestsAwaitingPaymentAsync(C.Id, Doc.UserId, Page()));

    [Fact]
    public Task Awaiting_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().ReceptionistGetLabRequestsAwaitingPaymentAsync(999_999, Rec, Page()));

    // ------------------------------------------------------------------ Patient: PatientGetLabRequestAsync

    [Fact]
    public async Task Patient_sees_only_his_own_requests_newest_first()
    {
        var (a, _) = await SeedLabAsync();
        var (b, _) = await SeedLabAsync(LabRequestStatus.Cancelled);
        var other = await C.AddPatientAsync();
        await SeedLabAsync(patientId: other.PatientId);
        var sub = await C.AddSubPatientAsync(Pat);
        await SeedLabAsync(patientId: sub.PatientId);
        await StampAsync((a, 3), (b, 1));

        var r = await NewLabService().PatientGetLabRequestAsync(C.Id, Pat.UserId, Page(), null);

        r.Items.Select(i => i.LabRequestId).ToArray().ShouldBe(new[] { b, a });
        r.Items[1].TotalPrice.ShouldBe(30m);
        r.Items[1].LabItems!.Count.ShouldBe(2);
        r.Items[1].PatientName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Patient_sees_the_childs_requests_when_a_child_is_selected()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await SeedLabAsync();
        var (kid, _) = await SeedLabAsync(patientId: sub.PatientId);

        var r = await NewLabService().PatientGetLabRequestAsync(C.Id, Pat.UserId, Page(), sub.PatientId);

        r.Items.Single().LabRequestId.ShouldBe(kid);
        r.Items.Single().PatientId.ShouldBe(sub.PatientId);
    }

    [Fact]
    public async Task Patient_requests_SubPatient_NotFoundOrNotOwned()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewLabService().PatientGetLabRequestAsync(C.Id, Pat.UserId, Page(), child.PatientId));
    }

    [Fact]
    public Task Patient_requests_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewLabService().PatientGetLabRequestAsync(C.Id, 999_999, Page(), null));

    [Fact]
    public Task Patient_requests_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().PatientGetLabRequestAsync(999_999, Pat.UserId, Page(), null));

    // ------------------------------------------------------------------ Patient: PatientGetLabResultAsync

    [Fact]
    public async Task Patient_results_contain_only_Completed_requests_with_their_values()
    {
        var (done, items) = await SeedLabAsync(LabRequestStatus.Completed);
        await SeedLabAsync(LabRequestStatus.Processing);
        await SeedLabAsync(LabRequestStatus.Collected);
        await using (var db = Db())
        {
            (await db.LabRequestItems.SingleAsync(i => i.Id == items[0])).ResultValue = 12.5m;
            await db.SaveChangesAsync();
        }

        var r = await NewLabService().PatientGetLabResultAsync(C.Id, Pat.UserId, Page(), null);

        r.TotalCount.ShouldBe(1);
        r.Items.Single().LabRequestId.ShouldBe(done);
        r.Items.Single().labResultItem.Count.ShouldBe(2);
        r.Items.Single().labResultItem.Single(i => i.LabRequestItemId == items[0]).ResultValue.ShouldBe(12.5m);
    }

    [Fact]
    public async Task Patient_results_for_a_child_and_never_the_parents()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await SeedLabAsync(LabRequestStatus.Completed);
        var (kid, _) = await SeedLabAsync(LabRequestStatus.Completed, patientId: sub.PatientId);

        var r = await NewLabService().PatientGetLabResultAsync(C.Id, Pat.UserId, Page(), sub.PatientId);

        r.Items.Single().LabRequestId.ShouldBe(kid);
    }

    [Fact]
    public async Task Patient_results_SubPatient_NotFoundOrNotOwned()
    {
        var stranger = await C.AddPatientAsync();
        var child = await C.AddSubPatientAsync(stranger);
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned", () => NewLabService().PatientGetLabResultAsync(C.Id, Pat.UserId, Page(), child.PatientId));
    }

    [Fact]
    public Task Patient_results_Patient_NotFound() =>
        ShouldFailWith("Patient.NotFound", () => NewLabService().PatientGetLabResultAsync(C.Id, 999_999, Page(), null));

    [Fact]
    public Task Patient_results_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().PatientGetLabResultAsync(999_999, Pat.UserId, Page(), null));

    // ------------------------------------------------------------------ Whole journey

    [Fact]
    public async Task Full_journey_create_collect_pay_upload_view()
    {
        var created = await NewLabService().CreateLabRequestAsync(C.Id, Doc.UserId,
            new() { PatientId = Pat.PatientId, AppointmentId = Appt, LabItems = [new() { Id = Svc1 }, new() { Id = Svc2 }] });

        await NewLabService().LabCollectSample(C.Id, Tech, created.LabRequestId, null);
        await NewConfirmService().ReceptionistPayLabRequestAsync(C.Id, Rec, created.LabRequestId,
            new Sehatak.Application.DTOs.ConfirmPaymentDto.CollectPaymentRequestDto { Method = Sehatak.Domain.Enums.PaymentEnums.PaymentMethod.cash });

        int[] itemIds;
        await using (var db = Db()) itemIds = await db.LabRequestItems.OrderBy(i => i.Id).Select(i => i.Id).ToArrayAsync();
        var up = await NewLabService().LabUploadResult(C.Id, Tech, new()
        {
            LabRequestId = created.LabRequestId,
            Results = itemIds.Select(i => new Sehatak.Application.DTOs.LabDto.LabResultItemDto { LabRequestItemId = i, ResultValue = 1m }).ToList()
        });

        up.LabStatus.ShouldBe("Completed");
        (await NewLabService().PatientGetLabResultAsync(C.Id, Pat.UserId, Page(), null)).TotalCount.ShouldBe(1);
        (await NewLabService().DoctorGetLabResultsForPatient(C.Id, Doc.UserId, Pat.PatientId, created.LabRequestId)).LabItems!.Count.ShouldBe(2);
    }
}
