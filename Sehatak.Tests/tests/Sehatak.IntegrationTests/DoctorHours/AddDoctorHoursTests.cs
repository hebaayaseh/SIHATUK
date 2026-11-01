using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.DoctorHours;

/// <summary>Admin adds a weekly working window for a doctor.</summary>
[Collection(SqlServerCollection.Name)]
public class AddDoctorHoursTests(SqlServerFixture fx) : DoctorHoursTestBase(fx)
{
    private Task<Sehatak.Application.DTOs.AddDoctorDailyHour.AddDoctorDailyHoursResponse> Add(
        DayOfWeek day, TimeOnly start, TimeOnly end, int slot = 30, int? doctorId = null, int? userId = null) =>
        NewHoursService().AddDoctorDailyHoursAsync(C.Id, userId ?? Admin, doctorId ?? Dr.DoctorId, AddReq(day, start, end, slot));

    [Fact]
    public async Task Adds_an_active_schedule_and_audits_it()
    {
        var r = await Add(DayOfWeek.Tuesday, T09, T11, 20);

        r.DoctorId.ShouldBe(Dr.DoctorId);
        r.DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
        r.StartTime.ShouldBe(T09);
        r.EndTime.ShouldBe(T11);
        r.SlotDurationMinutes.ShouldBe(20);

        var s = (await SchedulesOfAsync(Dr.DoctorId)).Single();
        s.IsActive.ShouldBeTrue();
        s.DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
        s.SlotDurationMinutes.ShouldBe(20);
        (await CountAuditAsync("AddDoctorSchedule", s.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_doctor_can_work_on_different_days()
    {
        await Add(DayOfWeek.Monday, T09, T11);
        await Add(DayOfWeek.Tuesday, T09, T11);
        (await SchedulesOfAsync(Dr.DoctorId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Two_windows_on_the_same_day_are_fine_when_they_only_touch()
    {
        await Add(DayOfWeek.Monday, T09, T10);
        await Add(DayOfWeek.Monday, T10, T11);        // starts exactly when the first ends
        (await SchedulesOfAsync(Dr.DoctorId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Another_doctor_can_have_the_same_window()
    {
        var other = await C.AddDoctorAsync();
        await Add(DayOfWeek.Monday, T09, T11);
        await Add(DayOfWeek.Monday, T09, T11, doctorId: other.DoctorId);
        (await SchedulesOfAsync(other.DoctorId)).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(8, 10)]    // overlaps the start of the existing 09-11
    [InlineData(10, 12)]   // overlaps the end
    [InlineData(9, 11)]    // identical
    [InlineData(8, 12)]    // contains it
    [InlineData(10, 11)]   // inside it
    public async Task Schedule_Conflict_when_the_windows_overlap(int fromHour, int toHour)
    {
        await Add(DayOfWeek.Monday, T09, T11);
        await ShouldFailWith("Schedule.Conflict", () => Add(DayOfWeek.Monday, new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0)));
        (await SchedulesOfAsync(Dr.DoctorId)).Count.ShouldBe(1);
    }

    /// <summary>When a schedule is replaced by Update the old row stays with IsActive = false. Add still counts it as a conflict, so that window can never be used again (Update ignores inactive rows, Add does not).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_deactivated_schedule_does_not_block_a_new_one()
    {
        await Add(DayOfWeek.Monday, T09, T11);
        var oldId = (await SchedulesOfAsync(Dr.DoctorId)).Single().Id;
        await NewHoursService().UpdateDoctorDailyHoursAsync(C.Id, Admin, Dr.DoctorId, UpdReq(oldId, DayOfWeek.Tuesday, T09, T11));

        // Monday 09-11 is now inactive: the doctor no longer works Mondays, so adding Monday again must be allowed.
        var r = await Add(DayOfWeek.Monday, T09, T11);

        r.DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Fact]
    public async Task Adding_a_schedule_refreshes_the_cached_list()
    {
        await Add(DayOfWeek.Monday, T09, T11);
        var svc = NewHoursService();
        (await svc.GetDoctorDailyHoursAsync(C.Id, Dr.DoctorId, new PagedRequest { PageNumber = 1, PageSize = 10 })).Items.Count.ShouldBe(1);

        await Add(DayOfWeek.Wednesday, T09, T11);

        (await svc.GetDoctorDailyHoursAsync(C.Id, Dr.DoctorId, new PagedRequest { PageNumber = 1, PageSize = 10 })).Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Auth_Forbidden_for_a_receptionist()
    {
        var rec = await C.AddReceptionistAsync();
        await ShouldFailWith("Auth.Forbidden", () => Add(DayOfWeek.Monday, T09, T11, userId: rec));
        (await SchedulesOfAsync(Dr.DoctorId)).ShouldBeEmpty();
    }

    [Fact]
    public Task Auth_Forbidden_for_an_unknown_user() =>
        ShouldFailWith("Auth.Forbidden", () => Add(DayOfWeek.Monday, T09, T11, userId: 999_999));

    [Fact]
    public Task Doctor_NotFound_for_an_unknown_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => Add(DayOfWeek.Monday, T09, T11, doctorId: 999_999));

    [Fact]
    public async Task Doctor_NotFound_for_an_inactive_doctor()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => Add(DayOfWeek.Monday, T09, T11, doctorId: inactive.DoctorId));
    }

    [Fact]
    public Task Center_NotFound() =>
        ShouldFailWith("Center.NotFound",
            () => NewHoursService().AddDoctorDailyHoursAsync(999_999, Admin, Dr.DoctorId, AddReq(DayOfWeek.Monday, T09, T11)));
}
