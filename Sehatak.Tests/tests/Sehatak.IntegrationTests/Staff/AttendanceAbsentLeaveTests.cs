using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Staff;

/// <summary>The admin marks someone absent for today; a staff member marks himself on leave for today.</summary>
[Collection(SqlServerCollection.Name)]
public class AttendanceAbsentLeaveTests(SqlServerFixture fx) : StaffTestBase(fx)
{
    private int _shift;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _shift = await AddStaffShiftAsync(Nurse, Today);
    }

    private Task<string> Absent(int userId, DateOnly? date = null) =>
        NewAttendanceService().AbsentStaffAsync(C.Id, new StaffAbsentRequestDto { userId = userId, AttendanceDate = date ?? Today });

    private Task<string> Leave(int userId, DateOnly? date = null) =>
        NewAttendanceService().OnLeaveAsync(C.Id, userId, new StaffOnLeaveRequestDto { AttendanceDate = date ?? Today });

    // ------------------------------------------------------------------ absent (admin)

    [Fact]
    public async Task Admin_marks_a_person_absent()
    {
        var msg = await Absent(Nurse);

        msg.ShouldNotBeNullOrWhiteSpace();
        var a = (await AttendanceOfAsync(Nurse, Today))!;
        a.attendanceStatus.ShouldBe(AttendanceStatus.Absent);
        a.StaffShiftId.ShouldBe(_shift);
        a.CheckInTime.ShouldBeNull();
        a.CheckOutTime.ShouldBeNull();
        (await CountAuditAsync("StaffAttendance", a.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Absent_AlreadyExsist_when_the_day_already_has_a_record()
    {
        await SeedAttendanceAsync(Nurse, _shift, Today, AttendanceStatus.Present, checkIn: DateTime.UtcNow);
        await ShouldFailWith("Attendance.AlreadyExsist", () => Absent(Nurse));
        (await AttendanceOfAsync(Nurse, Today))!.attendanceStatus.ShouldBe(AttendanceStatus.Present);
    }

    [Fact]
    public async Task Absent_Date_Invalid_unless_today()
    {
        await ShouldFailWith("Date.Invalid", () => Absent(Nurse, Today.AddDays(-1)));
        await ShouldFailWith("Date.Invalid", () => Absent(Nurse, Today.AddDays(1)));
    }

    [Fact]
    public async Task Absent_User_NotFound_for_unknown_inactive_or_no_shift_today()
    {
        var inactive = await AddStaffAsync(active: false);
        await AddStaffShiftAsync(inactive, Today);
        var noShift = await AddStaffAsync();

        await ShouldFailWith("User.NotFound", () => Absent(999_999));
        await ShouldFailWith("User.NotFound", () => Absent(inactive));
        await ShouldFailWith("User.NotFound", () => Absent(noShift));
    }

    [Fact]
    public async Task Absent_Shift_NotFound_when_the_shift_has_no_hours_configured()
    {
        var other = await AddStaffAsync();
        await AddStaffShiftAsync(other, Today, ShiftGroup.B);
        await ShouldFailWith("Shift.NotFound", () => Absent(other));
    }

    [Fact]
    public Task Absent_Center_NotFound() =>
        ShouldFailWith("Center.NotFound",
            () => NewAttendanceService().AbsentStaffAsync(999_999, new() { userId = Nurse, AttendanceDate = Today }));

    /// <summary>Marking someone ABSENT answers "leave recorded successfully" (same text as on-leave), and check-out answers "attendance recorded".</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Absent_and_leave_give_different_messages()
    {
        var other = await AddStaffAsync();
        await AddStaffShiftAsync(other, Today);

        var absent = await Absent(Nurse);
        var leave = await Leave(other);

        absent.ShouldNotBe(leave);
    }

    // ------------------------------------------------------------------ on leave (self)

    [Fact]
    public async Task Staff_member_records_leave_for_today()
    {
        var msg = await Leave(Nurse);

        msg.ShouldNotBeNullOrWhiteSpace();
        var a = (await AttendanceOfAsync(Nurse, Today))!;
        a.attendanceStatus.ShouldBe(AttendanceStatus.OnLeave);
        a.StaffShiftId.ShouldBe(_shift);
        a.CheckInTime.ShouldBeNull();
        (await CountAuditAsync("StaffAttendance", a.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Leave_AlreadyExsist_the_second_time()
    {
        await Leave(Nurse);
        await ShouldFailWith("Attendance.AlreadyExsist", () => Leave(Nurse));
    }

    /// <summary>The "already" check only looks for an OnLeave row. If the person already checked in (or was marked absent) the insert hits the unique (user, date) index and the user sees a server error instead of Attendance.AlreadyExsist.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(AttendanceStatus.Present)]
    [InlineData(AttendanceStatus.Late)]
    [InlineData(AttendanceStatus.Absent)]
    public async Task Leave_AlreadyExsist_when_the_day_already_has_another_record(AttendanceStatus status)
    {
        await SeedAttendanceAsync(Nurse, _shift, Today, status, checkIn: status is AttendanceStatus.Absent ? null : DateTime.UtcNow);

        var error = await Record.ExceptionAsync(() => Leave(Nurse));

        error.ShouldBeOfType<BusinessException>();
        ((BusinessException)error!).Message.ShouldBe("Attendance.AlreadyExsist");
    }

    [Fact]
    public async Task Leave_Date_Invalid_unless_today()
    {
        await ShouldFailWith("Date.Invalid", () => Leave(Nurse, Today.AddDays(1)));
        await ShouldFailWith("Date.Invalid", () => Leave(Nurse, Today.AddDays(-1)));
    }

    [Fact]
    public async Task Leave_User_NotFound_for_unknown_inactive_or_no_shift_today()
    {
        var inactive = await AddStaffAsync(active: false);
        await AddStaffShiftAsync(inactive, Today);
        var noShift = await AddStaffAsync();

        await ShouldFailWith("User.NotFound", () => Leave(999_999));
        await ShouldFailWith("User.NotFound", () => Leave(inactive));
        await ShouldFailWith("User.NotFound", () => Leave(noShift));
    }

    [Fact]
    public async Task Leave_Shift_NotFound_when_the_shift_has_no_hours_configured()
    {
        var other = await AddStaffAsync();
        await AddStaffShiftAsync(other, Today, ShiftGroup.B);
        await ShouldFailWith("Shift.NotFound", () => Leave(other));
    }

    [Fact]
    public Task Leave_Center_NotFound() =>
        ShouldFailWith("Center.NotFound",
            () => NewAttendanceService().OnLeaveAsync(999_999, Nurse, new() { AttendanceDate = Today }));
}
