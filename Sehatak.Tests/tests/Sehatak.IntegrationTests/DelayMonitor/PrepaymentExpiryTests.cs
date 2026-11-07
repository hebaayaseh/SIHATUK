using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.DelayMonitor;

/// <summary>PrePayment pass: an unpaid prepayment that ran out cancels the booking and offers the slot to the waiting list.</summary>
[Collection(SqlServerCollection.Name)]
public class PrepaymentExpiryTests(SqlServerFixture fx) : DelayTestBase(fx)
{
    private PatientInfo _waiter = null!;
    private static DateTime Ago(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _waiter = await C.AddPatientAsync();
        await SetPrepaymentAsync(true);
    }

    private async Task<int> PendingBookingAsync(DateTime expiresAt, PatientInfo? patient = null, DateOnly? date = null,
                                                TimeOnly? slot = null, PaymentStatus pay = PaymentStatus.Pending)
    {
        var p = patient ?? Pat;
        var id = await AddApptAsync(D.DoctorId, p.PatientId, date ?? Today, slot ?? new TimeOnly(10, 0), AppointmentStatus.Pending);
        await AddPrepaymentAsync(id, p.PatientId, expiresAt, pay);
        return id;
    }

    [Fact]
    public async Task An_expired_prepayment_cancels_the_booking_fails_the_payment_and_tells_the_patient()
    {
        var id = await PendingBookingAsync(Ago(5));
        await AddWaiterAsync(_waiter);                       // gives the pass something to save at the end

        await RunAsync(PrepayPass);

        (await ApptAsync(id)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await PaymentOfAsync(id)).Status.ShouldBe(PaymentStatus.Failed);
        var n = (await NotificationsAsync(Pat.UserId)).ShouldHaveSingleItem();
        n.Type.ShouldBe(NotificationType.Cancellation);
    }

    [Fact]
    public async Task The_next_waiting_patient_gets_a_pending_appointment_with_a_20_minute_payment()
    {
        var id = await PendingBookingAsync(Ago(5));
        var w = await AddWaiterAsync(_waiter);

        await RunAsync(PrepayPass);

        var promoted = (await NewAppointmentsAsync(D.DoctorId, id)).ShouldHaveSingleItem();
        promoted.patientId.ShouldBe(_waiter.PatientId);
        promoted.appointmentStatus.ShouldBe(AppointmentStatus.Pending);
        var pay = await PaymentOfAsync(promoted.Id);
        pay.Status.ShouldBe(PaymentStatus.Pending);
        pay.Amount.ShouldBe(50m);
        Math.Abs((pay.ExpiresAt!.Value - DateTime.UtcNow.AddMinutes(20)).TotalMinutes).ShouldBeLessThan(2);

        var row = await WaiterAsync(w);
        row.Status.ShouldBe(WaitlistStatus.Entered);
        row.PromotedAppointmentId.ShouldBe(promoted.Id);
        (await NotificationsAsync(_waiter.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_far_future_expiry_a_paid_payment_and_a_center_without_prepayment_are_left_alone()
    {
        var far = await PendingBookingAsync(DateTime.UtcNow.AddHours(6));
        var paid = await PendingBookingAsync(Ago(30), patient: _waiter, slot: new TimeOnly(10, 30), pay: PaymentStatus.Paid);
        await RunAsync(PrepayPass);
        (await ApptAsync(far)).appointmentStatus.ShouldBe(AppointmentStatus.Pending);
        (await ApptAsync(paid)).appointmentStatus.ShouldBe(AppointmentStatus.Pending);

        await SetPrepaymentAsync(false);
        var expired = await PendingBookingAsync(Ago(30), slot: new TimeOnly(11, 0));
        await AddWaiterAsync(_waiter);
        await RunAsync(PrepayPass);
        (await ApptAsync(expired)).appointmentStatus.ShouldBe(AppointmentStatus.Pending);
    }

    // ------------------------------------------------------------------ known bugs

    /// <summary>
    /// The pass compares the payment's UTC expiry with ClinicClock.Now (clinic local time, UTC+2/+3), so every payment is treated
    /// as expired 2-3 hours EARLY: a booking is cancelled the very first minute after it was made.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_payment_that_expires_in_10_minutes_is_not_cancelled_yet()
    {
        var id = await PendingBookingAsync(DateTime.UtcNow.AddMinutes(10));
        await AddWaiterAsync(_waiter);

        await RunAsync(PrepayPass);

        (await ApptAsync(id)).appointmentStatus.ShouldBe(AppointmentStatus.Pending);
        (await PaymentOfAsync(id)).Status.ShouldBe(PaymentStatus.Pending);
    }

    /// <summary>`continue` when nobody is waiting skips SaveChanges and the loop has no final save: the last cancellation is never stored.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task An_expired_booking_is_cancelled_even_when_nobody_is_waiting()
    {
        var id = await PendingBookingAsync(Ago(5));

        await RunAsync(PrepayPass);

        (await ApptAsync(id)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await PaymentOfAsync(id)).Status.ShouldBe(PaymentStatus.Failed);
        (await NotificationsAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    /// <summary>`(int)Patient.userId` is null for a dependent: the exception kills the pass for the whole center, again and again every minute.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task An_expired_booking_of_a_sub_patient_is_cancelled_and_the_parent_is_notified()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var id = await PendingBookingAsync(Ago(5), patient: sub);
        await AddWaiterAsync(_waiter);

        await RunAsync(PrepayPass);

        (await ApptAsync(id)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await NotificationsAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    /// <summary>Only `appointmentDate == today` is looked at, so a booking for a later day with an expired payment stays Pending and blocks its slot. Its waiting list is the list of THAT day.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task An_expired_booking_for_a_future_day_is_cancelled_too()
    {
        var id = await PendingBookingAsync(Ago(5), date: Today.AddDays(3));
        await AddWaiterAsync(_waiter, preferred: Today.AddDays(3));

        await RunAsync(PrepayPass);

        (await ApptAsync(id)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        var promoted = (await NewAppointmentsAsync(D.DoctorId, id)).ShouldHaveSingleItem();
        promoted.appointmentDate.ShouldBe(Today.AddDays(3));
    }

    /// <summary>The offer to the waiting patient is a waiting-list notification, not a "cancellation" one.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_offer_to_the_waiting_patient_is_a_waitlist_notification()
    {
        await PendingBookingAsync(Ago(5));
        await AddWaiterAsync(_waiter);
        await RunAsync(PrepayPass);
        (await NotificationsAsync(_waiter.UserId)).ShouldHaveSingleItem().Type.ShouldBe(NotificationType.Waitlist);
    }

    /// <summary>The promoted appointment must take over the cancelled one's slot, not float with no time.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_promoted_appointment_takes_the_cancelled_time_slot()
    {
        var id = await PendingBookingAsync(Ago(5), slot: new TimeOnly(10, 0));
        await AddWaiterAsync(_waiter);

        await RunAsync(PrepayPass);

        var promoted = (await NewAppointmentsAsync(D.DoctorId, id)).ShouldHaveSingleItem();
        promoted.timeSlot.ShouldBe(new TimeOnly(10, 0));
    }

    /// <summary>A patient waiting for another date must not receive today's slot.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_patient_waiting_for_another_day_is_not_promoted_here()
    {
        var id = await PendingBookingAsync(Ago(5));
        var w = await AddWaiterAsync(_waiter, preferred: Today.AddDays(7));

        await RunAsync(PrepayPass);

        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await NewAppointmentsAsync(D.DoctorId, id)).ShouldBeEmpty();
    }
}
