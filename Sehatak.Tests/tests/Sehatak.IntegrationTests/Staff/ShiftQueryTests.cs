using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Staff;

/// <summary>Admin screens: shift hours list, staff directory, monthly roster per shift with attendance status.</summary>
[Collection(SqlServerCollection.Name)]
public class ShiftQueryTests(SqlServerFixture fx) : StaffTestBase(fx)
{
    private static PagedRequest Page(int n = 1, int size = 10) => new() { PageNumber = n, PageSize = size };

    [Fact]
    public async Task Shifts_list_returns_every_configured_shift()
    {
        await AddShiftScheduleAsync(ShiftGroup.B, new(14, 0), new(22, 0));

        var res = await NewShiftService().GetShiftsSchedulesAsync(C.Id);

        res.ShiftsSchedule.Select(s => s.ShiftName).OrderBy(x => x).ShouldBe([ShiftGroup.A, ShiftGroup.B]);
        var a = res.ShiftsSchedule.Single(s => s.ShiftName == ShiftGroup.A);
        a.StartTime.ShouldBe(T0900);
        a.EndTime.ShouldBe(T1700);
    }

    [Fact]
    public Task Shifts_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewShiftService().GetShiftsSchedulesAsync(999_999));

    // ------------------------------------------------------------------ staff directory

    [Fact]
    public async Task Directory_lists_clinical_and_front_desk_staff_only_ordered_by_name()
    {
        var inactive = await AddStaffAsync(userRole.LabTechnician, active: false);
        var rec = await AddStaffAsync(userRole.Receptionist);
        var doc = await AddStaffAsync(userRole.GeneralDoctor);

        var res = await NewShiftService().GetAllStaffAsync(C.Id, Page());

        var ids = res.Items.Select(i => i.Id).ToList();
        ids.ShouldBe(new[] { Nurse, inactive, rec, doc }.OrderBy(x => x));
        res.Items.ShouldNotContain(i => i.Id == Pat.UserId || i.Id == Doc.UserId || i.Id == C.AdminUserId);
        res.Items.Single(i => i.Id == inactive).isActive.ShouldBeFalse();
        res.Items.Single(i => i.Id == rec).role.ShouldBe(nameof(userRole.Receptionist));
        res.Items.ShouldAllBe(i => !string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Email));
    }

    [Fact]
    public async Task Directory_is_paged()
    {
        for (var i = 0; i < 4; i++) await AddStaffAsync();       // 5 with the nurse

        var p3 = await NewShiftService().GetAllStaffAsync(C.Id, Page(3, 2));

        p3.TotalCount.ShouldBe(5);
        p3.Items.Count.ShouldBe(1);
    }

    [Fact]
    public Task Directory_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewShiftService().GetAllStaffAsync(999_999, Page()));

    // ------------------------------------------------------------------ monthly roster

    private DateOnly NextMonthDay(int day)
    {
        var first = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1);
        return first.AddDays(day - 1);
    }

    private (int Year, int Month) NextMonth() => (NextMonthDay(1).Year, NextMonthDay(1).Month);

    [Fact]
    public async Task Roster_groups_a_persons_days_with_their_attendance_status()
    {
        var (y, m) = NextMonth();
        var d1 = await AddStaffShiftAsync(Nurse, NextMonthDay(2));
        var d2 = await AddStaffShiftAsync(Nurse, NextMonthDay(1));
        await AddStaffShiftAsync(Nurse, NextMonthDay(3), active: false);
        await SeedAttendanceAsync(Nurse, d1, NextMonthDay(2), AttendanceStatus.Late);

        var res = await NewShiftService().GetStaffsWithShiftAsync(C.Id, ShiftGroup.A, Page(), y, m);

        res.TotalCount.ShouldBe(1);
        var person = res.Items.Single();
        person.userId.ShouldBe(Nurse);
        person.role.ShouldBe(nameof(userRole.Nurse));
        person.userIsActive.ShouldBeTrue();
        person.name.ShouldNotBeNullOrWhiteSpace();
        person.Days.Select(d => d.Date).ToList().ShouldBe([NextMonthDay(1), NextMonthDay(2), NextMonthDay(3)]);
        person.Days[0].Status.ShouldBeNull();
        person.Days[1].Status.ShouldBe(AttendanceStatus.Late);
        person.Days[2].isActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Roster_leaves_out_other_shifts_and_other_months()
    {
        var (y, m) = NextMonth();
        var other = await AddStaffAsync();
        await AddShiftScheduleAsync(ShiftGroup.B, new(14, 0), new(22, 0));
        await AddStaffShiftAsync(Nurse, NextMonthDay(2));
        await AddStaffShiftAsync(other, NextMonthDay(2), ShiftGroup.B);
        await AddStaffShiftAsync(other, NextMonthDay(2).AddMonths(1));        // not in the asked month

        var res = await NewShiftService().GetStaffsWithShiftAsync(C.Id, ShiftGroup.A, Page(), y, m);

        res.Items.Select(i => i.userId).ShouldBe([Nurse]);
    }

    [Fact]
    public async Task Roster_is_paged_by_person_in_name_order()
    {
        var (y, m) = NextMonth();
        var people = new List<int> { Nurse };
        for (var i = 0; i < 2; i++) people.Add(await AddStaffAsync());
        foreach (var p in people) { await AddStaffShiftAsync(p, NextMonthDay(2)); await AddStaffShiftAsync(p, NextMonthDay(3)); }

        var p2 = await NewShiftService().GetStaffsWithShiftAsync(C.Id, ShiftGroup.A, Page(2, 2), y, m);

        p2.TotalCount.ShouldBe(3);
        p2.Items.Count.ShouldBe(1);
        p2.Items[0].userId.ShouldBe(people.Max());
        p2.Items[0].Days.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Roster_is_empty_when_nobody_is_assigned()
    {
        var (y, m) = NextMonth();
        var res = await NewShiftService().GetStaffsWithShiftAsync(C.Id, ShiftGroup.A, Page(), y, m);
        res.Items.ShouldBeEmpty();
        res.TotalCount.ShouldBe(0);
    }

    [Fact]
    public Task Roster_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewShiftService().GetStaffsWithShiftAsync(999_999, ShiftGroup.A, Page()));
}
