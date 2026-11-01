using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.DoctorHours;

/// <summary>The doctor cancels a whole working day: appointments are cancelled, the day is blocked, the waitlist is released.</summary>
[Collection(SqlServerCollection.Name)]
public class CancelDoctorDayTests(SqlServerFixture fx) : DoctorHoursTestBase(fx)
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Monday, T09, T11, 30);
    }

    private Task<string> Cancel(DateOnly? date = null, int? userId = null) =>
        NewHoursService().CancleDailyHoursAsync(C.Id, userId ?? Dr.UserId, date ?? MonDate);

    [Fact]
    public async Task Cancels_the_days_confirmed_appointments_and_blocks_the_day()
    {
        var a1 = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900);
        var a2 = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0930);

        var msg = await Cancel();

        msg.ShouldNotBeNullOrWhiteSpace();
        (await LoadApptAsync(a1)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await LoadApptAsync(a2)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);

        var p = (await PostponedOfAsync(a1)).Single();
        p.Type.ShouldBe(PostponeType.DoctorAppointment);
        p.Status.ShouldBe(PostponeStatus.Active);
        p.PatientId.ShouldBe(Pat.PatientId);
        p.CreatedByUserId.ShouldBe(Dr.UserId);
        ShouldBeNowUtc(p.CreatedAt);

        var notes = await NotificationsOfAsync(Pat.UserId);
        notes.Count.ShouldBe(2);
        notes.ShouldAllBe(n => n.Type == NotificationType.Cancellation && !n.IsRead);

        var blocked = (await BlockedOfAsync(Dr.DoctorId)).Single();
        blocked.date.ShouldBe(MonDate);
        blocked.timeSlot.ShouldBeNull();
        blocked.isBlocked.ShouldBeTrue();
        blocked.Reason.ShouldNotBeNullOrWhiteSpace();
        ShouldBeNowUtc(blocked.CreatedAt);
    }

    [Fact]
    public async Task A_day_without_appointments_is_just_blocked_with_a_different_message()
    {
        var emptyDay = await Cancel();
        var busyDate = MonDate.AddDays(7);
        await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, busyDate, S0900);
        var busyDay = await Cancel(busyDate);

        emptyDay.ShouldNotBeNullOrWhiteSpace();
        busyDay.ShouldNotBe(emptyDay);
        (await BlockedOfAsync(Dr.DoctorId)).Count.ShouldBe(2);
        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_childs_notification_goes_to_the_parent()
    {
        var child = await C.AddSubPatientAsync(Pat);
        await C.AddAppointmentAsync(Dr.DoctorId, child.PatientId, MonDate, S0900);

        await Cancel();

        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Leaves_other_days_doctors_and_statuses_alone()
    {
        var other = await C.AddDoctorAsync();
        await C.AddScheduleAsync(other.DoctorId, DayOfWeek.Monday, T09, T11, 30);

        var otherDate = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate.AddDays(7), S0900);
        var othersAppt = await C.AddAppointmentAsync(other.DoctorId, Pat.PatientId, MonDate, S0900);
        var inProgress = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900, AppointmentStatus.InProgress);
        var completed = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0930, AppointmentStatus.Completed);

        await Cancel();

        (await LoadApptAsync(otherDate)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await LoadApptAsync(othersAppt)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await LoadApptAsync(inProgress)).appointmentStatus.ShouldBe(AppointmentStatus.InProgress);
        (await LoadApptAsync(completed)).appointmentStatus.ShouldBe(AppointmentStatus.Completed);
        (await NotificationsOfAsync(Pat.UserId)).ShouldBeEmpty();
        (await BlockedOfAsync(other.DoctorId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_booked_follow_up_goes_back_to_Pending_with_8_more_days()
    {
        var scheduled = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900);
        var until = MonDate.AddDays(10);
        int followUpId;
        await using (var db = Db())
        {
            var original = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, ClinicClock.Today.AddDays(-9), null, AppointmentStatus.Completed);
            var f = new Sehatak.Domain.Entities.TenantEntities.FollowUp
            {
                OriginalAppointmentId = original, PatientId = Pat.PatientId, DoctorId = Dr.DoctorId,
                AllowFollowUpDate = until, Status = FollowUpStatus.Booked, ScheduledAppointmentId = scheduled,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };
            db.FollowUps.Add(f);
            await db.SaveChangesAsync();
            followUpId = f.Id;
        }

        await Cancel();

        await using var check = Db();
        var after = await check.FollowUps.AsNoTracking().SingleAsync(f => f.Id == followUpId);
        after.Status.ShouldBe(FollowUpStatus.Pending);
        after.AllowFollowUpDate.ShouldBe(until.AddDays(8));
    }

    [Fact]
    public async Task Exits_waiting_waitlist_entries_of_that_day_only()
    {
        var other = await C.AddPatientAsync();
        var onDay = await AddWaitingAsync(Dr.DoctorId, other.PatientId, MonDate);
        var otherDay = await AddWaitingAsync(Dr.DoctorId, other.PatientId, MonDate.AddDays(7));
        var entered = await AddWaitingAsync(Dr.DoctorId, other.PatientId, MonDate, WaitlistStatus.Entered);

        await Cancel();

        (await WaitStatusAsync(onDay)).ShouldBe(WaitlistStatus.Exited);
        (await WaitStatusAsync(otherDay)).ShouldBe(WaitlistStatus.Waiting);
        (await WaitStatusAsync(entered)).ShouldBe(WaitlistStatus.Entered);
        (await NotificationsOfAsync(other.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Doctor_DayAlreadyBlocked_the_second_time()
    {
        await Cancel();
        await ShouldFailWith("Doctor.DayAlreadyBlocked", () => Cancel());
        (await BlockedOfAsync(Dr.DoctorId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_blocked_single_slot_does_not_count_as_a_blocked_day()
    {
        await SetBlockedAsync(Dr.DoctorId, MonDate, S0900);
        await Cancel();
        (await BlockedOfAsync(Dr.DoctorId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Schedule_NotFound_when_the_doctor_does_not_work_that_weekday()
    {
        await ShouldFailWith("Schedule.NotFound", () => Cancel(MonDate.AddDays(1)));      // Tuesday
        (await BlockedOfAsync(Dr.DoctorId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Schedule_NotFound_when_that_weekdays_schedule_is_inactive()
    {
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Wednesday, T09, T11, 30, isActive: false);
        await ShouldFailWith("Schedule.NotFound", () => Cancel(MonDate.AddDays(2)));
    }

    /// <summary>The audit row is built before SaveChanges, so blockedDay.Id is still 0 and the log points at no row.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Audit_entity_id_is_the_blocked_day_id()
    {
        await Cancel();
        var blockedId = (await BlockedOfAsync(Dr.DoctorId)).Single().Id;
        (await AuditIdAsync("CancelDoctorDay")).ShouldBe(blockedId);
    }

    /// <summary>The doctor can "cancel" a date that already passed: its Confirmed appointments (never closed) are cancelled and patients are told "your appointment today is cancelled".</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_date_in_the_past_is_refused()
    {
        var past = PastDateOn(DayOfWeek.Monday);
        var a = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, past, S0900);

        await ShouldFailWith("Date.Invalid", () => Cancel(past));

        (await LoadApptAsync(a)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await BlockedOfAsync(Dr.DoctorId)).ShouldBeEmpty();
    }

    /// <summary>A Pending appointment (waiting for payment) on the cancelled day is skipped, so it can still be paid and become Confirmed on a day the doctor is not working.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Pending_appointments_of_that_day_are_cancelled_too()
    {
        var pending = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900, AppointmentStatus.Pending);

        await Cancel();

        (await LoadApptAsync(pending)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
    }

    /// <summary>Two parallel requests both pass the "already blocked" check (read first, insert later, no unique index).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Parallel_cancels_block_the_day_and_postpone_each_appointment_once()
    {
        var a = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900);

        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await Cancel(); return true; }
            catch (Sehatak.Application.DTOs.Exceptions.BusinessException) { return false; }
        });
        var results = await Task.WhenAll(tasks);

        results.Count(r => r).ShouldBe(1);
        (await BlockedOfAsync(Dr.DoctorId)).Count.ShouldBe(1);
        (await PostponedOfAsync(a)).Count.ShouldBe(1);
        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public Task Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Cancel(userId: Admin));

    [Fact]
    public async Task Doctor_NotFound_for_an_inactive_doctor()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => Cancel(userId: inactive.UserId));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewHoursService().CancleDailyHoursAsync(999_999, Dr.UserId, MonDate));
}
