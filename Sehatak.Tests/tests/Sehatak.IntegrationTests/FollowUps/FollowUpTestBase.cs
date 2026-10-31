using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using FollowSvc = Sehatak.Infrastructure.Services.FollowUpService.FollowUpService;

namespace Sehatak.IntegrationTests.FollowUps;

/// <summary>
/// Center + doctor + patient + receptionist, and one Confirmed appointment (Appt) for the doctor and patient.
/// FollowUpDate is a valid "allow until" date a week after the appointment date.
/// </summary>
public abstract class FollowUpTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static int _seq;

    protected int Rec { get; private set; }
    protected int Appt { get; private set; }
    protected DateOnly FollowUpDate => Date.AddDays(7);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Rec = await C.AddReceptionistAsync();
        Appt = await C.AddAppointmentAsync(Doc.DoctorId, Pat.PatientId, Date, S0900);
    }

    protected FollowSvc NewFollowService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);

    protected async Task<int> SetAppointmentStatusAsync(int appointmentId, AppointmentStatus status)
    {
        await using var db = Db();
        (await db.Appointments.SingleAsync(a => a.Id == appointmentId)).appointmentStatus = status;
        await db.SaveChangesAsync();
        return appointmentId;
    }

    /// <summary>A follow-up written straight to the database, with its own (Completed, past) original appointment.</summary>
    protected async Task<int> SeedFollowUpAsync(int? doctorId = null, int? patientId = null,
        FollowUpStatus status = FollowUpStatus.Pending, DateTime? createdUtc = null,
        DateOnly? allowUntil = null, int? originalAppointmentId = null, int? scheduledAppointmentId = null)
    {
        var doc = doctorId ?? Doc.DoctorId;
        var pat = patientId ?? Pat.PatientId;
        var original = originalAppointmentId ?? await C.AddAppointmentAsync(doc, pat,
            ClinicClock.Today.AddDays(-10 - Interlocked.Increment(ref _seq)), null, AppointmentStatus.Completed);

        await using var db = Db();
        var f = new FollowUp
        {
            OriginalAppointmentId = original, PatientId = pat, DoctorId = doc,
            AllowFollowUpDate = allowUntil ?? FollowUpDate, Status = status,
            CreatedAt = createdUtc ?? DateTime.UtcNow, UpdatedAt = createdUtc ?? DateTime.UtcNow,
            ScheduledAppointmentId = scheduledAppointmentId
        };
        db.FollowUps.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    protected async Task<FollowUp> LoadFollowUpAsync(int id)
    {
        await using var db = Db();
        return await db.FollowUps.AsNoTracking().SingleAsync(f => f.Id == id);
    }

    protected async Task<int> CountAuditAsync(string action, int entityId)
    {
        await using var db = Db();
        return await db.AuditLogs.CountAsync(a => a.Action == action && a.EntityId == entityId);
    }

    protected async Task<List<Notification>> NotificationsOfAsync(int userId)
    {
        await using var db = Db();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync();
    }

    protected static void ShouldBeNowUtc(DateTime value) =>
        value.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
}
