using FluentValidation;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AddDoctorDailyHour;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.DoctorSchedule;
using Sehatak.Application.Validators.Shift;
using Sehatak.Domain.Enums;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class ShiftValidatorTests
{
    private static readonly TimeOnly T8 = new(8, 0);
    private static readonly TimeOnly T16 = new(16, 0);
    private static DateOnly Today => ClinicClock.Today;

    // ---------- create ----------

    private readonly ShiftScheduleRequestValidator _create = new();

    [Fact] public void A_normal_shift_is_valid() => _create.ShouldBeValid(new ShiftScheduleRequest { ShiftName = ShiftGroup.A, StartTime = T8, EndTime = T16 });

    [Fact]
    public void A_night_shift_that_crosses_midnight_is_valid() =>
        _create.ShouldBeValid(new ShiftScheduleRequest { ShiftName = ShiftGroup.C, StartTime = new(22, 0), EndTime = new(6, 0) });

    [Fact]
    public void A_shift_that_starts_and_ends_at_the_same_time_is_refused() =>
        _create.ShouldFail(new ShiftScheduleRequest { ShiftName = ShiftGroup.A, StartTime = T8, EndTime = T8 }, "EndTime", ValidationKeys.EndBeforeStart);

    [Fact]
    public void The_shift_group_and_times_must_be_sent()
    {
        _create.ShouldFail(new ShiftScheduleRequest { ShiftName = 0, StartTime = T8, EndTime = T16 }, "ShiftName", ValidationKeys.InvalidEnum);
        _create.ShouldFail(new ShiftScheduleRequest { ShiftName = (ShiftGroup)4, StartTime = T8, EndTime = T16 }, "ShiftName", ValidationKeys.InvalidEnum);
    }

    /// <summary>TimeOnly cannot be "omitted", so RealTime treats 00:00 as missing: a shift that ends or starts at midnight is refused as "required".</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(16, 0, 0, 0)]     // 16:00 -> 00:00
    [InlineData(0, 0, 8, 0)]      // 00:00 -> 08:00
    public void A_shift_that_touches_midnight_is_valid(int sh, int sm, int eh, int em) =>
        _create.ShouldBeValid(new ShiftScheduleRequest { ShiftName = ShiftGroup.C, StartTime = new(sh, sm), EndTime = new(eh, em) });

    // ---------- update ----------

    private readonly UpdateShiftSchedualRequestDtoValidator _update = new();

    [Fact]
    public void An_update_with_one_field_is_valid_when_times_come_as_a_pair()
    {
        _update.ShouldBeValid(new UpdateShiftSchedualRequestDto { ShiftId = 1, ShiftName = ShiftGroup.B });
        _update.ShouldBeValid(new UpdateShiftSchedualRequestDto { ShiftId = 1, StartTime = T8, EndTime = T16 });
    }

    [Fact]
    public void A_start_without_an_end_or_the_reverse_is_refused()
    {
        _update.ShouldFail(new UpdateShiftSchedualRequestDto { ShiftId = 1, StartTime = T8 }, "EndTime", ValidationKeys.Required);
        _update.ShouldFail(new UpdateShiftSchedualRequestDto { ShiftId = 1, EndTime = T16 }, "StartTime", ValidationKeys.Required);
    }

    [Fact]
    public void An_update_that_changes_nothing_is_refused() =>
        _update.ShouldFail(new UpdateShiftSchedualRequestDto { ShiftId = 1 }, "request", ValidationKeys.AtLeastOneField);

    [Fact]
    public void An_update_checks_the_id_the_group_and_equal_times()
    {
        _update.ShouldFail(new UpdateShiftSchedualRequestDto { ShiftId = 0, ShiftName = ShiftGroup.A }, "ShiftId", ValidationKeys.InvalidId);
        _update.ShouldFail(new UpdateShiftSchedualRequestDto { ShiftId = 1, ShiftName = (ShiftGroup)0 }, "ShiftName", ValidationKeys.InvalidEnum);
        _update.ShouldFail(new UpdateShiftSchedualRequestDto { ShiftId = 1, StartTime = T8, EndTime = T8 }, "EndTime", ValidationKeys.EndBeforeStart);
    }

    // ---------- assign to staff: yesterday .. 12 months ahead ----------

    private readonly AssignShiftToStaffRequestDtoValidator _assign = new();
    private static AssignShiftToStaffRequestDto Assign(DateOnly d) => new() { UserId = 1, ShiftName = ShiftGroup.A, ShiftDate = d };

    [Fact]
    public void A_shift_can_be_assigned_from_yesterday_to_twelve_months_ahead()
    {
        _assign.ShouldBeValid(Assign(Today.AddDays(-1)));
        _assign.ShouldBeValid(Assign(Today));
        _assign.ShouldBeValid(Assign(Today.AddMonths(12)));
    }

    [Fact]
    public void Older_or_further_dates_are_refused()
    {
        _assign.ShouldFail(Assign(Today.AddDays(-2)), "ShiftDate", ValidationKeys.DateInPast);
        _assign.ShouldFail(Assign(Today.AddMonths(12).AddDays(1)), "ShiftDate", ValidationKeys.DateTooFar);
        _assign.ShouldFail(Assign(default), "ShiftDate", ValidationKeys.Required);
    }

    [Fact]
    public void Assigning_needs_a_user_and_a_group()
    {
        _assign.ShouldFail(Assign(Today).With("UserId", 0), "UserId", ValidationKeys.InvalidId);
        _assign.ShouldFail(Assign(Today).With("ShiftName", (ShiftGroup)0), "ShiftName", ValidationKeys.InvalidEnum);
    }

    // ---------- attendance ----------

    private readonly StaffAttendanceCheckInRequestDtoValidator _checkIn = new();

    [Fact]
    public void A_check_in_now_for_the_clinic_day_is_valid()
    {
        var now = DateTime.UtcNow;
        // Use the clinic date that matches the UTC instant, so this test is stable at any hour.
        var clinicDay = DateOnly.FromDateTime(ClinicClock.Now);
        var utcDay = DateOnly.FromDateTime(now);
        if (clinicDay != utcDay) return;     // inside the 2–3 hours where the two dates differ: covered by the KnownBug test below

        _checkIn.ShouldBeValid(new StaffAttendanceCheckInRequestDto { AttendanceDate = clinicDay, CheckTime = now });
    }

    [Fact]
    public void A_check_in_in_the_future_is_refused()
    {
        _checkIn.ShouldFail(new StaffAttendanceCheckInRequestDto { AttendanceDate = Today.AddDays(1), CheckTime = DateTime.UtcNow }, "AttendanceDate", ValidationKeys.DateInFuture);
        _checkIn.ShouldFail(new StaffAttendanceCheckInRequestDto { AttendanceDate = Today, CheckTime = DateTime.UtcNow.AddHours(1) }, "CheckTime", ValidationKeys.DateInFuture);
    }

    [Fact]
    public void Five_minutes_of_clock_drift_are_tolerated()
    {
        var now = DateTime.UtcNow;
        var date = DateOnly.FromDateTime(now);
        if (date != DateOnly.FromDateTime(now.AddMinutes(4)) || date != DateOnly.FromDateTime(ClinicClock.Now)) return;   // avoid the midnight edge

        _checkIn.ShouldNotFailOn(new StaffAttendanceCheckInRequestDto { AttendanceDate = date, CheckTime = now.AddMinutes(4) }, "CheckTime");
    }

    [Fact]
    public void The_time_must_belong_to_the_attendance_date()
    {
        var req = new StaffAttendanceCheckInRequestDto { AttendanceDate = new DateOnly(2026, 1, 10), CheckTime = new DateTime(2026, 1, 13, 9, 0, 0, DateTimeKind.Utc) };

        _checkIn.ShouldFail(req, "CheckTime", ValidationKeys.InvalidDate);
    }

    [Fact]
    public void An_omitted_date_or_time_is_refused()
    {
        _checkIn.ShouldFail(new StaffAttendanceCheckInRequestDto { AttendanceDate = default, CheckTime = DateTime.UtcNow }, "AttendanceDate", ValidationKeys.Required);
        _checkIn.ShouldFail(new StaffAttendanceCheckInRequestDto { AttendanceDate = Today, CheckTime = default }, "CheckTime", ValidationKeys.Required);
    }

    /// <summary>
    /// The API sends CheckTime as a UTC instant (the attendance tests do too) but AttendanceDate is a clinic date.
    /// The validator compares the UTC date with the clinic date, so between 00:00 and 02:00/03:00 clinic time a
    /// perfectly honest check-in is refused with InvalidDate.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void A_check_in_just_after_clinic_midnight_is_accepted()
    {
        // 00:30 in Hebron on 15 January (UTC+2) is 22:30 UTC on 14 January.
        var req = new StaffAttendanceCheckInRequestDto
        {
            AttendanceDate = new DateOnly(2026, 1, 15),
            CheckTime = new DateTime(2026, 1, 14, 22, 30, 0, DateTimeKind.Utc)
        };

        _checkIn.ShouldNotFailOn(req, "CheckTime");
    }

    [Fact]
    public void Marking_absent_needs_a_user_and_a_date()
    {
        var v = new StaffAbsentRequestDtoValidator();

        v.ShouldBeValid(new StaffAbsentRequestDto { userId = 1, AttendanceDate = Today });
        v.ShouldFail(new StaffAbsentRequestDto { userId = 0, AttendanceDate = Today }, "userId", ValidationKeys.InvalidId);
        v.ShouldFail(new StaffAbsentRequestDto { userId = 1, AttendanceDate = default }, "AttendanceDate", ValidationKeys.Required);
    }

    [Fact]
    public void Marking_leave_needs_a_date()
    {
        var v = new StaffOnLeaveRequestDtoValidator();

        v.ShouldBeValid(new StaffOnLeaveRequestDto { AttendanceDate = Today });
        v.ShouldFail(new StaffOnLeaveRequestDto { AttendanceDate = default }, "AttendanceDate", ValidationKeys.Required);
    }
}

public class DoctorDailyHoursValidatorTests
{
    private static AddDoctorDailyHoursRequest Add() => new() { DayOfWeek = DayOfWeek.Sunday, StartTime = new(9, 0), EndTime = new(17, 0), SlotDurationMinutes = 30 };
    private static UpdateDoctorDailyHousrRequest Upd() => new() { schedualId = 1, DayOfWeek = DayOfWeek.Monday, StartTime = new(9, 0), EndTime = new(17, 0), SlotDurationMinutes = 30 };

    private readonly AddDoctorDailyHoursRequestValidator _add = new();
    private readonly UpdateDoctorDailyHousrRequestValidator _update = new();

    [Fact]
    public void A_normal_day_is_valid()
    {
        _add.ShouldBeValid(Add());
        _update.ShouldBeValid(Upd());
    }

    [Theory]
    [InlineData(0)]    // Sunday is a real working day here
    [InlineData(6)]
    public void Every_day_of_the_week_is_a_valid_choice(int day) => _add.ShouldBeValid(Add().With("DayOfWeek", (DayOfWeek)day));

    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public void A_day_outside_the_week_is_refused(int day) =>
        _add.ShouldFail(Add().With("DayOfWeek", (DayOfWeek)day), "DayOfWeek", ValidationKeys.InvalidEnum);

    [Theory]
    [InlineData(5)]
    [InlineData(480)]
    public void A_slot_of_5_to_480_minutes_is_valid(int minutes) =>
        _add.ShouldBeValid(Add().With("StartTime", new TimeOnly(1, 0)).With("EndTime", new TimeOnly(23, 0)).With("SlotDurationMinutes", minutes));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(481)]
    [InlineData(-30)]
    public void Any_other_slot_length_is_refused(int minutes) =>
        _add.ShouldFail(Add().With("SlotDurationMinutes", minutes), "SlotDurationMinutes", ValidationKeys.InvalidSlotDuration);

    [Fact]
    public void The_end_must_be_after_the_start()
    {
        _add.ShouldFail(Add().With("EndTime", new TimeOnly(9, 0)), "EndTime", ValidationKeys.EndBeforeStart);
        _add.ShouldFail(Add().With("EndTime", new TimeOnly(8, 0)), "EndTime", ValidationKeys.EndBeforeStart);
        _add.ShouldFail(Add().With("StartTime", default(TimeOnly)), "StartTime", ValidationKeys.Required);
    }

    [Fact]
    public void A_day_shorter_than_one_slot_is_refused()
    {
        var shortDay = Add().With("EndTime", new TimeOnly(9, 20));           // 20 min with a 30 min slot

        _add.ShouldFail(shortDay, "SlotDurationMinutes", ValidationKeys.ShiftTooShort);
        _add.ShouldBeValid(Add().With("EndTime", new TimeOnly(9, 30)));       // exactly one slot
    }

    [Fact]
    public void An_update_follows_the_same_rules_and_needs_an_id()
    {
        _update.ShouldFail(Upd().With("schedualId", 0), "schedualId", ValidationKeys.InvalidId);
        _update.ShouldFail(Upd().With("SlotDurationMinutes", 4), "SlotDurationMinutes", ValidationKeys.InvalidSlotDuration);
        _update.ShouldFail(Upd().With("EndTime", new TimeOnly(9, 0)), "EndTime", ValidationKeys.EndBeforeStart);
        _update.ShouldFail(Upd().With("EndTime", new TimeOnly(9, 20)), "SlotDurationMinutes", ValidationKeys.ShiftTooShort);
        _update.ShouldFail(Upd().With("DayOfWeek", (DayOfWeek)9), "DayOfWeek", ValidationKeys.InvalidEnum);
    }
}
