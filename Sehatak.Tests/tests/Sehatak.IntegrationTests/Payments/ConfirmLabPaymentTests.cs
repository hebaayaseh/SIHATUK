using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ConfirmPaymentDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Payments;

/// <summary>ConfirmPaymentService.ReceptionistPayLabRequestAsync: pay for a lab request once the sample was collected.</summary>
[Collection(SqlServerCollection.Name)]
public class ConfirmLabPaymentTests(SqlServerFixture fx) : PaymentTestBase(fx)
{
    private int _lab;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        var a = await AddServiceAsync("CBC", 30m);
        var b = await AddServiceAsync("Vitamin D", 20m);
        var c = await AddServiceAsync("Rare test", 10m);
        // C is not available at this lab, so it must not be billed
        _lab = await AddLabRequestAsync(Pat.PatientId, LabRequestStatus.Collected, (a, 30m, true), (b, 20m, true), (c, 10m, false));
    }

    private Task<PaymentResponseDto> Pay(int? labId = null, int? userId = null, int? centerId = null, PaymentMethod method = PaymentMethod.cash) =>
        NewConfirmService().ReceptionistPayLabRequestAsync(centerId ?? C.Id, userId ?? Rec,
            labId ?? _lab, new CollectPaymentRequestDto { Method = method });

    [Fact]
    public async Task Bills_only_the_available_items_and_moves_the_request_to_Processing()
    {
        var res = await Pay(method: PaymentMethod.card);

        res.Amount.ShouldBe(50m);
        res.Status.ShouldBe("Paid");
        res.Method.ShouldBe("card");

        await using var db = Db();
        var p = await db.Payments.SingleAsync(x => x.LabRequestId == _lab);
        p.PatientId.ShouldBe(Pat.PatientId);
        p.Type.ShouldBe(PaymentType.Lab);
        p.Status.ShouldBe(PaymentStatus.Paid);
        p.Amount.ShouldBe(50m);
        p.RecordedByStaffId.ShouldBe(Rec);

        (await db.LabRequests.SingleAsync(x => x.Id == _lab)).Status.ShouldBe(LabRequestStatus.Processing);
        (await db.AuditLogs.CountAsync(x => x.Action == "PayLabRequest" && x.EntityId == _lab)).ShouldBe(1);
    }

    [Fact]
    public async Task Paying_again_is_refused_because_the_request_is_no_longer_Collected()
    {
        await Pay();

        await ShouldFailWith("LabRequest.NotCollected", () => Pay());

        await using var db = Db();
        (await db.Payments.CountAsync(x => x.LabRequestId == _lab)).ShouldBe(1);
    }

    [Theory]
    [InlineData(LabRequestStatus.Pending)]
    [InlineData(LabRequestStatus.Processing)]
    [InlineData(LabRequestStatus.Completed)]
    [InlineData(LabRequestStatus.Cancelled)]
    public async Task LabRequest_NotCollected_unless_the_sample_was_collected(LabRequestStatus status)
    {
        var s = await AddServiceAsync("X", 10m);
        var lab = await AddLabRequestAsync(Pat.PatientId, status, (s, 10m, true));

        await ShouldFailWith("LabRequest.NotCollected", () => Pay(lab));

        await using var db = Db();
        (await db.Payments.CountAsync(x => x.LabRequestId == lab)).ShouldBe(0);
        (await db.LabRequests.SingleAsync(x => x.Id == lab)).Status.ShouldBe(status);
    }

    [Fact]
    public async Task LabRequest_AlreadyPaid_when_a_payment_exists_for_a_Collected_request()
    {
        var s = await AddServiceAsync("X", 10m);
        var lab = await AddLabRequestAsync(Pat.PatientId, LabRequestStatus.Collected, (s, 10m, true));
        await AddPaymentAsync(Pat.PatientId, 10m, PaymentType.Lab, PaymentStatus.Paid, labRequestId: lab);

        await ShouldFailWith("LabRequest.AlreadyPaid", () => Pay(lab));
    }

    [Fact]
    public Task LabRequest_NotFound() =>
        ShouldFailWith("LabRequest.NotFound", () => Pay(999_999));

    [Fact]
    public Task Receptionist_NotFound_for_an_unknown_user() =>
    ShouldFailWith("Receptionist.NotFound", () => Pay(userId: 999_999));

    [Fact]
    public async Task Receptionist_NotFound_for_an_inactive_receptionist()
    {
        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("Receptionist.NotFound", () => Pay(userId: inactive));
    }


    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => Pay(centerId: 999_999));

    /// <summary>
    /// PaidAt and UpdatedAt are written with ClinicClock.Now (Palestine time) into columns that hold UTC everywhere else,
    /// so they are 2-3 hours ahead. Fix: DateTime.UtcNow for both (ClinicClock is only for comparing calendar dates).
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Paid_time_and_update_time_are_stored_in_UTC()
    {
        await Pay();

        await using var db = Db();
        var p = await db.Payments.SingleAsync(x => x.LabRequestId == _lab);
        var l = await db.LabRequests.SingleAsync(x => x.Id == _lab);
        Math.Abs((p.PaidAt - DateTime.UtcNow).TotalMinutes).ShouldBeLessThan(5, $"PaidAt={p.PaidAt:O}");
        Math.Abs((l.UpdatedAt - DateTime.UtcNow).TotalMinutes).ShouldBeLessThan(5, $"UpdatedAt={l.UpdatedAt:O}");
    }
}
