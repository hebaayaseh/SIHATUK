using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Job = Sehatak.Infrastructure.Services.SuperAdminService.Background.AppointmentReminderJob;

namespace Sehatak.IntegrationTests.DelayMonitor;

/// <summary>
/// "Your appointment is in 30 minutes". The job reads the REAL clinic clock (ClinicClock), so the appointments are placed
/// relative to "now". Between 21:30 and 00:40 the window can cross midnight, so the tests quietly skip in those minutes.
/// The job pass is private, so it is invoked by reflection (same as the other job tests).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AppointmentReminderJobTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private const string Pass = "CheckAppointmentReminders";

    private static DateOnly Today => ClinicClock.Today;
    private static TimeOnly NowTime => TimeOnly.FromDateTime(ClinicClock.Now);
    private static bool NearMidnight => NowTime > new TimeOnly(21, 30) || NowTime < new TimeOnly(0, 40);

    private static TimeOnly InMinutes(int minutes)
    {
        var t = NowTime.AddMinutes(minutes);
        return new TimeOnly(t.Hour, t.Minute);
    }

    private async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Fx.Harness.NewSharedDb());
        services.AddSingleton(Fx.Harness.TenantFactory);
        using var provider = services.BuildServiceProvider();

        var job = new Job(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<Job>.Instance);
        var m = typeof(Job).GetMethod(Pass, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)m.Invoke(job, null)!;
    }

    /// <summary>A confirmed appointment today, <paramref name="inMinutes"/> from now, booked long ago unless said otherwise.</summary>
    private async Task<int> BookAsync(int inMinutes, int? doctorId = null, AppointmentStatus status = AppointmentStatus.Confirmed,
        DateOnly? date = null, int bookedMinutesAgo = 3 * 24 * 60)
    {
        var id = await C.AddAppointmentAsync(doctorId ?? Doc.DoctorId, Pat.PatientId, date ?? Today, InMinutes(inMinutes), status);
        await using var db = Db();
        var a = await db.Appointments.SingleAsync(x => x.Id == id);
        a.createdAt = DateTime.UtcNow.AddMinutes(-bookedMinutesAgo);
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<int> RemindersAsync()
    {
        await using var db = Db();
        return await db.Notifications.CountAsync(n => n.UserId == Pat.UserId && n.Type == NotificationType.Reminder);
    }

    private async Task<bool> FlagAsync(int appointmentId)
    {
        await using var db = Db();
        return (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointmentId)).ReminderSent;
    }

    [Fact]
    public async Task An_appointment_in_15_minutes_gets_one_reminder_and_is_marked()
    {
        if (NearMidnight) return;
        var id = await BookAsync(15);

        await RunAsync();

        (await RemindersAsync()).ShouldBe(1);
        (await FlagAsync(id)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_appointment_two_hours_away_is_left_alone()
    {
        if (NearMidnight) return;
        var id = await BookAsync(120);

        await RunAsync();

        (await RemindersAsync()).ShouldBe(0);
        (await FlagAsync(id)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_same_reminder_is_not_sent_twice()
    {
        if (NearMidnight) return;
        await BookAsync(15);

        await RunAsync();
        await RunAsync();

        (await RemindersAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task A_patient_who_booked_inside_the_window_is_not_reminded_but_the_booking_is_marked()
    {
        if (NearMidnight) return;
        var id = await BookAsync(15, bookedMinutesAgo: 1);      // the booking notification just reached him

        await RunAsync();

        (await RemindersAsync()).ShouldBe(0);
        (await FlagAsync(id)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_rescheduled_booking_is_judged_by_the_time_of_the_reschedule()
    {
        if (NearMidnight) return;
        var id = await BookAsync(15);                            // booked 3 days ago ...
        await using (var db = Db())
        {
            var a = await db.Appointments.SingleAsync(x => x.Id == id);
            a.RescheduleCount = 1;
            a.updateAt = DateTime.UtcNow.AddMinutes(-1);         // ... but moved to this slot a minute ago
            await db.SaveChangesAsync();
        }

        await RunAsync();

        (await RemindersAsync()).ShouldBe(0);
        (await FlagAsync(id)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task Only_confirmed_appointments_are_reminded(AppointmentStatus status)
    {
        if (NearMidnight) return;
        await BookAsync(15, status: status);

        await RunAsync();

        (await RemindersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task An_appointment_whose_time_has_passed_or_that_is_tomorrow_gets_nothing()
    {
        if (NearMidnight) return;
        await BookAsync(-10);
        await BookAsync(15, date: Today.AddDays(1));

        await RunAsync();

        (await RemindersAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_doctor_with_long_slots_gives_the_patient_a_longer_notice()
    {
        if (NearMidnight) return;
        var d = await C.AddDoctorAsync();
        await C.AddScheduleAsync(d.DoctorId, Today.DayOfWeek, new TimeOnly(0, 0), new TimeOnly(23, 59), 60);
        var id = await BookAsync(45, doctorId: d.DoctorId);      // 45 min is inside a 60-minute notice, outside the default 30

        await RunAsync();

        (await RemindersAsync()).ShouldBe(1);
        (await FlagAsync(id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_schedule_today_the_notice_is_30_minutes()
    {
        if (NearMidnight) return;
        var id = await BookAsync(45);                            // Doc has no schedule for today in this test base unless today is Monday

        await using (var db = Db())
        {
            db.DoctorSchedules.RemoveRange(db.DoctorSchedules.Where(s => s.DoctorId == Doc.DoctorId));
            await db.SaveChangesAsync();
        }

        await RunAsync();

        (await RemindersAsync()).ShouldBe(0);
        (await FlagAsync(id)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_suspended_center_is_not_processed()
    {
        if (NearMidnight) return;
        await BookAsync(15);
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            (await shared.MedicalCenters.SingleAsync(c => c.Id == C.Id)).CenterStatus = CenterStatus.Suspended;
            await shared.SaveChangesAsync();
        }

        await RunAsync();

        (await RemindersAsync()).ShouldBe(0);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_job_instances_running_at_the_same_moment_send_one_reminder()
    {
        if (NearMidnight) return;
        await BookAsync(15);

        await Task.WhenAll(RunAsync(), RunAsync());      // a deploy with two servers, or a restart overlap

        (await RemindersAsync()).ShouldBe(1);
    }
}
