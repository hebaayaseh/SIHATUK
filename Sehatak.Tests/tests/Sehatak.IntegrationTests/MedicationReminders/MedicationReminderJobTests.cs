using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Infrastructure.Data;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Job = Sehatak.Infrastructure.Services.SuperAdminService.Background.MedicationReminderJob;

namespace Sehatak.IntegrationTests.MedicationReminders;

/// <summary>
/// The job fires on the exact clinic minute, so tests seed a reminder for "this minute" and only assert when the minute did not
/// roll over while the job ran (otherwise the result is legitimately different and the test just returns).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class MedicationReminderJobTests(SqlServerFixture fx) : ReminderTestBase(fx)
{
    private static TimeOnly NowMinute() { var n = ClinicClock.Now; return new TimeOnly(n.Hour, n.Minute); }
    private static ReminderDays TodayFlag => (ReminderDays)(1 << (int)Today.DayOfWeek);

    private async Task RunPassAsync()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Fx.Harness.NewSharedDb());
        services.AddSingleton(Fx.Harness.TenantFactory);
        using var provider = services.BuildServiceProvider();
        var job = new Job(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<Job>.Instance);
        var m = typeof(Job).GetMethod("CheckMedicationReminders", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)m.Invoke(job, null)!;
    }

    /// <summary>Seeds for this minute, runs the pass, and returns false if the minute changed meanwhile.</summary>
    private async Task<bool> RunInSameMinuteAsync(TimeOnly minute)
    {
        await RunPassAsync();
        return NowMinute() == minute;
    }

    private async Task<List<Notification>> NotificationsAsync(int userId)
    {
        await using var db = Db();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.Type == NotificationType.MedicationReminder).ToListAsync();
    }

    [Fact]
    public async Task A_due_reminder_sends_one_notification_and_marks_today()
    {
        var minute = NowMinute();
        var id = await SeedAsync(Pat.PatientId, minute, name: "Amoxil");

        if (!await RunInSameMinuteAsync(minute)) return;

        var n = (await NotificationsAsync(Pat.UserId)).ShouldHaveSingleItem();
        n.Message.ShouldContain("Amoxil");
        n.IsRead.ShouldBeFalse();
        (await LoadAsync(id)).LastNotifiedDate.ShouldBe(Today);
    }

    [Fact]
    public async Task The_same_reminder_is_not_sent_twice_on_the_same_day()
    {
        var minute = NowMinute();
        await SeedAsync(Pat.PatientId, minute);

        if (!await RunInSameMinuteAsync(minute)) return;
        if (!await RunInSameMinuteAsync(minute)) return;

        (await NotificationsAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_sub_patient_reminder_notifies_the_parent_account()
    {
        var sub = await C.AddSubPatientAsync(Pat);
        var minute = NowMinute();
        await SeedAsync(sub.PatientId, minute);

        if (!await RunInSameMinuteAsync(minute)) return;

        (await NotificationsAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_last_day_notifies_and_then_deactivates_the_reminder()
    {
        var minute = NowMinute();
        var id = await SeedAsync(Pat.PatientId, minute, start: Today.AddDays(-4), end: Today);

        if (!await RunInSameMinuteAsync(minute)) return;

        (await NotificationsAsync(Pat.UserId)).Count.ShouldBe(1);
        (await LoadAsync(id)).IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Reminders_that_are_not_due_stay_silent()
    {
        var minute = NowMinute();
        await SeedAsync(Pat.PatientId, minute.AddHours(5));                                                   // another time of day
        await SeedAsync(Pat.PatientId, minute, days: ReminderDays.All & ~TodayFlag);                           // today not selected
        await SeedAsync(Pat.PatientId, minute, active: false);                                                // switched off
        await SeedAsync(Pat.PatientId, minute, start: Today.AddDays(1), end: Today.AddDays(5));                // not started
        await SeedAsync(Pat.PatientId, minute, start: Today.AddDays(-5), end: Today.AddDays(-1));              // finished
        await SeedAsync(Pat.PatientId, minute, lastNotified: Today);                                          // already sent today

        await RunPassAsync();

        (await NotificationsAsync(Pat.UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_suspended_center_is_not_processed()
    {
        var minute = NowMinute();
        await SeedAsync(Pat.PatientId, minute);
        await SuspendCenterAsync();

        await RunPassAsync();

        (await NotificationsAsync(Pat.UserId)).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ known bugs

    /// <summary>
    /// The job loop is "work, then wait 1 minute", so every run drifts a little later and sooner or later a whole minute is skipped
    /// (and a restart/downtime loses everything in between). The match is `== this minute`; it must catch up on a short window.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_reminder_a_few_minutes_late_is_still_sent_but_an_old_one_is_not()
    {
        var now = NowMinute();
        if (now < new TimeOnly(1, 0)) return;                                  // avoid the midnight wrap

        var recent = await SeedAsync(Pat.PatientId, now.AddMinutes(-4), name: "Recent");
        var stale = await SeedAsync(Pat.PatientId, now.AddHours(-1), name: "Stale");

        await RunPassAsync();

        (await LoadAsync(recent)).LastNotifiedDate.ShouldBe(Today);
        (await LoadAsync(stale)).LastNotifiedDate.ShouldBeNull();
    }

    /// <summary>A failure outside the per-center try/catch (e.g. the shared DB is down for a second) ends the BackgroundService; in .NET 8 that stops the whole API.</summary>
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
