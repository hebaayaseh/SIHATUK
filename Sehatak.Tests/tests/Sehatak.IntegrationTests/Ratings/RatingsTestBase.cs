using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using RatingSvc = Sehatak.Infrastructure.Services.DoctorRatingService.DoctorRatingService;

namespace Sehatak.IntegrationTests.Ratings;

/// <summary>Center + doctor + patient from the base, and a real DoctorRatingService.</summary>
public abstract class RatingsTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected RatingSvc NewRatings() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);

    protected Task<int> CompletedAsync(PatientInfo patient, TimeOnly slot, int? doctorId = null, DateOnly? date = null) =>
        C.AddAppointmentAsync(doctorId ?? Doc.DoctorId, patient.PatientId, date ?? Date, slot, AppointmentStatus.Completed);

    protected async Task<DoctorRating> RatingRowAsync(int ratingId)
    {
        await using var db = Db();
        return await db.DoctorRatings.AsNoTracking().SingleAsync(r => r.Id == ratingId);
    }

    protected async Task<int> RatingCountAsync()
    {
        await using var db = Db();
        return await db.DoctorRatings.CountAsync();
    }

    /// <summary>Seeds a rating straight into the table (for doctors / patients the test does not act as).</summary>
    protected async Task<int> SeedRatingAsync(int doctorId, int patientId, int appointmentId, int rating, string? review = null)
    {
        await using var db = Db();
        var r = new DoctorRating
        {
            DoctorId = doctorId, PatientId = patientId, AppointmentId = appointmentId,
            Rating = rating, Review = review, CreatedAt = DateTime.UtcNow, UpdateAt = DateTime.UtcNow
        };
        db.DoctorRatings.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }
}
