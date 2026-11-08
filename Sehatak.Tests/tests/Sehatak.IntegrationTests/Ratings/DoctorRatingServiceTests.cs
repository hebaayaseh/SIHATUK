using Sehatak.Application.Common;
using Sehatak.Application.DTOs.DoctorRatingDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Ratings;

[Collection(SqlServerCollection.Name)]
public class DoctorRatingServiceTests(SqlServerFixture fx) : RatingsTestBase(fx)
{
    private static AddDoctorRatingRequest Add(int appointmentId, int rating = 5, string? review = "Great") =>
        new() { AppointmentId = appointmentId, Rating = rating, Review = review };

    // ---------- add ----------

    [Fact]
    public async Task A_patient_rates_the_doctor_of_a_completed_appointment()
    {
        var appt = await CompletedAsync(Pat, S0900);

        var r = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt, 4, "Kind"), null);

        r.Id.ShouldBeGreaterThan(0);
        r.DoctorId.ShouldBe(Doc.DoctorId);
        r.PatientId.ShouldBe(Pat.PatientId);
        r.AppointmentId.ShouldBe(appt);
        r.Rating.ShouldBe(4);
        r.Review.ShouldBe("Kind");
        var u = await UserOf(Pat.UserId);
        r.PatientName.ShouldBe($"{u.firstName} {u.lastName}");
        var row = await RatingRowAsync(r.Id);
        row.DoctorId.ShouldBe(Doc.DoctorId);
        row.Rating.ShouldBe(4);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.InProgress)]
    public async Task Only_a_completed_appointment_can_be_rated(AppointmentStatus status)
    {
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900, status);

        await ShouldFailWith("DoctorRating.AppointmentNotCompleted",
            () => NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt), null));

        (await RatingCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task An_appointment_is_rated_once()
    {
        var appt = await CompletedAsync(Pat, S0900);
        await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt), null);

        await ShouldFailWith("DoctorRating.AlreadyRated",
            () => NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt, 1), null));

        (await RatingCountAsync()).ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_simultaneous_ratings_of_the_same_appointment_give_one_rating_and_one_clean_error()
    {
        var appt = await CompletedAsync(Pat, S0900);

        var attempts = Enumerable.Range(0, 6).Select(async _ =>
        {
            try { await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt), null); return (Exception?)null; }
            catch (Exception ex) { return ex; }
        });
        var results = await Task.WhenAll(attempts);

        (await RatingCountAsync()).ShouldBe(1);
        results.Count(e => e == null).ShouldBe(1);
        // the losers must get the normal "already rated" answer, not a database unique-index exception (HTTP 500)
        results.Where(e => e != null).ShouldAllBe(e => e is Sehatak.Application.DTOs.Exceptions.BusinessException);
    }

    [Fact]
    public async Task Someone_elses_or_unknown_appointment_cannot_be_rated()
    {
        var other = await C.AddPatientAsync();
        var theirs = await CompletedAsync(other, S0900);

        await ShouldFailWith("Appointment.NotFound", () => NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(theirs), null));
        await ShouldFailWith("Appointment.NotFound", () => NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(999_999), null));

        (await RatingCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_parent_rates_for_a_child_only_when_naming_the_child()
    {
        var kid = await C.AddSubPatientAsync(Pat);
        var appt = await CompletedAsync(kid, S0900);

        await ShouldFailWith("Appointment.NotFound", () => NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt), null));

        var r = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt), kid.PatientId);

        r.PatientId.ShouldBe(kid.PatientId);
        r.PatientName.ShouldBe("P Test");
    }

    [Fact]
    public async Task Another_parents_child_cannot_be_used()
    {
        var other = await C.AddPatientAsync();
        var theirKid = await C.AddSubPatientAsync(other);
        var appt = await CompletedAsync(theirKid, S0900);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt), theirKid.PatientId));
        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt), 999_999));
    }

    [Fact]
    public async Task Only_active_patients_of_an_active_center_can_rate()
    {
        var appt = await CompletedAsync(Pat, S0900);
        var gone = await C.AddPatientAsync(active: false);
        var suspended = await Fx.Harness.CreateCenterAsync(CenterStatus.Suspended);

        await ShouldFailWith("Patient.NotFound", () => NewRatings().AddDoctorRatingAsync(C.Id, gone.UserId, Add(appt), null));
        await ShouldFailWith("Patient.NotFound", () => NewRatings().AddDoctorRatingAsync(C.Id, Doc.UserId, Add(appt), null));
        await ShouldFailWith("Center.NotFound", () => NewRatings().AddDoctorRatingAsync(999_999, Pat.UserId, Add(appt), null));
        await ShouldFailWith("Center.NotFound", () => NewRatings().AddDoctorRatingAsync(suspended.Id, Pat.UserId, Add(appt), null));
    }

    // ---------- update ----------

    [Fact]
    public async Task Update_changes_only_what_was_sent_and_stamps_the_update_time()
    {
        var appt = await CompletedAsync(Pat, S0900);
        var added = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt, 5, "Great"), null);
        await Task.Delay(30);

        var r = await NewRatings().UpdateDoctorRatingAsync(C.Id, Pat.UserId, new UpdateDoctorRatingRequest { RatingId = added.Id, Rating = 2 }, null);

        r.Rating.ShouldBe(2);
        r.Review.ShouldBe("Great");                       // not sent -> kept
        var row = await RatingRowAsync(added.Id);
        row.Rating.ShouldBe(2);
        row.CreatedAt.ShouldBe(added.CreatedAt, TimeSpan.FromSeconds(1));
        row.UpdateAt.ShouldBeGreaterThan(added.UpdateAt);

        await NewRatings().UpdateDoctorRatingAsync(C.Id, Pat.UserId, new UpdateDoctorRatingRequest { RatingId = added.Id, Review = "Changed my mind" }, null);
        var again = await RatingRowAsync(added.Id);
        again.Rating.ShouldBe(2);
        again.Review.ShouldBe("Changed my mind");
    }

    [Fact]
    public async Task Update_cannot_touch_someone_elses_rating()
    {
        var other = await C.AddPatientAsync();
        var theirAppt = await CompletedAsync(other, S0900);
        var theirs = await SeedRatingAsync(Doc.DoctorId, other.PatientId, theirAppt, 5, "Theirs");

        await ShouldFailWith("Rating.NotFound", () => NewRatings().UpdateDoctorRatingAsync(C.Id, Pat.UserId,
            new UpdateDoctorRatingRequest { RatingId = theirs, Rating = 1, Review = "Hacked" }, null));
        await ShouldFailWith("Rating.NotFound", () => NewRatings().UpdateDoctorRatingAsync(C.Id, Pat.UserId,
            new UpdateDoctorRatingRequest { RatingId = 999_999, Rating = 1 }, null));

        var row = await RatingRowAsync(theirs);
        row.Rating.ShouldBe(5);
        row.Review.ShouldBe("Theirs");
    }

    [Fact]
    public async Task A_childs_rating_is_edited_through_the_parent_naming_the_child()
    {
        var kid = await C.AddSubPatientAsync(Pat);
        var appt = await CompletedAsync(kid, S0900);
        var added = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt, 3), kid.PatientId);

        await ShouldFailWith("Rating.NotFound", () => NewRatings().UpdateDoctorRatingAsync(C.Id, Pat.UserId,
            new UpdateDoctorRatingRequest { RatingId = added.Id, Rating = 1 }, null));

        var r = await NewRatings().UpdateDoctorRatingAsync(C.Id, Pat.UserId,
            new UpdateDoctorRatingRequest { RatingId = added.Id, Rating = 1 }, kid.PatientId);

        r.Rating.ShouldBe(1);
        r.PatientName.ShouldBe("P Test");
    }

    // ---------- remove ----------

    [Fact]
    public async Task A_removed_rating_is_gone_and_the_appointment_can_be_rated_again()
    {
        var appt = await CompletedAsync(Pat, S0900);
        var added = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt, 1), null);

        await NewRatings().RemoveDoctorRatingAsync(C.Id, Pat.UserId, added.Id, null);

        (await RatingCountAsync()).ShouldBe(0);
        var again = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(appt, 5), null);
        again.Rating.ShouldBe(5);
    }

    [Fact]
    public async Task Remove_cannot_delete_someone_elses_rating_or_an_unknown_one()
    {
        var other = await C.AddPatientAsync();
        var theirAppt = await CompletedAsync(other, S0900);
        var theirs = await SeedRatingAsync(Doc.DoctorId, other.PatientId, theirAppt, 5);

        await ShouldFailWith("Rating.NotFound", () => NewRatings().RemoveDoctorRatingAsync(C.Id, Pat.UserId, theirs, null));
        await ShouldFailWith("Rating.NotFound", () => NewRatings().RemoveDoctorRatingAsync(C.Id, Pat.UserId, 999_999, null));

        (await RatingCountAsync()).ShouldBe(1);
    }

    // ---------- patient's list ----------

    [Fact]
    public async Task A_patient_sees_only_their_own_ratings_newest_first_with_the_doctor_name()
    {
        var other = await C.AddPatientAsync();
        var a1 = await CompletedAsync(Pat, S0900);
        var a2 = await CompletedAsync(Pat, S0930);
        var a3 = await CompletedAsync(other, S1000);
        var first = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(a1, 3), null);
        await Task.Delay(30);
        var second = await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(a2, 5), null);
        await NewRatings().AddDoctorRatingAsync(C.Id, other.UserId, Add(a3, 1), null);
        var doc = await UserOf(Doc.UserId);

        var page = await NewRatings().PatientGetRatingsAsync(C.Id, Pat.UserId, new PagedRequest(), null);

        page.Items.Select(i => i.ratingId).ShouldBe(new[] { second.Id, first.Id });
        page.Items.ShouldAllBe(i => i.doctorName == $"{doc.firstName} {doc.lastName}" && i.doctorId == Doc.DoctorId);
        page.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_list_can_be_paged_and_scoped_to_a_child()
    {
        var kid = await C.AddSubPatientAsync(Pat);
        var mine = await CompletedAsync(Pat, S0900);
        var k1 = await CompletedAsync(kid, S0930);
        var k2 = await CompletedAsync(kid, S1000);
        await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(mine), null);
        await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(k1), kid.PatientId);
        await NewRatings().AddDoctorRatingAsync(C.Id, Pat.UserId, Add(k2), kid.PatientId);

        var parent = await NewRatings().PatientGetRatingsAsync(C.Id, Pat.UserId, new PagedRequest(), null);
        var child = await NewRatings().PatientGetRatingsAsync(C.Id, Pat.UserId, new PagedRequest { PageNumber = 1, PageSize = 1 }, kid.PatientId);

        parent.Items.Count.ShouldBe(1);
        child.Items.Count.ShouldBe(1);
        child.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_list_refuses_a_child_of_another_parent()
    {
        var other = await C.AddPatientAsync();
        var theirKid = await C.AddSubPatientAsync(other);

        await ShouldFailWith("SubPatient.NotFoundOrNotOwned",
            () => NewRatings().PatientGetRatingsAsync(C.Id, Pat.UserId, new PagedRequest(), theirKid.PatientId));
    }

    // ---------- doctor's view ----------

    [Fact]
    public async Task The_doctor_sees_the_average_and_each_review_with_the_patient_name()
    {
        var other = await C.AddPatientAsync();
        var kid = await C.AddSubPatientAsync(Pat);
        var a1 = await CompletedAsync(Pat, S0900);
        var a2 = await CompletedAsync(other, S0930);
        var a3 = await CompletedAsync(kid, S1000);
        await SeedRatingAsync(Doc.DoctorId, Pat.PatientId, a1, 5, "Great");
        await SeedRatingAsync(Doc.DoctorId, other.PatientId, a2, 4, "Good");
        await SeedRatingAsync(Doc.DoctorId, kid.PatientId, a3, 3, null);
        var u = await UserOf(Pat.UserId);

        var r = await NewRatings().DoctorGetRatingsAsync(C.Id, Doc.UserId);

        r.AvrageRating.ShouldBe(4.0, 0.0001);
        r.PatientRatings.Count.ShouldBe(3);
        r.PatientRatings.Single(p => p.patientId == Pat.PatientId).patientName.ShouldBe($"{u.firstName} {u.lastName}");
        r.PatientRatings.Single(p => p.patientId == kid.PatientId).patientName.ShouldBe("P Test");
        r.PatientRatings.Single(p => p.AppointmentId == a1).Review.ShouldBe("Great");
    }

    [Fact]
    public async Task The_doctor_does_not_see_other_doctors_ratings_and_an_empty_list_averages_zero()
    {
        var otherDoc = await C.AddDoctorAsync();
        var appt = await CompletedAsync(Pat, S0900, doctorId: otherDoc.DoctorId);
        await SeedRatingAsync(otherDoc.DoctorId, Pat.PatientId, appt, 1);

        var r = await NewRatings().DoctorGetRatingsAsync(C.Id, Doc.UserId);

        r.AvrageRating.ShouldBe(0);
        r.PatientRatings.ShouldBeEmpty();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Each_review_the_doctor_sees_carries_its_id_and_dates()
    {
        var a1 = await CompletedAsync(Pat, S0900);
        var id = await SeedRatingAsync(Doc.DoctorId, Pat.PatientId, a1, 5, "Great");

        var r = await NewRatings().DoctorGetRatingsAsync(C.Id, Doc.UserId);

        var item = r.PatientRatings.ShouldHaveSingleItem();
        item.ratingId.ShouldBe(id);                                         // never filled: 0
        item.CreatedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5)); // never filled: 0001-01-01
        item.UpdateAt.ShouldBeGreaterThan(DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task The_doctor_view_needs_an_active_doctor_in_an_active_center()
    {
        var goneDoc = await C.AddDoctorAsync(active: false);

        await ShouldFailWith("Doctor.NotFound", () => NewRatings().DoctorGetRatingsAsync(C.Id, goneDoc.UserId));
        await ShouldFailWith("Doctor.NotFound", () => NewRatings().DoctorGetRatingsAsync(C.Id, Pat.UserId));
        await ShouldFailWith("Center.NotFound", () => NewRatings().DoctorGetRatingsAsync(999_999, Doc.UserId));
    }

    private async Task<Sehatak.Domain.Entities.TenantEntities.User> UserOf(int userId)
    {
        await using var db = Db();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            db.Users, u => u.Id == userId);
    }
}
