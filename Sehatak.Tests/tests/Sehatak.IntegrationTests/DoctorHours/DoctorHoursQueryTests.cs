using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.DoctorHours;

/// <summary>The read side: schedule list (admin), today's appointments (doctor), blocked days (admin / doctor).</summary>
[Collection(SqlServerCollection.Name)]
public class DoctorHoursQueryTests(SqlServerFixture fx) : DoctorHoursTestBase(fx)
{
    private static PagedRequest Page(int n = 1, int size = 10) => new() { PageNumber = n, PageSize = size };

    // ------------------------------------------------------------------ schedule list

    [Fact]
    public async Task Schedule_list_returns_the_doctors_rows_with_their_flags()
    {
        var other = await C.AddDoctorAsync();
        await C.AddScheduleAsync(other.DoctorId, DayOfWeek.Friday, T09, T11, 30);
        var active = await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Monday, T09, T11, 20);
        var inactive = await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Tuesday, T10, T12, 15, isActive: false);

        var res = await NewHoursService().GetDoctorDailyHoursAsync(C.Id, Dr.DoctorId, Page());

        res.TotalCount.ShouldBe(2);
        var a = res.Items.Single(i => i.Id == active);
        a.DoctorId.ShouldBe(Dr.DoctorId);
        a.DayOfWeek.ShouldBe(DayOfWeek.Monday);
        a.StartTime.ShouldBe(T09);
        a.EndTime.ShouldBe(T11);
        a.SlotDurationMinutes.ShouldBe(20);
        a.IsActive.ShouldBeTrue();
        res.Items.Single(i => i.Id == inactive).IsActive.ShouldBeFalse();
    }

    /// <summary>DayOfWeek is stored as text, so ORDER BY sorts by the day's NAME: Friday, Monday, Saturday, Sunday, Thursday, Tuesday, Wednesday.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Schedule_list_is_in_week_order()
    {
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Wednesday, T09, T11, 30);
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Monday, T09, T11, 30);
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Sunday, T09, T11, 30);
        await C.AddScheduleAsync(Dr.DoctorId, DayOfWeek.Friday, T09, T11, 30);

        var res = await NewHoursService().GetDoctorDailyHoursAsync(C.Id, Dr.DoctorId, Page());

        res.Items.Select(i => i.DayOfWeek).ToList()
            .ShouldBe([DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday]);
    }

    [Fact]
    public async Task Schedule_list_is_paged()
    {
        foreach (var d in new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday })
            await C.AddScheduleAsync(Dr.DoctorId, d, T09, T11, 30);

        var p2 = await NewHoursService().GetDoctorDailyHoursAsync(C.Id, Dr.DoctorId, Page(2, 2));

        p2.TotalCount.ShouldBe(3);
        p2.Items.Count.ShouldBe(1);
    }

    /// <summary>A new doctor has no schedule yet and the admin's screen for him gets an error instead of an empty list.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Schedule_list_is_empty_for_a_doctor_without_a_schedule()
    {
        var res = await NewHoursService().GetDoctorDailyHoursAsync(C.Id, Dr.DoctorId, Page());
        res.Items.ShouldBeEmpty();
        res.TotalCount.ShouldBe(0);
    }

    [Fact]
    public Task Schedule_list_Doctor_NotFound_for_an_unknown_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => NewHoursService().GetDoctorDailyHoursAsync(C.Id, 999_999, Page()));

    [Fact]
    public Task Schedule_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewHoursService().GetDoctorDailyHoursAsync(999_999, Dr.DoctorId, Page()));

    // ------------------------------------------------------------------ doctor's appointments for a day

    [Fact]
    public async Task Day_list_defaults_to_today_and_is_ordered_by_time()
    {
        var today = ClinicClock.Today;
        var late = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, today, S1000);
        var early = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, today, S0900);
        await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, today.AddDays(1), S0900);

        var res = await NewHoursService().GetDoctorAppointmentsForDayAsync(C.Id, Dr.UserId, null);

        res.appointments.Select(a => a.appointmentId).ToList().ShouldBe([early, late]);
        res.appointments[0].patientId.ShouldBe(Pat.PatientId);
        res.appointments[0].patientName.ShouldNotBeNullOrWhiteSpace();
        res.appointments[0].date.ShouldBe(today);
        res.appointments[0].timeSlot.ShouldBe(S0900);
        res.appointments[0].IsfollowUp.ShouldBeFalse();
    }

    [Fact]
    public async Task Day_list_shows_only_confirmed_appointments_of_this_doctor()
    {
        var day = ClinicClock.Today.AddDays(2);
        var other = await C.AddDoctorAsync();
        var mine = await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, day, S0900);
        await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, day, S0930, AppointmentStatus.Cancelled);
        await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, day, S1000, AppointmentStatus.Pending);
        await C.AddAppointmentAsync(Dr.DoctorId, Pat.PatientId, day, null);                    // no slot
        await C.AddAppointmentAsync(other.DoctorId, Pat.PatientId, day, S0900);

        var res = await NewHoursService().GetDoctorAppointmentsForDayAsync(C.Id, Dr.UserId, day);

        res.appointments.Select(a => a.appointmentId).ShouldBe([mine]);
    }

    [Fact]
    public async Task Day_list_marks_follow_up_appointments_and_uses_the_childs_name()
    {
        var day = ClinicClock.Today.AddDays(2);
        var child = await C.AddSubPatientAsync(Pat);
        await using (var db = Db())
        {
            db.Appointments.Add(new Sehatak.Domain.Entities.TenantEntities.Appointment
            {
                doctorId = Dr.DoctorId, patientId = child.PatientId, appointmentDate = day, timeSlot = S0900,
                appointmentStatus = AppointmentStatus.Confirmed, IsFollowUp = true
            });
            await db.SaveChangesAsync();
        }

        var item = (await NewHoursService().GetDoctorAppointmentsForDayAsync(C.Id, Dr.UserId, day)).appointments.Single();

        item.IsfollowUp.ShouldBeTrue();
        item.patientId.ShouldBe(child.PatientId);
        item.patientName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Day_list_Date_Invalid_for_a_past_date()
    {
        await ShouldFailWith("Date.Invalid",
            () => NewHoursService().GetDoctorAppointmentsForDayAsync(C.Id, Dr.UserId, ClinicClock.Today.AddDays(-1)));
    }

    [Fact]
    public Task Day_list_Doctor_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => NewHoursService().GetDoctorAppointmentsForDayAsync(C.Id, Admin, null));

    [Fact]
    public Task Day_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewHoursService().GetDoctorAppointmentsForDayAsync(999_999, Dr.UserId, null));

    // ------------------------------------------------------------------ blocked days: admin view

    [Fact]
    public async Task Admin_sees_all_doctors_blocked_on_a_date_newest_first()
    {
        var other = await C.AddDoctorAsync();
        await SetBlockedAsync(Dr.DoctorId, MonDate, null, DateTime.UtcNow.AddHours(-3));
        await SetBlockedAsync(other.DoctorId, MonDate, S0900, DateTime.UtcNow.AddHours(-1));
        await SetBlockedAsync(Dr.DoctorId, MonDate.AddDays(7), null);

        var res = await NewHoursService().GetDoctorsBlockDayAsync(C.Id, Admin, MonDate, Page());

        res.TotalCount.ShouldBe(2);
        res.Items[0].DoctorId.ShouldBe(other.DoctorId);
        res.Items[0].TimeSlot.ShouldBe(S0900);
        res.Items[0].date.ShouldBe(MonDate);
        res.Items[0].DoctorName.ShouldNotBeNullOrWhiteSpace();
        res.Items[1].DoctorId.ShouldBe(Dr.DoctorId);
        res.Items[1].TimeSlot.ShouldBeNull();
    }

    [Fact]
    public async Task Admin_block_list_is_empty_for_a_free_date()
    {
        var res = await NewHoursService().GetDoctorsBlockDayAsync(C.Id, Admin, MonDate, Page());
        res.Items.ShouldBeEmpty();
    }

    [Fact]
    public Task Admin_block_list_User_NotFound_for_an_unknown_user() =>
        ShouldFailWith("User.NotFound", () => NewHoursService().GetDoctorsBlockDayAsync(C.Id, 999_999, MonDate, Page()));

    [Fact]
    public async Task Admin_block_list_User_NotFound_for_an_inactive_user()
    {
        var inactive = await C.AddReceptionistAsync(active: false);
        await ShouldFailWith("User.NotFound", () => NewHoursService().GetDoctorsBlockDayAsync(C.Id, inactive, MonDate, Page()));
    }

    [Fact]
    public Task Admin_block_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewHoursService().GetDoctorsBlockDayAsync(999_999, Admin, MonDate, Page()));

    // ------------------------------------------------------------------ blocked days: doctor view

    [Fact]
    public async Task Doctor_sees_only_his_own_blocked_entries_for_the_date()
    {
        var other = await C.AddDoctorAsync();
        await SetBlockedAsync(Dr.DoctorId, MonDate, null, DateTime.UtcNow.AddHours(-2));
        await SetBlockedAsync(Dr.DoctorId, MonDate, S1000, DateTime.UtcNow.AddHours(-1));
        await SetBlockedAsync(other.DoctorId, MonDate, null);
        await SetBlockedAsync(Dr.DoctorId, MonDate.AddDays(7), null);

        var res = await NewHoursService().DoctorGetBlokDays(C.Id, Dr.UserId, MonDate, Page());

        res.TotalCount.ShouldBe(2);
        res.Items.ShouldAllBe(i => i.DoctorId == Dr.DoctorId && i.date == MonDate);
        res.Items[0].TimeSlot.ShouldBe(S1000);          // newest first
        res.Items[0].DoctorName.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public Task Doctor_block_list_User_NotFound_when_the_caller_is_not_a_doctor() =>
        ShouldFailWith("User.NotFound", () => NewHoursService().DoctorGetBlokDays(C.Id, Admin, MonDate, Page()));

    [Fact]
    public Task Doctor_block_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => NewHoursService().DoctorGetBlokDays(999_999, Dr.UserId, MonDate, Page()));
}
