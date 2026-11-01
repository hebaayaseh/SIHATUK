using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AddDoctorDailyHour;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PostponeEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using HoursSvc = Sehatak.Infrastructure.Services.AddStaff.DoctorDailyHoursService;

namespace Sehatak.IntegrationTests.DoctorHours;

/// <summary>
/// Center with its admin (C.AdminUserId), a patient (Pat) and a doctor "Dr" who has NO schedule yet.
/// (The inherited Doc already works Mondays 09-11; these tests use their own doctor to control the schedule.)
/// MonDate is a future Monday.
/// </summary>
public abstract class DoctorHoursTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected static readonly TimeOnly T08 = new(8, 0);
    protected static readonly TimeOnly T09 = new(9, 0);
    protected static readonly TimeOnly T10 = new(10, 0);
    protected static readonly TimeOnly T11 = new(11, 0);
    protected static readonly TimeOnly T12 = new(12, 0);

    protected IMemoryCache Cache { get; } = new MemoryCache(new MemoryCacheOptions());
    protected DoctorInfo Dr { get; private set; } = null!;
    protected int Admin => C.AdminUserId;
    protected DateOnly MonDate => Date;          // AppointmentTestBase.Date is a future Monday

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Dr = await C.AddDoctorAsync();
    }

    protected HoursSvc NewHoursService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Cache, C.Audit);

    protected static AddDoctorDailyHoursRequest AddReq(DayOfWeek day, TimeOnly start, TimeOnly end, int slot = 30) =>
        new() { DayOfWeek = day, StartTime = start, EndTime = end, SlotDurationMinutes = slot };

    protected static UpdateDoctorDailyHousrRequest UpdReq(int scheduleId, DayOfWeek day, TimeOnly start, TimeOnly end, int slot = 30) =>
        new() { schedualId = scheduleId, DayOfWeek = day, StartTime = start, EndTime = end, SlotDurationMinutes = slot };

    protected async Task<Doctorschedule> LoadScheduleAsync(int id)
    {
        await using var db = Db();
        return await db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    protected async Task<List<Doctorschedule>> SchedulesOfAsync(int doctorId)
    {
        await using var db = Db();
        return await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == doctorId).OrderBy(s => s.Id).ToListAsync();
    }

    protected async Task<Appointment> LoadApptAsync(int id)
    {
        await using var db = Db();
        return await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    protected async Task<List<PostponedService>> PostponedOfAsync(int appointmentId)
    {
        await using var db = Db();
        return await db.PostponedServices.AsNoTracking().Where(p => p.AppointmentId == appointmentId).ToListAsync();
    }

    protected async Task<List<Notification>> NotificationsOfAsync(int userId)
    {
        await using var db = Db();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync();
    }

    protected async Task<List<DoctorBlockedDay>> BlockedOfAsync(int doctorId)
    {
        await using var db = Db();
        return await db.DoctorBlockedDays.AsNoTracking().Where(b => b.doctorId == doctorId).ToListAsync();
    }

    protected async Task<int> AddWaitingAsync(int doctorId, int patientId, DateOnly date, WaitlistStatus status = WaitlistStatus.Waiting)
    {
        await using var db = Db();
        var w = new Waitlist { DoctorId = doctorId, PatientId = patientId, PreferredDate = date, Status = status };
        db.Waitlists.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    protected async Task<WaitlistStatus> WaitStatusAsync(int id)
    {
        await using var db = Db();
        return (await db.Waitlists.AsNoTracking().SingleAsync(w => w.Id == id)).Status;
    }

    protected async Task SetBlockedAsync(int doctorId, DateOnly date, TimeOnly? slot, DateTime? createdUtc = null)
    {
        await using var db = Db();
        db.DoctorBlockedDays.Add(new DoctorBlockedDay
        {
            doctorId = doctorId, date = date, timeSlot = slot, isBlocked = true,
            CreatedAt = createdUtc ?? DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    protected async Task<int> CountAuditAsync(string action, int entityId)
    {
        await using var db = Db();
        return await db.AuditLogs.CountAsync(a => a.Action == action && a.EntityId == entityId);
    }

    protected async Task<int> AuditIdAsync(string action)
    {
        await using var db = Db();
        return (await db.AuditLogs.AsNoTracking().Where(a => a.Action == action).Select(a => a.EntityId).SingleAsync()) ?? 0;
    }

    protected static void ShouldBeNowUtc(DateTime value) =>
        Math.Abs((DateTime.UtcNow - value).TotalMinutes).ShouldBeLessThan(2);

    protected static DateOnly PastDateOn(DayOfWeek day)
    {
        var d = ClinicClock.Today.AddDays(-1);
        while (d.DayOfWeek != day) d = d.AddDays(-1);
        return d;
    }
}
