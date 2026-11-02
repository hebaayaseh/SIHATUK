using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Staff;

/// <summary>Admin side: define shift hours (A/B/C), assign staff to a shift on a date, change or delete the hours.</summary>
[Collection(SqlServerCollection.Name)]
public class ShiftScheduleAdminTests(SqlServerFixture fx) : StaffTestBase(fx)
{
    private static readonly TimeOnly T2200 = new(22, 0);
    private static readonly TimeOnly T0600 = new(6, 0);

    // ------------------------------------------------------------------ AddShiftSchedule

    [Fact]
    public async Task Adds_a_shift_schedule_and_audits_it()
    {
        var r = await NewShiftService().AddShiftSchedule(C.Id, new() { ShiftName = ShiftGroup.B, StartTime = new(14, 0), EndTime = new(22, 0) });

        r.ShiftName.ShouldBe(ShiftGroup.B);
        r.StartTime.ShouldBe(new TimeOnly(14, 0));
        r.EndTime.ShouldBe(new TimeOnly(22, 0));
        (await CountAuditAsync("AddShiftSchedule", r.Id)).ShouldBe(1);
        await using var db = Db();
        (await db.shiftSchedules.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task A_night_shift_that_wraps_past_midnight_is_allowed()
    {
        var r = await NewShiftService().AddShiftSchedule(C.Id, new() { ShiftName = ShiftGroup.C, StartTime = T2200, EndTime = T0600 });
        r.StartTime.ShouldBe(T2200);
        r.EndTime.ShouldBe(T0600);
    }

    [Fact]
    public async Task ShiftSchedule_AlreadyExists_for_the_same_name()
    {
        await ShouldFailWith("ShiftSchedule.AlreadyExists",
            () => NewShiftService().AddShiftSchedule(C.Id, new() { ShiftName = ShiftGroup.A, StartTime = T0900, EndTime = T1700 }));
    }

    [Fact]
    public Task Add_Center_NotFound() =>
        ShouldFailWith("Center.NotFound",
            () => NewShiftService().AddShiftSchedule(999_999, new() { ShiftName = ShiftGroup.B, StartTime = T0900, EndTime = T1700 }));

    // ------------------------------------------------------------------ AssignShiftToStaff

    private Task<string> Assign(int userId, DateOnly date, ShiftGroup group = ShiftGroup.A) =>
        NewShiftService().AssignShiftToStaffAsync(C.Id, new AssignShiftToStaffRequestDto { UserId = userId, ShiftDate = date, ShiftName = group });

    [Theory]
    [InlineData(userRole.Nurse)]
    [InlineData(userRole.GeneralDoctor)]
    [InlineData(userRole.LabTechnician)]
    public async Task Assigns_a_shift_and_tells_the_staff_member(userRole role)
    {
        var staff = await AddStaffAsync(role);
        var date = Today.AddDays(2);

        var msg = await Assign(staff, date);

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        var s = await db.StaffShifts.AsNoTracking().SingleAsync(x => x.UserId == staff);
        s.ShiftDate.ShouldBe(date);
        s.ShiftName.ShouldBe(ShiftGroup.A);
        s.IsActive.ShouldBeTrue();
        var note = (await NotificationsOfAsync(staff)).Single();
        note.Type.ShouldBe(NotificationType.Shift);
        ShouldBeNowUtc(note.CreatedAt);
        (await CountAuditAsync("AssignShiftToStaff", s.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_person_can_get_shifts_on_different_days()
    {
        await Assign(Nurse, Today.AddDays(1));
        await Assign(Nurse, Today.AddDays(2));
        await using var db = Db();
        (await db.StaffShifts.CountAsync(s => s.UserId == Nurse)).ShouldBe(2);
    }

    [Fact]
    public async Task StaffShift_AlreadyAssigned_for_the_same_shift_and_date()
    {
        await Assign(Nurse, Today.AddDays(1));
        await ShouldFailWith("StaffShift.AlreadyAssigned", () => Assign(Nurse, Today.AddDays(1)));
        await using var db = Db();
        (await db.StaffShifts.CountAsync(s => s.UserId == Nurse)).ShouldBe(1);
    }

    [Fact]
    public async Task A_deactivated_assignment_can_be_assigned_again()
    {
        await AddStaffShiftAsync(Nurse, Today.AddDays(1), ShiftGroup.A, active: false);
        await Assign(Nurse, Today.AddDays(1));
    }

    /// <summary>The duplicate check includes the shift name, so a person gets shift A and shift B on the same date. Attendance has one row per person per day, so check-in then picks one of them at random.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_person_cannot_have_two_different_shifts_on_the_same_date()
    {
        await AddShiftScheduleAsync(ShiftGroup.B, new(14, 0), new(22, 0));
        await Assign(Nurse, Today.AddDays(1), ShiftGroup.A);
        await ShouldFailWith("StaffShift.AlreadyAssigned", () => Assign(Nurse, Today.AddDays(1), ShiftGroup.B));
    }

    [Fact]
    public async Task ShiftSchedule_NotConfigured_when_the_shift_has_no_hours()
    {
        await ShouldFailWith("ShiftSchedule.NotConfigured", () => Assign(Nurse, Today.AddDays(1), ShiftGroup.C));
    }

    [Fact]
    public async Task Staff_NotFound_for_a_patient_inactive_or_unknown_user()
    {
        var inactive = await AddStaffAsync(active: false);
        await ShouldFailWith("Staff.NotFound", () => Assign(Pat.UserId, Today.AddDays(1)));
        await ShouldFailWith("Staff.NotFound", () => Assign(inactive, Today.AddDays(1)));
        await ShouldFailWith("Staff.NotFound", () => Assign(999_999, Today.AddDays(1)));
    }

    [Fact]
    public Task Assign_Center_NotFound() =>
        ShouldFailWith("Center.NotFound",
            () => NewShiftService().AssignShiftToStaffAsync(999_999, new() { UserId = Nurse, ShiftDate = Today, ShiftName = ShiftGroup.A }));

    // ------------------------------------------------------------------ DeleteShiftScheduale

    [Fact]
    public async Task Delete_removes_the_hours_and_deactivates_upcoming_assignments_only()
    {
        var other = await AddStaffAsync();
        var past = await AddStaffShiftAsync(Nurse, Today.AddDays(-3));
        var today = await AddStaffShiftAsync(Nurse, Today);
        var future = await AddStaffShiftAsync(other, Today.AddDays(4));
        await AddShiftScheduleAsync(ShiftGroup.B, new(14, 0), new(22, 0));
        var otherShift = await AddStaffShiftAsync(other, Today.AddDays(5), ShiftGroup.B);
        var id = await ScheduleIdAsync(ShiftGroup.A);

        var msg = await NewShiftService().DeleteShiftSchedualeAsync(C.Id, id);

        msg.ShouldNotBeNullOrWhiteSpace();
        await using var db = Db();
        (await db.shiftSchedules.AnyAsync(s => s.ShiftName == ShiftGroup.A)).ShouldBeFalse();
        (await db.shiftSchedules.AnyAsync(s => s.ShiftName == ShiftGroup.B)).ShouldBeTrue();
        (await LoadStaffShiftAsync(past)).IsActive.ShouldBeTrue();
        (await LoadStaffShiftAsync(today)).IsActive.ShouldBeFalse();
        (await LoadStaffShiftAsync(future)).IsActive.ShouldBeFalse();
        (await LoadStaffShiftAsync(otherShift)).IsActive.ShouldBeTrue();
        (await NotificationsOfAsync(other)).Count.ShouldBeGreaterThan(0);
        (await CountAuditAsync("DeleteShiftSchedule", id)).ShouldBe(1);
    }

    /// <summary>One notification per affected shift DAY: a nurse with a month of shifts gets 30 identical messages.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Delete_sends_one_notification_per_person()
    {
        for (var i = 1; i <= 3; i++) await AddStaffShiftAsync(Nurse, Today.AddDays(i));
        await NewShiftService().DeleteShiftSchedualeAsync(C.Id, await ScheduleIdAsync(ShiftGroup.A));
        (await NotificationsOfAsync(Nurse)).Count.ShouldBe(1);
    }

    [Fact]
    public Task Delete_Shift_NotFound() =>
        ShouldFailWith("Shift.NotFound", () => NewShiftService().DeleteShiftSchedualeAsync(C.Id, 999_999));

    [Fact]
    public Task Delete_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewShiftService().DeleteShiftSchedualeAsync(999_999, 1));

    // ------------------------------------------------------------------ UpdateShiftSchedule

    private Task<ShiftScheduleResponse> Update(UpdateShiftSchedualRequestDto req) => NewShiftService().UpdateShiftScheduleAsync(C.Id, req);

    private async Task<int> ScheduleIdAsync(ShiftGroup group)
    {
        await using var db = Db();
        return await db.shiftSchedules.Where(s => s.ShiftName == group).Select(s => s.Id).SingleAsync();
    }

    [Fact]
    public async Task Updates_the_hours_and_tells_staff_with_upcoming_shifts()
    {
        var id = await ScheduleIdAsync(ShiftGroup.A);
        await AddStaffShiftAsync(Nurse, Today.AddDays(1));
        var staffWithoutUpcoming = await AddStaffAsync();
        await AddStaffShiftAsync(staffWithoutUpcoming, Today.AddDays(-2));

        var r = await Update(new() { ShiftId = id, StartTime = new(8, 0), EndTime = new(16, 0) });

        r.StartTime.ShouldBe(new TimeOnly(8, 0));
        r.EndTime.ShouldBe(new TimeOnly(16, 0));
        r.ShiftName.ShouldBe(ShiftGroup.A);
        await using var db = Db();
        var row = await db.shiftSchedules.AsNoTracking().SingleAsync(s => s.Id == id);
        row.StartTime.ShouldBe(new TimeOnly(8, 0));
        (await NotificationsOfAsync(Nurse)).Count.ShouldBeGreaterThan(0);
        (await NotificationsOfAsync(staffWithoutUpcoming)).ShouldBeEmpty();
        (await CountAuditAsync("UpdateShiftSchedule", id)).ShouldBe(1);
    }

    /// <summary>Same as delete: one message per shift day.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Update_sends_one_notification_per_person()
    {
        for (var i = 1; i <= 3; i++) await AddStaffShiftAsync(Nurse, Today.AddDays(i));
        await Update(new() { ShiftId = await ScheduleIdAsync(ShiftGroup.A), StartTime = new(8, 0), EndTime = new(16, 0) });
        (await NotificationsOfAsync(Nurse)).Count.ShouldBe(1);
    }

    /// <summary>StaffShift keeps the shift NAME, not an id. Renaming A to B leaves every upcoming assignment pointing at "A", which now has no hours, so those people can no longer check in (Shift.NotFound).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Renaming_a_shift_moves_the_upcoming_assignments_with_it()
    {
        var id = await ScheduleIdAsync(ShiftGroup.A);
        var upcoming = await AddStaffShiftAsync(Nurse, Today.AddDays(2));

        await Update(new() { ShiftId = id, ShiftName = ShiftGroup.B });

        (await LoadStaffShiftAsync(upcoming)).ShiftName.ShouldBe(ShiftGroup.B);
    }

    /// <summary>Rename to a name that already has hours: two schedules with the same name, and every lookup by name (FirstOrDefault) then picks one of them.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Renaming_to_an_existing_shift_name_is_refused()
    {
        await AddShiftScheduleAsync(ShiftGroup.B, new(14, 0), new(22, 0));
        var id = await ScheduleIdAsync(ShiftGroup.A);
        await ShouldFailWith("ShiftSchedule.AlreadyExists", () => Update(new() { ShiftId = id, ShiftName = ShiftGroup.B }));
    }

    [Fact]
    public Task Update_Shift_NotFound() =>
        ShouldFailWith("Shift.NotFound", () => Update(new() { ShiftId = 999_999, StartTime = T0900, EndTime = T1700 }));

    [Fact]
    public Task Update_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewShiftService().UpdateShiftScheduleAsync(999_999, new() { ShiftId = 1, StartTime = T0900, EndTime = T1700 }));
}
