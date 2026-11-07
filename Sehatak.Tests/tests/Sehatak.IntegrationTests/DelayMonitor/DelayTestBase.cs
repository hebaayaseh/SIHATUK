using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sehatak.Application.Common;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.PaymentEnums;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Job = Sehatak.Infrastructure.Services.SuperAdminService.Background.DelayMonitorJobs;

namespace Sehatak.IntegrationTests.DelayMonitor;

/// <summary>
/// Center + patient from AppointmentTestBase. D is a doctor working TODAY 09:00-12:00 in 30 min slots.
/// The two job passes are private, so they are invoked by reflection (same as the other job tests).
/// </summary>
public abstract class DelayTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected const string SlotPass = "CheckSlotTime";
    protected const string PrepayPass = "PrePayment";

    protected static DateOnly Today => ClinicClock.Today;
    protected static readonly TimeOnly T0900 = new(9, 0);
    protected DoctorInfo D { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        D = await C.AddDoctorAsync();
        await C.AddScheduleAsync(D.DoctorId, Today.DayOfWeek, T0900, new TimeOnly(12, 0), 30);
    }

    protected async Task RunAsync(string pass)
    {
        using var provider = Provider();
        var job = new Job(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<Job>.Instance);
        var m = typeof(Job).GetMethod(pass, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)m.Invoke(job, null)!;
    }

    protected ServiceProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => Fx.Harness.NewSharedDb());
        services.AddSingleton(Fx.Harness.TenantFactory);
        return services.BuildServiceProvider();
    }

    // ---------- seeding ----------

    protected async Task SetPrepaymentAsync(bool on, decimal amount = 50m)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var c = await shared.MedicalCenters.SingleAsync(x => x.Id == C.Id);
        c.RequiresPrepayment = on;
        c.PrepaymentAmount = amount;
        await shared.SaveChangesAsync();
    }

    protected async Task<int> AddApptAsync(int doctorId, int patientId, DateOnly date, TimeOnly? slot, AppointmentStatus status,
                                           DateTime? start = null, DateTime? end = null, bool processed = false)
    {
        await using var db = Db();
        var a = new Appointment
        {
            doctorId = doctorId, patientId = patientId, appointmentDate = date, timeSlot = slot,
            appointmentStatus = status, actualStartTime = start, actualEndTime = end
        };
        db.Appointments.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    }

    /// <summary>A finished visit today that lasted <paramref name="minutes"/> (the slot is 30).</summary>
    protected Task<int> AddCompletedAsync(int minutes, TimeOnly? slot = null, DoctorInfo? doctor = null, DateOnly? date = null, bool processed = false)
    {
        var start = DateTime.UtcNow.AddHours(-3);
        return AddApptAsync((doctor ?? D).DoctorId, Pat.PatientId, date ?? Today, slot ?? T0900,
                            AppointmentStatus.Completed, start, start.AddMinutes(minutes), processed);
    }

    protected Task<int> AddNoShowAsync(TimeOnly slot, DateOnly? date = null, DoctorInfo? doctor = null) =>
        AddApptAsync((doctor ?? D).DoctorId, Pat.PatientId, date ?? Today, slot, AppointmentStatus.NoShow);

    /// <summary>A confirmed booking that never started; <paramref name="checkedIn"/> = the receptionist saw the patient arrive.</summary>
    protected async Task<int> AddConfirmedAsync(TimeOnly slot, bool checkedIn = false)
    {
        var id = await AddApptAsync(D.DoctorId, Pat.PatientId, Today, slot, AppointmentStatus.Confirmed);
        if (checkedIn)
        {
            await using var db = Db();
            var a = await db.Appointments.SingleAsync(x => x.Id == id);
            a.CheckInTime = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return id;
    }

    protected static TimeOnly ClinicNow() { var n = ClinicClock.Now; return new TimeOnly(n.Hour, n.Minute); }

    protected async Task<int> AddWaiterAsync(PatientInfo patient, DoctorInfo? doctor = null, DateOnly? preferred = null, int createdMinutesAgo = 0)
    {
        await using var db = Db();
        var w = new Waitlist
        {
            PatientId = patient.PatientId, DoctorId = (doctor ?? D).DoctorId, PreferredDate = preferred ?? Today,
            Status = WaitlistStatus.Waiting, CreatedAt = DateTime.UtcNow.AddMinutes(-createdMinutesAgo)
        };
        db.Waitlists.Add(w);
        await db.SaveChangesAsync();
        return w.Id;
    }

    protected async Task<int> AddPrepaymentAsync(int appointmentId, int patientId, DateTime expiresAt, PaymentStatus status = PaymentStatus.Pending)
    {
        await using var db = Db();
        var p = new Payment
        {
            PatientId = patientId, AppointmentId = appointmentId, Amount = 50m, Type = PaymentType.Prepayment,
            Method = PaymentMethod.cash, Status = status, ExpiresAt = expiresAt
        };
        db.Payments.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    // ---------- reading ----------

    protected async Task<Appointment> ApptAsync(int id)
    {
        await using var db = Db();
        return await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    protected async Task<Waitlist> WaiterAsync(int id)
    {
        await using var db = Db();
        return await db.Waitlists.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    protected async Task<Payment> PaymentOfAsync(int appointmentId)
    {
        await using var db = Db();
        return await db.Payments.AsNoTracking().SingleAsync(p => p.AppointmentId == appointmentId);
    }

    protected async Task<List<Appointment>> NewAppointmentsAsync(int doctorId, params int[] knownIds)
    {
        await using var db = Db();
        return await db.Appointments.AsNoTracking()
            .Where(a => a.doctorId == doctorId && !knownIds.Contains(a.Id)).OrderBy(a => a.Id).ToListAsync();
    }

    protected async Task<List<Notification>> NotificationsAsync(int userId)
    {
        await using var db = Db();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync();
    }
}
