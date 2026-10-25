using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Appointments;

/// <summary>GetDoctorAsync (doctor card) and GetDoctorsAsync (departments with their doctors).</summary>
[Collection(SqlServerCollection.Name)]
public class DoctorDirectoryTests(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static readonly PagedRequest Page = new() { PageNumber = 1, PageSize = 10 };

    private async Task AddRatingAsync(int doctorId, int rating, string? review)
    {
        var apptId = await C.AddAppointmentAsync(doctorId, Pat.PatientId, ClinicClock.Today.AddDays(-10), null, AppointmentStatus.Completed);
        await using var db = Db();
        db.DoctorRatings.Add(new DoctorRating
        {
            DoctorId = doctorId, PatientId = Pat.PatientId, AppointmentId = apptId,
            Rating = rating, Review = review, UpdateAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> AddDepartmentAsync(string name)
    {
        await using var db = Db();
        var d = new Department { Name = name };
        db.Departments.Add(d);
        await db.SaveChangesAsync();
        return d.Id;
    }

    private async Task MoveDoctorAsync(int doctorId, int departmentId)
    {
        await using var db = Db();
        (await db.Doctors.SingleAsync(d => d.Id == doctorId)).departmentId = departmentId;
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ GetDoctorAsync

    [Fact]
    public async Task Doctor_card_has_name_bio_specialization_and_no_rating_yet()
    {
        var res = await C.NewAppointmentService().GetDoctorAsync(C.Id, Doc.DoctorId);

        await using var db = Db();
        var user = await db.Users.SingleAsync(u => u.Id == Doc.UserId);
        res.DoctorId.ShouldBe(Doc.DoctorId);
        res.DoctorName.ShouldBe($"{user.firstName} {user.lastName}");
        res.Bio.ShouldBe("bio");
        res.Specialization.ShouldBe("GP");
        res.AvrageRating.ShouldBe(0);
        res.Reviews.ShouldBeEmpty();
    }

    [Fact]
    public async Task Doctor_card_averages_the_ratings_and_lists_the_reviews()
    {
        await AddRatingAsync(Doc.DoctorId, 4, "good");
        await AddRatingAsync(Doc.DoctorId, 5, "excellent");

        var res = await C.NewAppointmentService().GetDoctorAsync(C.Id, Doc.DoctorId);

        res.AvrageRating.ShouldBe(4.5);
        res.Reviews.ShouldBe(new string?[] { "good", "excellent" }, ignoreOrder: true);
    }

    [Fact]
    public async Task Ratings_of_other_doctors_are_not_mixed_in()
    {
        var doc2 = await C.AddDoctorAsync();
        await AddRatingAsync(Doc.DoctorId, 2, "slow");
        await AddRatingAsync(doc2.DoctorId, 5, "great");

        (await C.NewAppointmentService().GetDoctorAsync(C.Id, Doc.DoctorId)).AvrageRating.ShouldBe(2);
    }

    [Fact]
    public Task Doctor_card_Doctor_NotFound_for_an_unknown_doctor() =>
        ShouldFailWith("Doctor.NotFound", () => C.NewAppointmentService().GetDoctorAsync(C.Id, 999_999));

    [Fact]
    public async Task Doctor_card_Doctor_NotFound_for_an_inactive_doctor()
    {
        var inactive = await C.AddDoctorAsync(active: false);
        await ShouldFailWith("Doctor.NotFound", () => C.NewAppointmentService().GetDoctorAsync(C.Id, inactive.DoctorId));
    }

    [Fact]
    public Task Doctor_card_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => C.NewAppointmentService().GetDoctorAsync(999_999, Doc.DoctorId));

    // ------------------------------------------------------------------ GetDoctorsAsync

    [Fact]
    public async Task Departments_are_sorted_by_name_and_empty_ones_are_kept()
    {
        var cardio = await AddDepartmentAsync("Cardiology");
        await AddDepartmentAsync("Empty");
        var cardiologist = await C.AddDoctorAsync();
        await MoveDoctorAsync(cardiologist.DoctorId, cardio);

        var res = await C.NewAppointmentService().GetDoctorsAsync(C.Id, Page);

        res.Items.Select(i => i.DepartmentName).ShouldBe(new[] { "Cardiology", "Empty", "General" });
        res.Items.Single(i => i.DepartmentName == "Cardiology").Doctors.Single().DoctorId.ShouldBe(cardiologist.DoctorId);
        res.Items.Single(i => i.DepartmentName == "Empty").Doctors.ShouldBeEmpty();
        res.Items.Single(i => i.DepartmentName == "General").Doctors.Single().DoctorId.ShouldBe(Doc.DoctorId);
    }

    [Fact]
    public async Task Inactive_doctors_are_not_listed()
    {
        await C.AddDoctorAsync(active: false);

        var res = await C.NewAppointmentService().GetDoctorsAsync(C.Id, Page);

        res.Items.Single(i => i.DepartmentName == "General").Doctors.Select(d => d.DoctorId).ShouldBe(new[] { Doc.DoctorId });
    }

    [Fact]
    public async Task Listed_doctors_carry_their_average_rating()
    {
        await AddRatingAsync(Doc.DoctorId, 3, null);
        await AddRatingAsync(Doc.DoctorId, 4, null);

        var res = await C.NewAppointmentService().GetDoctorsAsync(C.Id, Page);

        var card = res.Items.Single(i => i.DepartmentName == "General").Doctors.Single();
        card.AvrageRating.ShouldBe(3.5);
        card.isActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Departments_are_paged()
    {
        await AddDepartmentAsync("Cardiology");
        await AddDepartmentAsync("Dermatology");

        var res = await C.NewAppointmentService().GetDoctorsAsync(C.Id, new PagedRequest { PageNumber = 2, PageSize = 2 });

        res.TotalCount.ShouldBe(3);
        res.Items.Select(i => i.DepartmentName).ShouldBe(new[] { "General" });
    }

    [Fact]
    public Task Doctors_list_Center_NotFound() =>
        ShouldFailWith("Center.NotFound", () => C.NewAppointmentService().GetDoctorsAsync(999_999, Page));
}
