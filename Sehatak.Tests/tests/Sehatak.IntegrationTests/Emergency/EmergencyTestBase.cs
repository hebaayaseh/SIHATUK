using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using EmergencySvc = Sehatak.Infrastructure.Services.EmergencyService.EmergencyService;

namespace Sehatak.IntegrationTests.Emergency;

/// <summary>
/// Center with a receptionist (Rec) and one general doctor (GenDoc) who is on shift today (Present).
/// Emergency cases are not tied to patients/appointments: just a name, a general doctor and money.
/// </summary>
public abstract class EmergencyTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static int _seq;

    protected int Rec { get; private set; }
    protected int GenDoc { get; private set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Rec = await C.AddReceptionistAsync();
        GenDoc = await AddGeneralDoctorAsync();
        await AddAttendanceAsync(GenDoc);
    }

    protected EmergencySvc NewEmergencyService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);

    protected async Task<int> AddGeneralDoctorAsync(bool active = true, userRole role = userRole.GeneralDoctor)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var db = Db();
        var user = new User
        {
            firstName = $"Gen{n}", lastName = $"Doc{n}",
            email = $"gen{n}.{Guid.NewGuid():N}@test.local",
            passwordHash = "hash-hash-hash", phoneNumber = $"056{n:D7}",
            address = "addr", city = "Hebron", role = role, isActive = active
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Shift + attendance row for a staff member (one per user per day).</summary>
    protected async Task AddAttendanceAsync(int userId, AttendanceStatus status = AttendanceStatus.Present,
        DateOnly? date = null, ShiftGroup group = ShiftGroup.A)
    {
        var day = date ?? ClinicClock.Today;
        await using var db = Db();
        var shift = new StaffShift { UserId = userId, ShiftDate = day, ShiftName = group, IsActive = true };
        db.StaffShifts.Add(shift);
        await db.SaveChangesAsync();
        db.StaffAttendances.Add(new StaffAttendance
        {
            UserId = userId, StaffShiftId = shift.Id, AttendanceDate = day, attendanceStatus = status
        });
        await db.SaveChangesAsync();
    }

    protected async Task<int> SeedEmergencyAsync(string name = "Ali", bool insured = false, decimal amount = 50m,
        int? doctorUserId = null, DateTime? createdUtc = null)
    {
        await using var db = Db();
        var e = new EmergencyCase
        {
            PatientName = name, DoctorUserId = doctorUserId ?? GenDoc, ReceptionistId = Rec,
            IsInsurance = insured, AmountPaid = amount,
            CreatedAt = createdUtc ?? DateTime.UtcNow, UpdatedAt = createdUtc ?? DateTime.UtcNow
        };
        db.EmergencyCases.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    protected async Task<EmergencyCase> LoadAsync(int id)
    {
        await using var db = Db();
        return await db.EmergencyCases.AsNoTracking().SingleAsync(e => e.Id == id);
    }

    protected async Task<int> CountCasesAsync()
    {
        await using var db = Db();
        return await db.EmergencyCases.CountAsync();
    }

    protected async Task<int> CountAuditAsync(string action, int entityId)
    {
        await using var db = Db();
        return await db.AuditLogs.CountAsync(a => a.Action == action && a.EntityId == entityId);
    }

    protected static void ShouldBeNowUtc(DateTime value) =>
        Math.Abs((DateTime.UtcNow - value).TotalMinutes).ShouldBeLessThan(2);
}
