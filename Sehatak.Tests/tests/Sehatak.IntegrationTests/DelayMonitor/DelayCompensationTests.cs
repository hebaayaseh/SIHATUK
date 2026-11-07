using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Job = Sehatak.Infrastructure.Services.SuperAdminService.Background.DelayMonitorJobs;

namespace Sehatak.IntegrationTests.DelayMonitor;

/// <summary>
/// CheckSlotTime. The doctor's day has 30-minute slots. Minutes SAVED during the day (a patient leaves early, a booked patient
/// never comes) are added up; every time the total reaches one slot, one waiting-list patient of today gets a new slot at the end.
/// Running over a slot takes minutes away from that total.
/// Tests marked KnownBug describe the intended behaviour and fail until DelayMonitorJobs is replaced by the new version.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DelayCompensationTests(SqlServerFixture fx) : DelayTestBase(fx)
{
    private PatientInfo _w1 = null!, _w2 = null!;
    private static readonly TimeOnly S0930 = new(9, 30), S1000 = new(10, 0), S1030 = new(10, 30);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _w1 = await C.AddPatientAsync();
        _w2 = await C.AddPatientAsync();
    }

    // ------------------------------------------------------------------ saving time

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_visits_that_finish_15_minutes_early_give_the_first_waiting_patient_a_slot_at_the_end()
    {
        var a = await AddCompletedAsync(minutes: 15, slot: T0900);
        var b = await AddCompletedAsync(minutes: 15, slot: S0930);
        var w = await AddWaiterAsync(_w1);

        await RunAsync(SlotPass);

        var promoted = (await NewAppointmentsAsync(D.DoctorId, a, b)).ShouldHaveSingleItem();
        promoted.patientId.ShouldBe(_w1.PatientId);
        promoted.appointmentDate.ShouldBe(Today);
        promoted.timeSlot!.Value.ShouldBeGreaterThanOrEqualTo(S1000);      // after the last booked slot (09:30 + 30), later if the day is already past it
        promoted.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);

        var row = await WaiterAsync(w);
        row.Status.ShouldBe(WaitlistStatus.Entered);
        row.PromotedAppointmentId.ShouldBe(promoted.Id);
        (await NotificationsAsync(_w1.UserId)).ShouldHaveSingleItem().Type.ShouldBe(NotificationType.Appointment);
    }

    /// <summary>Even one minute counts: three visits that are 10 minutes short add up to one whole slot.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Small_savings_from_many_visits_add_up()
    {
        await AddCompletedAsync(minutes: 20, slot: T0900);
        await AddCompletedAsync(minutes: 20, slot: S0930);
        await AddCompletedAsync(minutes: 20, slot: S1000);
        var w = await AddWaiterAsync(_w1);

        await RunAsync(SlotPass);

        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_booked_patient_who_did_not_come_gives_the_whole_slot()
    {
        await AddNoShowAsync(T0900);
        var w = await AddWaiterAsync(_w1);

        await RunAsync(SlotPass);

        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    /// <summary>No one marks NoShow today, so a confirmed booking whose slot is over, never started and never checked in counts as a no-show.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_confirmed_booking_whose_slot_ended_without_the_patient_counts_but_a_checked_in_one_does_not()
    {
        var now = ClinicNow();
        if (now.Hour < 3) return;                                           // the slots below must be earlier today
        var missed = await AddConfirmedAsync(now.AddHours(-2));
        var waiting = await AddConfirmedAsync(now.AddHours(-1), checkedIn: true);   // arrived, still waiting: not a no-show
        var w1 = await AddWaiterAsync(_w1, createdMinutesAgo: 20);
        var w2 = await AddWaiterAsync(_w2, createdMinutesAgo: 10);

        await RunAsync(SlotPass);

        (await WaiterAsync(w1)).Status.ShouldBe(WaitlistStatus.Entered);
        (await WaiterAsync(w2)).Status.ShouldBe(WaitlistStatus.Waiting);    // only one slot was really saved
    }

    // ------------------------------------------------------------------ not enough / wrong kind of time

    [Fact]
    public async Task Less_than_one_slot_saved_gives_nothing()
    {
        await AddCompletedAsync(minutes: 25, slot: T0900);                  // 5 saved
        var w = await AddWaiterAsync(_w1);
        await RunAsync(SlotPass);
        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    /// <summary>25 minutes saved by one visit, 10 lost by another: 15 left, not a slot.</summary>
    [Fact]
    public async Task Running_over_takes_time_away_from_what_was_saved()
    {
        await AddCompletedAsync(minutes: 5, slot: T0900);                   // +25
        await AddCompletedAsync(minutes: 40, slot: S0930);                  // -10
        var w = await AddWaiterAsync(_w1);
        await RunAsync(SlotPass);
        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    /// <summary>The old job promoted somebody when a visit RAN OVER. A visit that ran over never creates time.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_visit_that_ran_over_never_creates_a_slot()
    {
        await AddCompletedAsync(minutes: 70, slot: T0900);
        var w = await AddWaiterAsync(_w1);
        await RunAsync(SlotPass);
        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task Only_todays_visits_count_and_cancelled_bookings_give_nothing()
    {
        await AddNoShowAsync(T0900, date: Today.AddDays(-1));
        await AddNoShowAsync(S0930, date: Today.AddDays(-1));
        await AddApptAsync(D.DoctorId, Pat.PatientId, Today, S1000, AppointmentStatus.Cancelled);
        var w = await AddWaiterAsync(_w1);

        await RunAsync(SlotPass);

        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task A_booking_that_has_not_reached_its_time_gives_nothing()
    {
        var now = ClinicNow();
        if (now.Hour >= 21) return;
        await AddConfirmedAsync(now.AddHours(2));
        var w = await AddWaiterAsync(_w1);
        await RunAsync(SlotPass);
        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    [Fact]
    public async Task A_doctor_with_no_schedule_today_is_skipped()
    {
        var other = await C.AddDoctorAsync();                               // no schedule
        await AddNoShowAsync(T0900, doctor: other);
        var w = await AddWaiterAsync(_w1, other);
        await RunAsync(SlotPass);
        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    // ------------------------------------------------------------------ who gets the slot

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_oldest_waiting_patient_of_this_doctor_goes_first()
    {
        var other = await C.AddDoctorAsync();
        var otherPatient = await C.AddPatientAsync();
        var wOther = await AddWaiterAsync(otherPatient, other, createdMinutesAgo: 60);
        var wOld = await AddWaiterAsync(_w1, createdMinutesAgo: 30);
        var wNew = await AddWaiterAsync(_w2, createdMinutesAgo: 5);
        await AddNoShowAsync(T0900);

        await RunAsync(SlotPass);

        (await WaiterAsync(wOld)).Status.ShouldBe(WaitlistStatus.Entered);
        (await WaiterAsync(wNew)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await WaiterAsync(wOther)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    /// <summary>The list is per day: somebody waiting for next week must not get today's slot.</summary>
    [Fact]
    public async Task A_patient_waiting_for_another_day_is_not_promoted_into_today()
    {
        await AddNoShowAsync(T0900);
        await AddNoShowAsync(S0930);
        var w = await AddWaiterAsync(_w1, preferred: Today.AddDays(7));
        await RunAsync(SlotPass);
        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Waiting);
    }

    /// <summary>A dependent has no login of his own; the parent account is the one notified.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_waiting_sub_patient_is_promoted_and_the_parent_is_notified()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        await AddNoShowAsync(T0900);
        var w = await AddWaiterAsync(sub);

        await RunAsync(SlotPass);

        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Entered);
        (await NotificationsAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_slots_saved_means_two_patients_with_different_times()
    {
        var a = await AddNoShowAsync(T0900);
        var b = await AddNoShowAsync(S0930);
        await AddWaiterAsync(_w1, createdMinutesAgo: 20);
        await AddWaiterAsync(_w2, createdMinutesAgo: 10);

        await RunAsync(SlotPass);

        var promoted = await NewAppointmentsAsync(D.DoctorId, a, b);
        promoted.Count.ShouldBe(2);
        promoted.Select(p => p.timeSlot).Distinct().Count().ShouldBe(2);
    }

    // ------------------------------------------------------------------ the balance carries over

    /// <summary>Saved minutes are not lost between runs, and a slot that was already given away is not given twice.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Savings_carry_over_between_runs_and_each_slot_is_used_once()
    {
        var w1 = await AddWaiterAsync(_w1, createdMinutesAgo: 20);
        var w2 = await AddWaiterAsync(_w2, createdMinutesAgo: 10);
        var seed = new List<int>();

        seed.Add(await AddCompletedAsync(minutes: 20, slot: T0900));        // +10
        seed.Add(await AddCompletedAsync(minutes: 20, slot: S0930));        // +10
        await RunAsync(SlotPass);
        (await WaiterAsync(w1)).Status.ShouldBe(WaitlistStatus.Waiting);    // 20 < 30

        seed.Add(await AddCompletedAsync(minutes: 20, slot: S1000));        // +10 => 30
        await RunAsync(SlotPass);
        (await WaiterAsync(w1)).Status.ShouldBe(WaitlistStatus.Entered);
        (await WaiterAsync(w2)).Status.ShouldBe(WaitlistStatus.Waiting);

        await RunAsync(SlotPass);                                           // nothing new: still only one promotion
        (await WaiterAsync(w2)).Status.ShouldBe(WaitlistStatus.Waiting);
        (await NewAppointmentsAsync(D.DoctorId, seed.ToArray())).Count.ShouldBe(1);

        seed.Add(await AddCompletedAsync(minutes: 20, slot: S1030));        // +10 => 40 - 30 used = 10
        seed.Add(await AddCompletedAsync(minutes: 20, slot: new TimeOnly(11, 0)));   // 20
        seed.Add(await AddCompletedAsync(minutes: 20, slot: new TimeOnly(11, 30)));  // 30
        await RunAsync(SlotPass);
        (await WaiterAsync(w2)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    /// <summary>The time was saved while nobody was waiting; when somebody joins the list later he still gets it.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Time_saved_before_anyone_waited_is_not_lost()
    {
        await AddNoShowAsync(T0900);
        await RunAsync(SlotPass);                                           // nobody waiting yet
        var w = await AddWaiterAsync(_w1);

        await RunAsync(SlotPass);

        (await WaiterAsync(w)).Status.ShouldBe(WaitlistStatus.Entered);
    }

    // ------------------------------------------------------------------ prepayment centers

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Without_prepayment_the_promoted_booking_is_confirmed_and_has_no_payment()
    {
        var done = await AddNoShowAsync(T0900);
        await AddWaiterAsync(_w1);
        await RunAsync(SlotPass);

        var promoted = (await NewAppointmentsAsync(D.DoctorId, done)).ShouldHaveSingleItem();
        promoted.appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        await using var db = Db();
        db.Payments.Any(p => p.AppointmentId == promoted.Id).ShouldBeFalse();
    }

    /// <summary>With prepayment the booking waits for the payment (the expiry job only cancels Pending bookings).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task With_prepayment_the_promoted_booking_waits_for_the_payment()
    {
        await SetPrepaymentAsync(true, 50m);
        var done = await AddNoShowAsync(T0900);
        await AddWaiterAsync(_w1);

        await RunAsync(SlotPass);

        var promoted = (await NewAppointmentsAsync(D.DoctorId, done)).ShouldHaveSingleItem();
        promoted.appointmentStatus.ShouldBe(AppointmentStatus.Pending);
        var pay = await PaymentOfAsync(promoted.Id);
        pay.Status.ShouldBe(PaymentStatus.Pending);
        pay.Amount.ShouldBe(50m);
        Math.Abs((pay.ExpiresAt!.Value - DateTime.UtcNow.AddMinutes(20)).TotalMinutes).ShouldBeLessThan(2);
    }

    // ------------------------------------------------------------------ the job itself

    /// <summary>Neither pass was inside a try/catch: one failure ends the BackgroundService and, in .NET 8, stops the whole API.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_failing_pass_does_not_kill_the_job()
    {
        var services = new ServiceCollection();
        services.AddScoped<SharedDbContext>(_ => throw new InvalidOperationException("db down"));
        services.AddSingleton(Fx.Harness.TenantFactory);
        using var provider = services.BuildServiceProvider();

        var job = new Job(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<Job>.Instance);
        await job.StartAsync(CancellationToken.None);
        await Task.Delay(500);

        var faulted = job.ExecuteTask!.IsFaulted;
        await job.StopAsync(CancellationToken.None);
        faulted.ShouldBeFalse("الـ job وقف بعد أول خطأ");
    }
}
