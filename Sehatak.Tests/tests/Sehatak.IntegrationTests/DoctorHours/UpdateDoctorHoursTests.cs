using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.DoctorHours;

/// <summary>
/// Admin changes a working window. The old row is deactivated and a new one is created;
/// Confirmed appointments that fell in the old window are postponed and the patients are told.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class UpdateDoctorHoursTests(SqlServerFixture fx) : DoctorHoursTestBase(fx)
{
    private int _sched;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _sched = await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Monday, T09, T11, 30);
    }

    private Task<Sehatak.Application.DTOs.AddDoctorDailyHour.UpdateDoctorDailyHoursResponse> Update(
        DayOfWeek day, TimeOnly start, TimeOnly end, int slot = 30, int? scheduleId = null, int? doctorId = null, int? userId = null) =>
        NewHoursService().UpdateDoctorDailyHoursAsync(C.Id, userId ?? Admin, doctorId ?? Dr.DoctorId,
            UpdReq(scheduleId ?? _sched, day, start, end, slot));

    [Fact]
    public async Task Replaces_the_schedule_with_a_new_active_one()
    {
        var r = await Update(DayOfWeek.Tuesday, T10, T12, 20);

        r.DoctorId.ShouldBe(Dr.DoctorId);
        r.DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
        r.StartTime.ShouldBe(T10);
        r.EndTime.ShouldBe(T12);
        r.SlotDurationMinutes.ShouldBe(20);

        var all = await SchedulesOfAsync(Dr.DoctorId);
        all.Count.ShouldBe(2);
        (await LoadScheduleAsync(_sched)).IsActive.ShouldBeFalse();
        var fresh = all.Single(s => s.Id != _sched);
        fresh.IsActive.ShouldBeTrue();
        fresh.DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
        fresh.StartTime.ShouldBe(T10);
        fresh.EndTime.ShouldBe(T12);
        fresh.SlotDurationMinutes.ShouldBe(20);
        (await CountAuditAsync("UpdateDoctorSchedule", _sched)).ShouldBe(1);
    }

    [Fact]
    public async Task Postpones_confirmed_appointments_in_the_old_window_and_tells_the_patient()
    {
        var a1 = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900);
        var a2 = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate.AddDays(7), S1030);

        await Update(DayOfWeek.Monday, T10, T12);

        (await LoadApptAsync(a1)).appointmentStatus.ShouldBe(AppointmentStatus.Postponed);
        (await LoadApptAsync(a2)).appointmentStatus.ShouldBe(AppointmentStatus.Postponed);

        var p = (await PostponedOfAsync(a1)).Single();
        p.Type.ShouldBe(PostponeType.DoctorAppointment);
        p.Status.ShouldBe(PostponeStatus.Active);
        p.PatientId.ShouldBe(Pat.PatientId);
        p.CreatedByUserId.ShouldBe(Admin);
        ShouldBeNowUtc(p.CreatedAt);

        var notes = await NotificationsOfAsync(Pat.UserId);
        notes.Count.ShouldBe(2);
        notes.ShouldAllBe(n => n.Type == NotificationType.Cancellation && !n.IsRead);
    }

    [Fact]
    public async Task A_childs_notification_goes_to_the_parent()
    {
        var child = await C.AddSubPatientAsync(Pat);
        await C.AddAppointmentAsync(Dr.DoctorId, child.PatientId, MonDate, S0900);

        await Update(DayOfWeek.Monday, T10, T12);

        (await NotificationsOfAsync(Pat.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Leaves_everything_else_alone()
    {
        var other = await C.AddDoctorAsync();
        await C.AddScheduleAsync(other.DoctorId, DayOfWeek.Monday, T09, T11, 30);

        var outside = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, new TimeOnly(11, 0));   // end is exclusive
        var otherDay = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate.AddDays(1), S0900);     // Tuesday
        var othersAppt = await C.AddAppointmentAsync(other.DoctorId, Pat.PatientId, MonDate, S0900);
        var cancelled = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0930, AppointmentStatus.Cancelled);
        var inProgress = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S1000, AppointmentStatus.InProgress);

        await Update(DayOfWeek.Monday, T10, T12);

        (await LoadApptAsync(outside)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await LoadApptAsync(otherDay)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await LoadApptAsync(othersAppt)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await LoadApptAsync(cancelled)).appointmentStatus.ShouldBe(AppointmentStatus.Cancelled);
        (await LoadApptAsync(inProgress)).appointmentStatus.ShouldBe(AppointmentStatus.InProgress);
        (await NotificationsOfAsync(Pat.UserId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Exits_waiting_waitlist_entries_for_the_affected_dates_only()
    {
        await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900);
        var other = await C.AddPatientAsync();
        var onDate = await AddWaitingAsync(Dr.DoctorId, other.PatientId, MonDate);
        var otherDate = await AddWaitingAsync(Dr.DoctorId, other.PatientId, MonDate.AddDays(7));
        var done = await AddWaitingAsync(Dr.DoctorId, other.PatientId, MonDate, WaitlistStatus.Entered);

        await Update(DayOfWeek.Monday, T10, T12);

        (await WaitStatusAsync(onDate)).ShouldBe(WaitlistStatus.Exited);
        (await WaitStatusAsync(otherDate)).ShouldBe(WaitlistStatus.Waiting);
        (await WaitStatusAsync(done)).ShouldBe(WaitlistStatus.Entered);
        (await NotificationsOfAsync(other.UserId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_new_window_can_overlap_the_one_being_replaced()
    {
        var r = await Update(DayOfWeek.Monday, T09, T10);      // shrinking 09-11 to 09-10
        r.EndTime.ShouldBe(T10);
    }

    [Fact]
    public async Task Schedule_Conflict_with_another_active_window()
    {
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Tuesday, T09, T11, 30);
        await ShouldFailWith("Schedule.Conflict", () => Update(DayOfWeek.Tuesday, T10, T12));
        (await LoadScheduleAsync(_sched)).IsActive.ShouldBeTrue();
        (await SchedulesOfAsync(Dr.DoctorId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task An_inactive_window_does_not_conflict()
    {
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Tuesday, T09, T11, 30, isActive: false);
        (await Update(DayOfWeek.Tuesday, T10, T12)).DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
    }

    [Fact]
    public async Task A_conflict_changes_nothing_not_even_the_appointments()
    {
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Tuesday, T09, T11, 30);
        var a = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900);

        await ShouldFailWith("Schedule.Conflict", () => Update(DayOfWeek.Tuesday, T10, T12));

        (await LoadApptAsync(a)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await PostponedOfAsync(a)).ShouldBeEmpty();
    }

    /// <summary>The query takes every Confirmed appointment on that weekday in the window, with no date filter, so a past Confirmed appointment (a no-show nobody closed) is "postponed" and its patient gets a message about a visit that already passed.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Past_appointments_are_not_postponed()
    {
        var past = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, PastDateOn(DayOfWeek.Monday), S0900);

        await Update(DayOfWeek.Monday, T10, T12);

        (await LoadApptAsync(past)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await NotificationsOfAsync(Pat.UserId)).ShouldBeEmpty();
    }

    /// <summary>Saving the same day / hours / slot length postpones every booked patient for nothing.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task An_update_that_changes_nothing_is_refused_and_postpones_nobody()
    {
        var a = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, MonDate, S0900);

        await ShouldFailWith("Schedule.NoChanges", () => Update(DayOfWeek.Monday, T09, T11, 30));

        (await LoadApptAsync(a)).appointmentStatus.ShouldBe(AppointmentStatus.Confirmed);
        (await SchedulesOfAsync(Dr.DoctorId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task General_NotFound_for_an_inactive_schedule()
    {
        var inactive = await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Friday, T09, T11, 30, isActive: false);
        await ShouldFailWith("General.NotFound", () => Update(DayOfWeek.Friday, T09, T11, scheduleId: inactive));
    }

    [Fact]
    public async Task General_NotFound_for_another_doctors_schedule()
    {
        var other = await C.AddDoctorAsync();
        var theirs = await C.AddScheduleAsync(other.DoctorId, DayOfWeek.Friday, T09, T11, 30);
        await ShouldFailWith("General.NotFound", () => Update(DayOfWeek.Friday, T09, T11, scheduleId: theirs));
        (await LoadScheduleAsync(theirs)).IsActive.ShouldBeTrue();
    }

    [Fact]
    public Task General_NotFound_for_an_unknown_schedule() =>
        ShouldFailWith("General.NotFound", () => Update(DayOfWeek.Friday, T09, T11, scheduleId: 999_999));

    [Fact]
    public async Task Auth_Forbidden_for_a_non_admin()
    {
        var rec = await C.AddReceptionistAsync();
        await ShouldFailWith("Auth.Forbidden", () => Update(DayOfWeek.Friday, T09, T11, userId: rec));
        (await LoadScheduleAsync(_sched)).IsActive.ShouldBeTrue();
    }

    [Fact]
    public Task Doctor_NotFound_for_an_unknown_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Update(DayOfWeek.Friday, T09, T11, doctorId: 999_999));

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound",
            () => NewHoursService().UpdateDoctorDailyHoursAsync(999_999, Admin, Dr.DoctorId, UpdReq(_sched, DayOfWeek.Friday, T09, T11)));
}
