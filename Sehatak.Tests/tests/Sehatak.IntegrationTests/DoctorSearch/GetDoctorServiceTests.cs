using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.DoctorSearch;

/// <summary>The admin's doctor card (with blocked days) and the department/doctor list.</summary>
[Collection(SqlServerCollection.Name)]
public class GetDoctorServiceTests(SqlServerFixture fx) : DoctorSearchTestBase(fx)
{
    private static readonly PagedRequest Page = new() { PageNumber = 1, PageSize = 20 };

    private DateOnly FirstOfMonth => new(Date.Year, Date.Month, 1);
    private DateOnly LastOfMonth => FirstOfMonth.AddMonths(1).AddDays(-1);

    private async Task AddRatingAsync(int doctorId, int rating, string? review)
    {
        var appt = await C.AddAppointmentAsync(doctorId, Pat.PatientId, ClinicClock.Today.AddDays(-10), null, AppointmentStatus.Completed);
        await using var db = Db();
        db.DoctorRatings.Add(new DoctorRating
        {
            DoctorId = doctorId, PatientId = Pat.PatientId, AppointmentId = appt,
            Rating = rating, Review = review, UpdateAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    // ---------- the card ----------

    [Fact]
    public async Task The_card_has_contact_details_and_professional_info()
    {
        var d = await AddNamedDoctorAsync("Zuhair", "Haddad", spec: "Cardiology");
        await using (var db = Db())
        {
            var u = await db.Users.SingleAsync(x => x.Id == d.UserId);
            u.ProfileImageUrl = "/uploads/profileImage/z.png";
            await db.SaveChangesAsync();
        }

        var r = await NewStaff().GetDoctorAsync(C.Id, d.DoctorId, Date.Year, Date.Month);

        r!.DoctorId.ShouldBe(d.DoctorId);
        r.DoctorName.ShouldBe("Zuhair Haddad");
        r.Specialization.ShouldBe("Cardiology");
        r.Bio.ShouldBe("bio");
        r.OnlineEnabled.ShouldBeFalse();
        r.ProfileImageUrl.ShouldBe("/uploads/profileImage/z.png");
        r.email.ShouldNotBeNullOrWhiteSpace();
        r.phoneNumber.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task The_card_lists_only_active_working_hours_with_their_day()
    {
        var d = await AddNamedDoctorAsync("Zuhair", "Sched");     // the default Doc already has Monday hours
        await C.AddScheduleAsync(d.DoctorId, DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(13, 0), slotMinutes: 20);
        await C.AddScheduleAsync(d.DoctorId, DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(13, 0), isActive: false);

        var r = await NewStaff().GetDoctorAsync(C.Id, d.DoctorId, Date.Year, Date.Month);

        var s = r!.doctorSchedule.ShouldHaveSingleItem();
        s.DayOfWeek.ShouldBe(DayOfWeek.Tuesday);
        s.StartTime.ShouldBe(new TimeOnly(9, 0));
        s.EndTime.ShouldBe(new TimeOnly(13, 0));
        s.SlotDurationMinutes.ShouldBe(20);
    }

    [Fact]
    public async Task The_card_fails_for_an_unknown_deactivated_doctor_or_unavailable_center()
    {
        var gone = await C.AddDoctorAsync(active: false);

        await ShouldFailWith("Doctor.NotFound", () => NewStaff().GetDoctorAsync(C.Id, 999_999));
        await ShouldFailWith("Doctor.NotFound", () => NewStaff().GetDoctorAsync(C.Id, gone.DoctorId));
        await ShouldFailWith("Center.NotFound", () => NewStaff().GetDoctorAsync(999_999, Doc.DoctorId));
    }

    // ---------- blocked days ----------

    [Fact]
    public async Task Blocked_days_are_those_of_the_asked_month_including_its_first_and_last_day()
    {
        await AddBlockAsync(Doc.DoctorId, FirstOfMonth, null);
        await AddBlockAsync(Doc.DoctorId, LastOfMonth, null);
        await AddBlockAsync(Doc.DoctorId, FirstOfMonth.AddDays(-1), null);   // previous month
        await AddBlockAsync(Doc.DoctorId, LastOfMonth.AddDays(1), null);     // next month

        var r = await NewStaff().GetDoctorAsync(C.Id, Doc.DoctorId, Date.Year, Date.Month);

        r!.BlockedDates.OrderBy(x => x).ShouldBe(new DateOnly?[] { FirstOfMonth, LastOfMonth });
    }

    [Fact]
    public async Task A_day_that_was_unblocked_is_not_listed()
    {
        await AddBlockAsync(Doc.DoctorId, FirstOfMonth.AddDays(3), null, isBlocked: false);

        var r = await NewStaff().GetDoctorAsync(C.Id, Doc.DoctorId, Date.Year, Date.Month);

        r!.BlockedDates.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_doctors_blocked_days_are_not_mixed_in()
    {
        var other = await C.AddDoctorAsync();
        await AddBlockAsync(other.DoctorId, FirstOfMonth.AddDays(3), null);

        var r = await NewStaff().GetDoctorAsync(C.Id, Doc.DoctorId, Date.Year, Date.Month);

        r!.BlockedDates.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Blocking_one_time_slot_does_not_show_the_whole_day_as_blocked()
    {
        // a block with a time slot only closes that slot; the calendar must not grey out the day
        await AddBlockAsync(Doc.DoctorId, FirstOfMonth.AddDays(3), new TimeOnly(9, 0));

        var r = await NewStaff().GetDoctorAsync(C.Id, Doc.DoctorId, Date.Year, Date.Month);

        r!.BlockedDates.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_day_with_several_blocks_is_listed_once()
    {
        await AddBlockAsync(Doc.DoctorId, FirstOfMonth.AddDays(3), null);
        await AddBlockAsync(Doc.DoctorId, FirstOfMonth.AddDays(3), new TimeOnly(9, 0));

        var r = await NewStaff().GetDoctorAsync(C.Id, Doc.DoctorId, Date.Year, Date.Month);

        r!.BlockedDates.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(2026, 13, "Validation.InvalidMonth")]
    [InlineData(2026, 0, "Validation.InvalidMonth")]
    [InlineData(0, 5, "Validation.InvalidYear")]
    [InlineData(10000, 5, "Validation.InvalidYear")]
    [Trait("Category", "KnownBug")]
    public async Task A_month_or_year_that_does_not_exist_is_a_clean_error(int year, int month, string code)
    {
        // new DateOnly(year, month, 1) throws ArgumentOutOfRangeException -> 500
        await ShouldFailWith(code, () => NewStaff().GetDoctorAsync(C.Id, Doc.DoctorId, year, month));
    }

    // ---------- departments with doctors ----------

    [Fact]
    public async Task Departments_are_sorted_by_name_and_empty_ones_are_kept()
    {
        var b = await AddDepartmentAsync("Bbb-Dept");
        var a = await AddDepartmentAsync("Aaa-Dept");

        var r = await NewStaff().GetDoctorsAsync(C.Id, Page);

        var names = r.Items.Select(i => i.DepartmentName).ToList();
        names.ShouldBe(names.OrderBy(n => n, StringComparer.Ordinal).ToList());
        r.Items.Single(i => i.DepartmentId == a).Doctors.ShouldBeEmpty();
        r.Items.Single(i => i.DepartmentId == b).Doctors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Each_doctor_is_listed_under_their_department_with_the_active_flag()
    {
        var dept = await AddDepartmentAsync("Cardiology");
        var active = await AddNamedDoctorAsync("Zuhair", "A", departmentId: dept);
        var gone = await AddNamedDoctorAsync("Zuhair", "B", active: false, departmentId: dept);

        var d = (await NewStaff().GetDoctorsAsync(C.Id, Page)).Items.Single(i => i.DepartmentId == dept);

        d.Doctors.Count.ShouldBe(2);                                                  // the admin also sees deactivated doctors
        d.Doctors.Single(x => x.DoctorId == active.DoctorId).isActive.ShouldBeTrue();
        d.Doctors.Single(x => x.DoctorId == gone.DoctorId).isActive.ShouldBeFalse();
        d.Doctors.Single(x => x.DoctorId == active.DoctorId).DoctorName.ShouldBe("Zuhair A");
    }

    [Fact]
    public async Task A_doctor_with_ratings_shows_the_average_and_the_reviews()
    {
        await AddRatingAsync(Doc.DoctorId, 5, "great");
        await AddRatingAsync(Doc.DoctorId, 3, "ok");

        var doctor = (await NewStaff().GetDoctorsAsync(C.Id, Page)).Items
            .SelectMany(i => i.Doctors).Single(x => x.DoctorId == Doc.DoctorId);

        doctor.AvrageRating.ShouldBe(4.0);
        doctor.Reviews.ShouldBe(new string?[] { "great", "ok" }, ignoreOrder: true);
    }

    [Fact]
    public async Task A_doctor_without_ratings_has_zero_and_no_reviews()
    {
        var doctor = (await NewStaff().GetDoctorsAsync(C.Id, Page)).Items
            .SelectMany(i => i.Doctors).Single(x => x.DoctorId == Doc.DoctorId);

        doctor.AvrageRating.ShouldBe(0);
        doctor.Reviews.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ratings_without_text_do_not_put_empty_entries_in_the_review_list()
    {
        await AddRatingAsync(Doc.DoctorId, 5, null);

        var doctor = (await NewStaff().GetDoctorsAsync(C.Id, Page)).Items
            .SelectMany(i => i.Doctors).Single(x => x.DoctorId == Doc.DoctorId);

        doctor.AvrageRating.ShouldBe(5);
        doctor.Reviews.ShouldNotContain((string?)null);
    }

    [Fact]
    public async Task Departments_are_paged()
    {
        for (var i = 1; i <= 4; i++) await AddDepartmentAsync($"Zz-Dept-{i}");

        var p1 = await NewStaff().GetDoctorsAsync(C.Id, new PagedRequest { PageNumber = 1, PageSize = 2 });
        var p2 = await NewStaff().GetDoctorsAsync(C.Id, new PagedRequest { PageNumber = 2, PageSize = 2 });

        p1.Items.Count.ShouldBe(2);
        p2.Items.Count.ShouldBe(2);
        p1.TotalCount.ShouldBe(5);   // 4 new + "General"
        p1.Items.Concat(p2.Items).Select(i => i.DepartmentId).Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task The_department_list_fails_for_an_unavailable_center()
    {
        await ShouldFailWith("Center.NotFound", () => NewStaff().GetDoctorsAsync(999_999, Page));
    }
}
