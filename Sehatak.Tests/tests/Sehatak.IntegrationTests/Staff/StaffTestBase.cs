using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using AttendanceSvc = Sehatak.Infrastructure.Services.StaffAttendanceService.StaffAttendanceService;
using ShiftSvc = Sehatak.Infrastructure.Services.ShiftService.ShiftScheduleService;

namespace Sehatak.IntegrationTests.Staff;

/// <summary>
/// Center + a nurse (Nurse) + shift "A" configured 09:00-17:00 (clinic time).
/// Times the "app" sends are UTC instants (the request validator compares CheckTime with UtcNow),
/// so tests build them from a clinic-local clock time with <see cref="LocalToUtc"/>.
/// </summary>
public abstract class StaffTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static int _seq;
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");

    protected static readonly TimeOnly T0900 = new(9, 0);
    protected static readonly TimeOnly T1700 = new(17, 0);

    protected int Nurse { get; private set; }
    protected DateOnly Today => ClinicClock.Today;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Nurse = await AddStaffAsync();
        await AddShiftScheduleAsync(ShiftGroup.A, T0900, T1700);
    }

    protected ShiftSvc NewShiftService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);
    protected AttendanceSvc NewAttendanceService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);

    protected static DateTime LocalToUtc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), Zone);

    protected async Task<int> AddStaffAsync(userRole role = userRole.Nurse, bool active = true)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var db = Db();
        var u = new User
        {
            firstName = $"Staff{n:D6}", lastName = $"Member{n:D6}",
            email = $"staff{n}.{Guid.NewGuid():N}@test.local",
            passwordHash = "hash-hash-hash", phoneNumber = $"057{n:D7}",
            address = "addr", city = "Hebron", role = role, isActive = active
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u.Id;
    }

    protected async Task<int> AddShiftScheduleAsync(ShiftGroup group, TimeOnly start, TimeOnly end)
    {
        await using var db = Db();
        var s = new ShiftSchedule { ShiftName = group, StartTime = start, EndTime = end };
        db.shiftSchedules.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    protected async Task<int> AddStaffShiftAsync(int userId, DateOnly date, ShiftGroup group = ShiftGroup.A, bool active = true)
    {
        await using var db = Db();
        var s = new StaffShift { UserId = userId, ShiftDate = date, ShiftName = group, IsActive = active };
        db.StaffShifts.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    protected async Task<StaffAttendance?> AttendanceOfAsync(int userId, DateOnly date)
    {
        await using var db = Db();
        return await db.StaffAttendances.AsNoTracking().SingleOrDefaultAsync(a => a.UserId == userId && a.AttendanceDate == date);
    }

    protected async Task SeedAttendanceAsync(int userId, int shiftId, DateOnly date, AttendanceStatus status,
        DateTime? checkIn = null, DateTime? checkOut = null)
    {
        await using var db = Db();
        db.StaffAttendances.Add(new StaffAttendance
        {
            UserId = userId, StaffShiftId = shiftId, AttendanceDate = date, attendanceStatus = status,
            CheckInTime = checkIn, CheckOutTime = checkOut
        });
        await db.SaveChangesAsync();
    }

    protected async Task<List<Notification>> NotificationsOfAsync(int userId)
    {
        await using var db = Db();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync();
    }

    protected async Task<StaffShift> LoadStaffShiftAsync(int id)
    {
        await using var db = Db();
        return await db.StaffShifts.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    protected async Task<int> CountAuditAsync(string action, int entityId)
    {
        await using var db = Db();
        return await db.AuditLogs.CountAsync(a => a.Action == action && a.EntityId == entityId);
    }

    protected static void ShouldBeNowUtc(DateTime value) =>
        Math.Abs((DateTime.UtcNow - value).TotalMinutes).ShouldBeLessThan(2);
}
