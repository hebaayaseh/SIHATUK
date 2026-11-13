using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ConfirmPaymentDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Payments;

/// <summary>Cases ConfirmAppointmentPaymentTests does not have: a center that is not active, and two receptionists pressing "collect" together.</summary>
[Collection(SqlServerCollection.Name)]
public class ConfirmAppointmentPaymentGapTests(SqlServerFixture fx) : PaymentTestBase(fx)
{
    private int _appt;
    private int _payment;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _appt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.InProgress);
        await CheckInAsync(_appt);
        _payment = await AddPaymentAsync(Pat.PatientId, 50m, PaymentType.Appointment, appointmentId: _appt, paidAtUtc: DateTime.UtcNow.AddHours(-5));
    }

    private Task<PaymentResponseDto> Collect(int? userId = null) =>
        NewConfirmService().ReceptionistCollectAppointmentPaymentAsync(C.Id, userId ?? Rec, _appt, new CollectPaymentRequestDto { Method = PaymentMethod.cash });

    [Theory]
    [InlineData(CenterStatus.Pending)]
    [InlineData(CenterStatus.Suspended)]
    public async Task A_center_that_is_not_active_cannot_take_payments(CenterStatus status)
    {
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            (await shared.MedicalCenters.SingleAsync(c => c.Id == C.Id)).CenterStatus = status;
            await shared.SaveChangesAsync();
        }

        await ShouldFailWith("Center.NotFound", () => Collect());

        await using var db = Db();
        (await db.Payments.SingleAsync(p => p.Id == _payment)).Status.ShouldBe(PaymentStatus.Pending);
        (await db.Appointments.SingleAsync(a => a.Id == _appt)).CheckOutTime.ShouldBeNull();
    }

    /// <summary>Both calls read the payment as Pending before either one saves, so both succeed: the patient is checked out twice and the audit log says the money was collected twice.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_receptionists_collecting_at_the_same_moment_collect_once()
    {
        var second = await C.AddReceptionistAsync();

        var results = await Task.WhenAll(new[] { Rec, second }.Select(async user =>
        {
            try { await Collect(user); return true; }
            catch { return false; }
        }));

        results.Count(ok => ok).ShouldBe(1);
        await using var db = Db();
        (await db.AuditLogs.CountAsync(a => a.Action == "AppointmentPay" && a.EntityId == _appt)).ShouldBe(1);
    }
}
