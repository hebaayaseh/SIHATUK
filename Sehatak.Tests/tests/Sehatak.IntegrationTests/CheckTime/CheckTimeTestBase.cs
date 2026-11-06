using Shouldly;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Svc = Sehatak.Infrastructure.Services.CheckTimeService.CheckTimeService;

namespace Sehatak.IntegrationTests.CheckTime;

/// <summary>Center + doctor + patient come from AppointmentTestBase; this adds a receptionist and "today" appointments.</summary>
public abstract class CheckTimeTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected int Reception { get; private set; }
    protected static DateOnly Today => ClinicClock.Today;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Reception = await C.AddReceptionistAsync();
    }

    protected Svc NewSvc() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);

    protected Task<int> AddTodayAsync(AppointmentStatus status = AppointmentStatus.Confirmed, DoctorInfo? doctor = null,
                                      bool checkedIn = false, DateTime? start = null, DateTime? end = null, DateOnly? date = null)
        => AddAsync(doctor ?? Doc, date ?? Today, status, checkedIn, start, end);

    protected async Task<int> AddAsync(DoctorInfo doctor, DateOnly date, AppointmentStatus status,
                                       bool checkedIn = false, DateTime? start = null, DateTime? end = null)
    {
        var id = await C.AddAppointmentAsync(doctor.DoctorId, Pat.PatientId, date, null, status);
        await using var db = Db();
        var a = await db.Appointments.SingleAsync(x => x.Id == id);
        if (checkedIn) a.CheckInTime = DateTime.UtcNow;
        a.actualStartTime = start;
        a.actualEndTime = end;
        await db.SaveChangesAsync();
        return id;
    }

    protected async Task<Appointment> LoadAsync(int id)
    {
        await using var db = Db();
        return await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    protected async Task<int> CountAuditAsync(string action, int entityId)
    {
        await using var db = Db();
        return await db.AuditLogs.CountAsync(a => a.Action == action && a.EntityId == entityId);
    }

    protected async Task SuspendCenterAsync()
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var c = await shared.MedicalCenters.SingleAsync(x => x.Id == C.Id);
        c.CenterStatus = CenterStatus.Suspended;
        await shared.SaveChangesAsync();
    }

    protected static void ShouldBeNowUtc(DateTime v) =>
        Math.Abs((DateTime.UtcNow - v).TotalMinutes).ShouldBeLessThan(2);
}
