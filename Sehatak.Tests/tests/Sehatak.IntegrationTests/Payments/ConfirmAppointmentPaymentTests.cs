using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ConfirmPaymentDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Payments;

/// <summary>ConfirmPaymentService.ReceptionistCollectAppointmentPaymentAsync: the receptionist takes the money and checks the patient out.</summary>
[Collection(SqlServerCollection.Name)]
public class ConfirmAppointmentPaymentTests(SqlServerFixture fx) : PaymentTestBase(fx)
{
    private int _appt;
    private int _payment;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _appt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, AppointmentStatus.InProgress);
        await CheckInAsync(_appt);
        // created 5 hours ago, still unpaid
        _payment = await AddPaymentAsync(Pat.PatientId, 50m, PaymentType.Appointment, appointmentId: _appt,
            paidAtUtc: DateTime.UtcNow.AddHours(-5));
    }

    private static CollectPaymentRequestDto Cash => new() { Method = PaymentMethod.cash };

    private Task<PaymentResponseDto> Collect(int? appointmentId = null, int? userId = null, int? centerId = null, PaymentMethod method = PaymentMethod.cash) =>
        NewConfirmService().ReceptionistCollectAppointmentPaymentAsync(centerId ?? C.Id, userId ?? Rec,
            appointmentId ?? _appt, new CollectPaymentRequestDto { Method = method });

    [Fact]
    public async Task Collecting_marks_the_payment_paid_and_checks_the_patient_out()
    {
        var res = await Collect();

        res.PaymentId.ShouldBe(_payment);
        res.Amount.ShouldBe(50m);
        res.Status.ShouldBe("Paid");
        res.Method.ShouldBe("cash");

        await using var db = Db();
        var p = await db.Payments.SingleAsync(x => x.Id == _payment);
        p.Status.ShouldBe(PaymentStatus.Paid);
        p.Method.ShouldBe(PaymentMethod.cash);
        p.RecordedByStaffId.ShouldBe(Rec);
        p.Amount.ShouldBe(50m);

        var a = await db.Appointments.SingleAsync(x => x.Id == _appt);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Completed);
        a.CheckOutTime.ShouldNotBeNull();
        Math.Abs((a.CheckOutTime!.Value - DateTime.UtcNow).TotalMinutes).ShouldBeLessThan(5);

        (await db.AuditLogs.CountAsync(x => x.Action == "AppointmentPay" && x.EntityId == _appt)).ShouldBe(1);
    }

    [Theory]
    [InlineData(PaymentMethod.card)]
    [InlineData(PaymentMethod.online)]
    public async Task The_chosen_payment_method_is_stored(PaymentMethod method)
    {
        var res = await Collect(method: method);

        res.Method.ShouldBe(method.ToString());
        await using var db = Db();
        (await db.Payments.SingleAsync(x => x.Id == _payment)).Method.ShouldBe(method);
    }

    [Fact]
    public async Task Only_the_requested_appointments_payment_is_touched()
    {
        var other = await C.AddPatientAsync();
        var otherAppt = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1000, AppointmentStatus.InProgress);
        await CheckInAsync(otherAppt);
        var otherPayment = await AddPaymentAsync(other.PatientId, 70m, PaymentType.Appointment, appointmentId: otherAppt);

        await Collect();

        await using var db = Db();
        (await db.Payments.SingleAsync(x => x.Id == otherPayment)).Status.ShouldBe(PaymentStatus.Pending);
        (await db.Appointments.SingleAsync(x => x.Id == otherAppt)).appointmentStatus.ShouldBe(AppointmentStatus.InProgress);
    }

    // ------------------------------------------------------------------ business errors

    [Fact]
    public async Task Collecting_twice_fails_the_second_time()
    {
        await Collect();

        await ShouldFailWith("Payment.NotFound", () => Collect());
    }

    [Fact]
    public async Task Payment_NotFound_when_the_appointment_has_no_pending_payment()
    {
        var other = await C.AddPatientAsync();
        var apptWithoutPayment = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1000, AppointmentStatus.InProgress);
        await CheckInAsync(apptWithoutPayment);

        await ShouldFailWith("Payment.NotFound", () => Collect(apptWithoutPayment));
    }

    [Fact]
    public Task Payment_NotFound_for_an_unknown_appointment() =>
        ShouldFailWith("Payment.NotFound", () => Collect(999_999));

    [Fact]
    public async Task Appointment_NotCheckedInYet_and_nothing_is_saved()
    {
        var other = await C.AddPatientAsync();
        var notStarted = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1000);
        var paymentId = await AddPaymentAsync(other.PatientId, 30m, PaymentType.Appointment, appointmentId: notStarted);

        await ShouldFailWith("Appointment.NotCheckedInYet", () => Collect(notStarted));

        await using var db = Db();
        (await db.Payments.SingleAsync(x => x.Id == paymentId)).Status.ShouldBe(PaymentStatus.Pending);
        var a = await db.Appointments.SingleAsync(x => x.Id == notStarted);
        a.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        a.CheckOutTime.ShouldBeNull();
    }

    [Fact]
    public async Task Appointment_AlreadyCheckedOut_and_nothing_changes()
    {
        var other = await C.AddPatientAsync();
        var done = await C.AddAppointmentAsync(Doc.DoctorId, other.PatientId, Date, S1000, AppointmentStatus.InProgress);
        await CheckInAsync(done, alsoCheckedOut: true);
        var paymentId = await AddPaymentAsync(other.PatientId, 30m, PaymentType.Appointment, appointmentId: done);

        await ShouldFailWith("Appointment.AlreadyCheckedOut", () => Collect(done));

        await using var db = Db();
        (await db.Payments.SingleAsync(x => x.Id == paymentId)).Status.ShouldBe(PaymentStatus.Pending);
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => Collect(centerId: 999_999));

    // ------------------------------------------------------------------ KNOWN BUGS (expected to FAIL until fixed)

    /// <summary>
    /// Collecting never sets Payment.PaidAt, so it keeps the time the payment row was created
    /// (the default) and the receipt/bill shows a wrong "paid at". Fix: payment.PaidAt = DateTime.UtcNow.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task PaidAt_is_set_when_the_money_is_collected()
    {
        var res = await Collect();

        Math.Abs((res.PaidAt - DateTime.UtcNow).TotalMinutes).ShouldBeLessThan(5, $"PaidAt={res.PaidAt:O} (row was created 5 hours ago)");
    }
    [Fact]
    public Task Receptionist_NotFound_for_an_unknown_staff_member() =>
        ShouldFailWith("Receptionist.NotFound", () => Collect(userId: 999_999));

    [Fact]
    public async Task Receptionist_NotFound_for_an_inactive_receptionist()
    {
        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("Receptionist.NotFound", () => Collect(userId: inactive));
    }


}
