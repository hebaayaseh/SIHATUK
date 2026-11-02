using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Staff;

/// <summary>
/// Check-in / check-out for the shift the person has TODAY. Shift A is 09:00-17:00 clinic time.
/// The app sends UTC instants (see StaffTestBase), so "09:30" below means 09:30 in the clinic.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AttendanceCheckTests(SqlServerFixture fx) : StaffTestBase(fx)
{
    private int _shift;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _shift = await AddStaffShiftAsync(Nurse, Today);
    }

    private StaffAttendanceCheckInRequestDto At(int hour, int minute = 0, DateOnly? date = null) =>
        new() { AttendanceDate = date ?? Today, CheckTime = LocalToUtc(date ?? Today, new TimeOnly(hour, minute)) };

    private Task<string> CheckIn(StaffAttendanceCheckInRequestDto req, int? userId = null) =>
        NewAttendanceService().CheckInTimeAsync(C.Id, userId ?? Nurse, req);

    private Task<string> CheckOut(StaffAttendanceCheckInRequestDto req, int? userId = null) =>
        NewAttendanceService().CheckOutTimeAsync(C.Id, userId ?? Nurse, req);

    // ------------------------------------------------------------------ check-in

    /// <summary>The service ignores the time in the request and uses the server clock, so the test can only say "now" and compare the status with the clinic clock at that moment.</summary>
    private static AttendanceStatus ExpectedAfterCheckIn() =>
        TimeOnly.FromDateTime(ClinicClock.Now) > T0900 ? AttendanceStatus.Late : AttendanceStatus.Present;

    [Fact]
    public async Task Check_in_stores_the_server_time_and_the_status_follows_the_clinic_clock()
    {
        var before = ExpectedAfterCheckIn();
        await CheckIn(At(8, 45));                      // the time inside the request is ignored
        var after = ExpectedAfterCheckIn();

        var a = (await AttendanceOfAsync(Nurse, Today))!;
        a.StaffShiftId.ShouldBe(_shift);
        a.CheckOutTime.ShouldBeNull();
        ShouldBeNowUtc(a.CheckInTime!.Value);
        if (before == after)                           // skip only if the clock crossed 09:00 during the call
            a.attendanceStatus.ShouldBe(before);
        (await CountAuditAsync("StaffAttendance", a.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Attendance_AlreadyExsist_on_a_second_check_in()
    {
        await CheckIn(At(8, 50));
        await ShouldFailWith("Attendance.AlreadyExsist", () => CheckIn(At(8, 55)));
    }

    [Theory]
    [InlineData(AttendanceStatus.Absent)]
    [InlineData(AttendanceStatus.OnLeave)]
    public async Task Attendance_AlreadyExsist_when_the_day_was_already_recorded_as_absent_or_leave(AttendanceStatus status)
    {
        await SeedAttendanceAsync(Nurse, _shift, Today, status);
        await ShouldFailWith("Attendance.AlreadyExsist", () => CheckIn(At(8, 50)));
    }

    /// <summary>CheckTime comes from the caller, so a nurse who arrives at noon can send 08:45. The service stores what it was given.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_check_in_time_far_from_now_is_not_accepted_as_given()
    {
        var claimed = DateTime.UtcNow.AddHours(-3);
        var req = new StaffAttendanceCheckInRequestDto { AttendanceDate = Today, CheckTime = claimed };

        var error = await Record.ExceptionAsync(() => CheckIn(req));

        if (error is null)
            ShouldBeNowUtc((await AttendanceOfAsync(Nurse, Today))!.CheckInTime!.Value);
        else
            error.ShouldBeOfType<BusinessException>();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Parallel_check_ins_record_once_and_the_rest_get_a_business_error()
    {
        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await CheckIn(At(8, 50)); return true; }
            catch (BusinessException) { return false; }      // anything else (e.g. unique index violation) fails the test
        });

        var results = await Task.WhenAll(tasks);

        results.Count(r => r).ShouldBe(1);
    }

    [Fact]
    public async Task Date_Invalid_unless_the_date_is_today()
    {
        await ShouldFailWith("Date.Invalid", () => CheckIn(At(8, 50, Today.AddDays(-1))));
        await ShouldFailWith("Date.Invalid", () => CheckIn(At(8, 50, Today.AddDays(1))));
        (await AttendanceOfAsync(Nurse, Today)).ShouldBeNull();
    }

    [Fact]
    public async Task User_NotFound_for_an_unknown_or_inactive_user()
    {
        var inactive = await AddStaffAsync(active: false);
        await AddStaffShiftAsync(inactive, Today);
        await ShouldFailWith("User.NotFound", () => CheckIn(At(8, 50), 999_999));
        await ShouldFailWith("User.NotFound", () => CheckIn(At(8, 50), inactive));
    }

    [Fact]
    public async Task User_NotFound_when_there_is_no_active_shift_today()
    {
        var noShift = await AddStaffAsync();
        var tomorrowOnly = await AddStaffAsync();
        await AddStaffShiftAsync(tomorrowOnly, Today.AddDays(1));
        var deactivated = await AddStaffAsync();
        await AddStaffShiftAsync(deactivated, Today, ShiftGroup.A, active: false);

        await ShouldFailWith("User.NotFound", () => CheckIn(At(8, 50), noShift));
        await ShouldFailWith("User.NotFound", () => CheckIn(At(8, 50), tomorrowOnly));
        await ShouldFailWith("User.NotFound", () => CheckIn(At(8, 50), deactivated));
    }

    [Fact]
    public async Task Shift_NotFound_when_the_shift_has_no_hours_configured()
    {
        var other = await AddStaffAsync();
        await AddStaffShiftAsync(other, Today, ShiftGroup.B);        // B has no schedule row
        await ShouldFailWith("Shift.NotFound", () => CheckIn(At(8, 50), other));
    }

    [Fact]
    public Task Check_in_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewAttendanceService().CheckInTimeAsync(999_999, Nurse, At(8, 50)));

    // ------------------------------------------------------------------ check-out

    private static AttendanceStatus ExpectedAfterCheckOut() =>
        TimeOnly.FromDateTime(ClinicClock.Now) < T1700 ? AttendanceStatus.EarlyOut : AttendanceStatus.Present;

    [Fact]
    public async Task Check_out_stores_the_server_time_and_the_status_follows_the_clinic_clock()
    {
        await SeedAttendanceAsync(Nurse, _shift, Today, AttendanceStatus.Present, checkIn: DateTime.UtcNow.AddHours(-1));

        var before = ExpectedAfterCheckOut();
        await CheckOut(At(17, 30));                    // the time inside the request is ignored
        var after = ExpectedAfterCheckOut();

        var a = (await AttendanceOfAsync(Nurse, Today))!;
        ShouldBeNowUtc(a.CheckOutTime!.Value);
        if (before == after)                           // skip only if the clock crossed 17:00 during the call
            a.attendanceStatus.ShouldBe(before);
    }

    /// <summary>Check-out overwrites the status with Present/EarlyOut, so a person who arrived late and left on time ends the day as Present: the lateness is erased.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Check_out_does_not_erase_a_Late_status()
    {
        await SeedAttendanceAsync(Nurse, _shift, Today, AttendanceStatus.Late, checkIn: LocalToUtc(Today, new(10, 30)));

        await CheckOut(At(17, 30));

        (await AttendanceOfAsync(Nurse, Today))!.attendanceStatus.ShouldBe(AttendanceStatus.Late);
    }

    [Fact]
    public async Task Attendance_AlreadyExsist_on_a_second_check_out()
    {
        await CheckIn(At(8, 50));
        await CheckOut(At(17, 30));
        await ShouldFailWith("Attendance.AlreadyExsist", () => CheckOut(At(18, 0)));
    }

    [Fact]
    public async Task Attendance_NotFound_when_the_person_never_checked_in()
    {
        await ShouldFailWith("Attendance.NotFound", () => CheckOut(At(17, 30)));
        (await AttendanceOfAsync(Nurse, Today)).ShouldBeNull();
    }

    /// <summary>The admin marked the person absent (or on leave). A "check-out" then fills CheckOutTime and flips the status to Present/EarlyOut although there was never a check-in.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(AttendanceStatus.Absent)]
    [InlineData(AttendanceStatus.OnLeave)]
    public async Task Check_out_is_refused_for_a_day_recorded_as_absent_or_leave(AttendanceStatus status)
    {
        await SeedAttendanceAsync(Nurse, _shift, Today, status);

        var error = await Record.ExceptionAsync(() => CheckOut(At(17, 30)));

        error.ShouldBeOfType<BusinessException>();
        var a = (await AttendanceOfAsync(Nurse, Today))!;
        a.attendanceStatus.ShouldBe(status);
        a.CheckOutTime.ShouldBeNull();
    }

    /// <summary>A night shift (22:00-06:00) starts on one date and ends on the next. The person checks out at 06:00 "today" but the shift belongs to "yesterday", so the lookup for today's shift fails and he can never check out.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_night_shift_can_be_checked_out_after_midnight()
    {
        await AddShiftScheduleAsync(ShiftGroup.C, new(22, 0), new(6, 0));
        var night = await AddStaffAsync();
        var yesterday = Today.AddDays(-1);
        var shift = await AddStaffShiftAsync(night, yesterday, ShiftGroup.C);
        await SeedAttendanceAsync(night, shift, yesterday, AttendanceStatus.Present, checkIn: LocalToUtc(yesterday, new(21, 55)));
        var before = TimeOnly.FromDateTime(ClinicClock.Now) < new TimeOnly(6, 0) ? AttendanceStatus.EarlyOut : AttendanceStatus.Present;

        await CheckOut(At(6, 5), night);

        var after = TimeOnly.FromDateTime(ClinicClock.Now) < new TimeOnly(6, 0) ? AttendanceStatus.EarlyOut : AttendanceStatus.Present;
        var a = (await AttendanceOfAsync(night, yesterday))!;
        ShouldBeNowUtc(a.CheckOutTime!.Value);          // the row of YESTERDAY'S shift was closed
        if (before == after)
            a.attendanceStatus.ShouldBe(before);
    }

    [Fact]
    public async Task Check_out_Date_Invalid_unless_today()
    {
        await CheckIn(At(8, 50));
        await ShouldFailWith("Date.Invalid", () => CheckOut(At(17, 30, Today.AddDays(-1))));
    }

    [Fact]
    public async Task Check_out_User_NotFound_without_a_shift_today()
    {
        var noShift = await AddStaffAsync();
        await ShouldFailWith("User.NotFound", () => CheckOut(At(17, 30), noShift));
        await ShouldFailWith("User.NotFound", () => CheckOut(At(17, 30), 999_999));
    }

    [Fact]
    public Task Check_out_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewAttendanceService().CheckOutTimeAsync(999_999, Nurse, At(17, 30)));
}