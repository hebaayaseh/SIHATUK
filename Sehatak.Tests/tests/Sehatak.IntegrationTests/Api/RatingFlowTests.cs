using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// E2E batch 8a: patients rate the doctor after a completed visit; the doctor sees the average and the reviews.
/// The finished visits are seeded; everything else is real HTTP.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class RatingFlowTests(SqlServerFixture fx) : FlowTestBase(fx)
{
    private static DateOnly Past => ClinicClock.Today.AddDays(-3);
    private string PatientToken(PatientInfo p) => Token("Patient", C.Id, p.UserId);

    private async Task<(PatientInfo P, int Appt)> VisitAsync(TimeOnly? slot = null, AppointmentStatus status = AppointmentStatus.Completed, DoctorInfo? doctor = null)
    {
        var p = await C.AddPatientAsync();
        return (p, await C.AddAppointmentAsync((doctor ?? Doc).DoctorId, p.PatientId, Past, slot ?? new TimeOnly(9, 0), status));
    }

    private Task<HttpResponseMessage> Rate(PatientInfo p, int appt, int rating, string? review = "good", int? sub = null) =>
        PostAsync($"/DoctorRating/patient-add-doctor-rating/{C.Id}{(sub != null ? $"?subPatientId={sub}" : "")}",
            new { appointmentId = appt, rating, review }, PatientToken(p));

    private async Task<int> RatingIdAsync(int appt)
    {
        await using var db = Db();
        return await db.DoctorRatings.Where(r => r.AppointmentId == appt).Select(r => r.Id).SingleAsync();
    }

    private async Task<JsonElement> DoctorViewAsync(string? token = null) =>
        await OkJsonAsync(await GetAsync($"/DoctorRating/doctor-get-ratings/{C.Id}", token ?? DoctorToken));

    private static async Task Ok(HttpResponseMessage r) => r.StatusCode.ShouldBe(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());

    [Fact]
    public async Task Ratings_flow_from_the_patient_to_the_doctors_average()
    {
        var a = await VisitAsync(new TimeOnly(9, 0));
        var b = await VisitAsync(new TimeOnly(9, 30));

        // nothing yet
        (await DoctorViewAsync()).GetProperty("avrageRating").GetDouble().ShouldBe(0);

        // the first patient rates 4 with a review
        await Ok(await Rate(a.P, a.Appt, 4, "very kind doctor"));
        var view = await DoctorViewAsync();
        view.GetProperty("avrageRating").GetDouble().ShouldBe(4);
        view.GetProperty("patientRatings").GetArrayLength().ShouldBe(1);
        JsonContains(view, "very kind doctor").ShouldBeTrue(view.ToString());

        // the patient sees his own rating
        var mine = await OkJsonAsync(await PostAsync($"/DoctorRating/patient-get-ratings/{C.Id}?PageNumber=1&PageSize=10", new { }, PatientToken(a.P)));
        JsonContains(mine, "very kind doctor").ShouldBeTrue(mine.ToString());

        // he changes his mind: 4 -> 5
        var ratingId = await RatingIdAsync(a.Appt);
        await Ok(await PostAsync($"/DoctorRating/patient-update-doctor-rating/{C.Id}", new { ratingId, rating = 5, review = "even better" }, PatientToken(a.P)));
        (await DoctorViewAsync()).GetProperty("avrageRating").GetDouble().ShouldBe(5);

        // a second patient rates 3 -> average 4
        await Ok(await Rate(b.P, b.Appt, 3, null));
        (await DoctorViewAsync()).GetProperty("avrageRating").GetDouble().ShouldBe(4);

        // the first one removes his rating -> only the 3 is left
        await Ok(await PostAsync($"/DoctorRating/patient-remove-doctor-rating/{C.Id}/{ratingId}", new { }, PatientToken(a.P)));
        var after = await DoctorViewAsync();
        after.GetProperty("avrageRating").GetDouble().ShouldBe(3);
        after.GetProperty("patientRatings").GetArrayLength().ShouldBe(1);

        // and he can rate that visit again after removing it
        await Ok(await Rate(a.P, a.Appt, 5, "second thought"));
    }

    [Fact]
    public async Task Only_a_completed_visit_that_is_yours_and_not_yet_rated_can_be_rated()
    {
        var done = await VisitAsync(new TimeOnly(9, 0));
        var pending = await VisitAsync(new TimeOnly(9, 30), AppointmentStatus.Confirmed);
        var cancelled = await VisitAsync(new TimeOnly(10, 0), AppointmentStatus.Cancelled);
        var stranger = await C.AddPatientAsync();

        ((int)(await Rate(pending.P, pending.Appt, 5)).StatusCode).ShouldBe(400);      // not finished yet
        ((int)(await Rate(cancelled.P, cancelled.Appt, 5)).StatusCode).ShouldBe(400);  // cancelled
        ((int)(await Rate(stranger, done.Appt, 5)).StatusCode).ShouldBe(400);          // somebody else's visit
        ((int)(await Rate(done.P, 99_999_999, 5)).StatusCode).ShouldBe(400);           // no such visit

        await Ok(await Rate(done.P, done.Appt, 5));
        ((int)(await Rate(done.P, done.Appt, 1)).StatusCode).ShouldBe(400);            // twice

        await using var db = Db();
        (await db.DoctorRatings.CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task A_rating_outside_one_to_five_is_refused(int rating)
    {
        var v = await VisitAsync();

        ((int)(await Rate(v.P, v.Appt, rating)).StatusCode).ShouldBe(400);

        await using var db = Db();
        (await db.DoctorRatings.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task A_review_that_is_too_long_is_refused()
    {
        var v = await VisitAsync();

        ((int)(await Rate(v.P, v.Appt, 5, new string('x', 1001))).StatusCode).ShouldBe(400);
    }

    [Fact]
    public async Task Nobody_can_change_or_remove_somebody_elses_rating()
    {
        var v = await VisitAsync();
        await Ok(await Rate(v.P, v.Appt, 5, "mine"));
        var ratingId = await RatingIdAsync(v.Appt);
        var other = await C.AddPatientAsync();

        ((int)(await PostAsync($"/DoctorRating/patient-update-doctor-rating/{C.Id}", new { ratingId, rating = 1, review = "hacked" }, PatientToken(other))).StatusCode).ShouldBe(400);
        ((int)(await PostAsync($"/DoctorRating/patient-remove-doctor-rating/{C.Id}/{ratingId}", new { }, PatientToken(other))).StatusCode).ShouldBe(400);

        await using var db = Db();
        var row = await db.DoctorRatings.SingleAsync(r => r.Id == ratingId);
        row.Rating.ShouldBe(5);
        row.Review.ShouldBe("mine");
    }

    [Fact]
    public async Task A_parent_rates_the_visit_of_his_sub_patient_only_on_the_sub_patients_behalf()
    {
        var parent = await C.AddPatientAsync();
        var sub = await C.AddSubPatientAsync(parent);
        var appt = await C.AddAppointmentAsync(Doc.DoctorId, sub.PatientId, Past, new TimeOnly(9, 0), AppointmentStatus.Completed);

        ((int)(await Rate(parent, appt, 5)).StatusCode).ShouldBe(400);                 // as himself it is not his visit
        await Ok(await Rate(parent, appt, 5, "for my child", sub: sub.PatientId));

        await using var db = Db();
        (await db.DoctorRatings.SingleAsync(r => r.AppointmentId == appt)).PatientId.ShouldBe(sub.PatientId);

        var someoneElsesChild = await C.AddSubPatientAsync(await C.AddPatientAsync());
        ((int)(await Rate(parent, appt, 4, sub: someoneElsesChild.PatientId)).StatusCode).ShouldBe(400);
    }

    [Fact]
    public async Task A_doctor_sees_only_the_ratings_of_his_own_visits()
    {
        var v = await VisitAsync();
        await Ok(await Rate(v.P, v.Appt, 5, "for the first doctor"));
        var other = await C.AddDoctorAsync();

        var view = await DoctorViewAsync(Token("Doctor", C.Id, other.UserId));

        view.GetProperty("avrageRating").GetDouble().ShouldBe(0);
        view.GetProperty("patientRatings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Ratings_doors_are_closed_to_the_wrong_roles_and_to_strangers()
    {
        var v = await VisitAsync();
        var body = new { appointmentId = v.Appt, rating = 5, review = "x" };

        foreach (var role in new[] { "Doctor", "Receptionist", "Admin", "LabTechnician" })
            (await PostAsync($"/DoctorRating/patient-add-doctor-rating/{C.Id}", body, Token(role, C.Id, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);
        foreach (var role in new[] { "Patient", "Receptionist", "Admin" })
            (await GetAsync($"/DoctorRating/doctor-get-ratings/{C.Id}", Token(role, C.Id, 1))).StatusCode.ShouldBe(HttpStatusCode.Forbidden, role);

        (await PostAsync($"/DoctorRating/patient-add-doctor-rating/{C.Id}", body)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync($"/DoctorRating/doctor-get-ratings/{C.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PostAsync($"/DoctorRating/patient-add-doctor-rating/{C.Id + 1}", body, PatientToken(v.P))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
