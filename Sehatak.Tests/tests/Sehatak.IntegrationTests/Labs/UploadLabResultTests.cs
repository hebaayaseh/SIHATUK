using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Labs;

/// <summary>The technician uploads results; the request completes when every available test has a value.</summary>
[Collection(SqlServerCollection.Name)]
public class UploadLabResultTests(SqlServerFixture fx) : LabTestBase(fx)
{
    private Task<LabUploadResultResponseDto> Upload(int labId, int? userId = null, params LabResultItemDto[] results) =>
        NewLabService().LabUploadResult(C.Id, userId ?? Tech, new UploadLabResultRequestDto { LabRequestId = labId, Results = results.ToList() });

    private static LabResultItemDto R(int itemId, decimal value, Microsoft.AspNetCore.Http.IFormFile? file = null) =>
        new() { LabRequestItemId = itemId, ResultValue = value, ResultFileUrl = file };

    [Fact]
    public async Task Uploading_every_result_completes_the_request()
    {
        var (id, items) = await SeedProcessingLabAsync();

        var r = await Upload(id, null, R(items[0], 4.5m), R(items[1], 90m));

        r.LabStatus.ShouldBe("Completed");
        r.LabItems.Select(i => i.ResultValue).ShouldBe(new decimal?[] { 4.5m, 90m }, ignoreOrder: true);

        await using var db = Db();
        var lab = await db.LabRequests.SingleAsync();
        lab.Status.ShouldBe(LabRequestStatus.Completed);
        ShouldBeNowUtc(lab.UpdatedAt);
        var result = await db.LabResults.SingleAsync();
        result.LabRequestId.ShouldBe(id);
        result.PatientId.ShouldBe(Pat.PatientId);
        result.TechnicianId.ShouldBe(Tech);
        result.Status.ShouldBe(LabStatus.Delivered);
        result.PaymentId.ShouldBe((await db.Payments.SingleAsync(p => p.LabRequestId == id)).Id);
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.LabResult)).ShouldBe(1);
        (await db.AuditLogs.CountAsync(a => a.Action == "UploadLabRequest" && a.EntityId == id)).ShouldBe(1);
    }

    [Fact]
    public async Task A_partial_upload_keeps_Processing_and_the_second_upload_completes_it()
    {
        var (id, items) = await SeedProcessingLabAsync();

        var first = await Upload(id, null, R(items[0], 1m));
        first.LabStatus.ShouldBe("Processing");
        await using (var db = Db())
        {
            (await db.LabResults.CountAsync()).ShouldBe(0);
            (await db.Notifications.CountAsync(n => n.Type == NotificationType.LabResult)).ShouldBe(0);
        }

        var second = await Upload(id, null, R(items[1], 2m));
        second.LabStatus.ShouldBe("Completed");
        await using var db2 = Db();
        (await db2.LabResults.CountAsync()).ShouldBe(1);
        (await db2.Notifications.CountAsync(n => n.Type == NotificationType.LabResult)).ShouldBe(1);
    }

    [Fact]
    public async Task Unavailable_tests_do_not_block_completion()
    {
        var (id, items) = await SeedProcessingLabAsync(null, (Svc1, 10m, true), (Svc2, 20m, false));

        var r = await Upload(id, null, R(items[0], 7m));

        r.LabStatus.ShouldBe("Completed");
    }

    [Fact]
    public async Task LabRequestItem_NotAvailable_when_uploading_for_an_unavailable_test()
    {
        var (id, items) = await SeedProcessingLabAsync(null, (Svc1, 10m, true), (Svc2, 20m, false));
        await ShouldFailWith("LabRequestItem.NotAvailable", () => Upload(id, null, R(items[1], 1m)));
    }

    [Fact]
    public async Task The_patient_is_notified_through_the_parent_when_the_patient_is_a_child()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var (id, items) = await SeedProcessingLabAsync(sub.PatientId, (Svc1, 10m, true));

        await Upload(id, null, R(items[0], 1m));

        await using var db = Db();
        (await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.LabResult)).ShouldBe(1);
        (await db.LabResults.SingleAsync()).PatientId.ShouldBe(sub.PatientId);
    }

    // ------------------------------------------------------------------ files

    [Fact]
    public async Task A_result_file_is_stored_and_its_url_returned()
    {
        var (id, items) = await SeedProcessingLabAsync(null, (Svc1, 10m, true));

        var r = await Upload(id, null, R(items[0], 1m, FakeFile("scan.PDF")));

        var url = r.LabItems.Single().ResultFileUrl;
        url.ShouldNotBeNull();
        url.ShouldStartWith("/uploads/labResult/");
        url.ShouldEndWith(".pdf");                 // the service lower-cases the extension here
        (await LoadLabAsync(id)).Items.Single().ResultFileUrl.ShouldBe(url);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("page.html")]
    public async Task Validation_InvalidFileType_and_nothing_is_saved(string name)
    {
        var (id, items) = await SeedProcessingLabAsync(null, (Svc1, 10m, true));

        await ShouldFailWith("Validation.InvalidFileType", () => Upload(id, null, R(items[0], 5m, FakeFile(name))));

        (await LoadLabAsync(id)).Items.Single().ResultValue.ShouldBeNull();
        (await LoadLabAsync(id)).Status.ShouldBe(LabRequestStatus.Processing);
    }

    [Fact]
    public async Task Validation_FileTooLarge_above_5MB()
    {
        var (id, items) = await SeedProcessingLabAsync(null, (Svc1, 10m, true));
        await ShouldFailWith("Validation.FileTooLarge", () => Upload(id, null, R(items[0], 5m, FakeFile("big.png", 5 * 1024 * 1024 + 1))));
    }

    /// <summary>
    /// Each upload overwrites ResultFileUrl with the file of THIS call, even when there is none. Sending a corrected value
    /// without re-sending the file erases the file that was attached before. Fix: only assign when a new file arrived.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_later_upload_without_a_file_keeps_the_earlier_file()
    {
        var (id, items) = await SeedProcessingLabAsync();
        var first = await Upload(id, null, R(items[0], 1m, FakeFile("a.png")));
        var url = first.LabItems.Single(i => i.LabRequestItemId == items[0]).ResultFileUrl;
        url.ShouldNotBeNull();

        await Upload(id, null, R(items[0], 1.5m));            // corrected value, no file

        (await LoadLabAsync(id)).Items.Single(i => i.Id == items[0]).ResultFileUrl.ShouldBe(url);
    }

    // ------------------------------------------------------------------ preconditions

    [Theory]
    [InlineData(LabRequestStatus.Pending)]
    [InlineData(LabRequestStatus.Collected)]
    [InlineData(LabRequestStatus.Completed)]
    [InlineData(LabRequestStatus.Cancelled)]
    public async Task NotProcessing_unless_the_request_is_Processing(LabRequestStatus status)
    {
        var (id, items) = await SeedLabAsync(status);
        await ShouldFailWith("LabRequest.NotProcessing", () => Upload(id, null, R(items[0], 1m)));
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    public async Task Payment_NotCompleted_when_the_payment_is_not_Paid(PaymentStatus status)
    {
        var (id, items) = await SeedLabAsync(LabRequestStatus.Processing);
        await AddPaymentAsync(Pat.PatientId, 30m, PaymentType.Lab, status, labRequestId: id);

        await ShouldFailWith("Payment.NotCompleted", () => Upload(id, null, R(items[0], 1m)));
    }

    [Fact]
    public async Task Payment_NotCompleted_when_there_is_no_payment()
    {
        var (id, items) = await SeedLabAsync(LabRequestStatus.Processing);
        await ShouldFailWith("Payment.NotCompleted", () => Upload(id, null, R(items[0], 1m)));
    }

    [Fact]
    public async Task LabRequestItemNotFound_for_an_item_of_another_request_and_nothing_is_saved()
    {
        var (id, items) = await SeedProcessingLabAsync();
        var (_, other) = await SeedLabAsync();

        await ShouldFailWith("LabRequestItemNotFound", () => Upload(id, null, R(items[0], 3m), R(other[0], 4m)));

        (await LoadLabAsync(id)).Items.ShouldAllBe(i => i.ResultValue == null);
    }

    [Fact]
    public Task LabRequest_NotFound() =>
        ShouldFailWith("LabRequest.NotFound", () => Upload(999_999, null, R(1, 1m)));

    [Fact]
    public Task Technician_NotFound_for_an_unknown_user() =>
        ShouldFailWith("Technician.NotFound", () => Upload(1, 999_999, R(1, 1m)));

    

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewLabService().LabUploadResult(999_999, Tech, new UploadLabResultRequestDto { LabRequestId = 1, Results = [] }));
}
