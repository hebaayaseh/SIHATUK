using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.Interfaces.AuditLog;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Infrastructure.CalculateSlot;
using Sehatak.Infrastructure.Services.AppointmentService;
using AuditLogEntity = Sehatak.Domain.Entities.General.AuditLog;

namespace Sehatak.IntegrationTests.Infrastructure;

public sealed record DoctorInfo(int UserId, int DoctorId);
public sealed record PatientInfo(int UserId, int PatientId);

/// <summary>
/// A real, migrated center (row in the Shared DB + its own tenant DB) with small helpers to seed data.
/// Seeding writes straight to the DB so tests do not depend on other services.
/// </summary>
public sealed class TestCenter
{
    private static int _seq;

    public TenantTestHarness Harness { get; }
    public int Id { get; }
    public int AdminUserId { get; private set; }
    public int DepartmentId { get; private set; }

    /// <summary>Audit mock: Build() returns a real AuditLog row owned by the center admin so the FK is valid.</summary>
    public IAuditLog Audit { get; } = Substitute.For<IAuditLog>();

    internal TestCenter(TenantTestHarness harness, int id)
    {
        Harness = harness;
        Id = id;
    }

    internal async Task InitAsync()
    {
        await using var db = Harness.Tenant(Id);
        var admin = NewUser(userRole.Admin);
        var dept = new Department { Name = "General" };
        db.Users.Add(admin);
        db.Departments.Add(dept);
        await db.SaveChangesAsync();
        AdminUserId = admin.Id;
        DepartmentId = dept.Id;

        Audit.Build(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<object?>(), Arg.Any<object?>())
             .Returns(ci => new AuditLogEntity
             {
                 UserId = AdminUserId,
                 Action = ci.ArgAt<string>(0),
                 EntityType = ci.ArgAt<string>(1),
                 EntityId = ci.ArgAt<int?>(2),
                 NewValue = "{}"
             });
    }

    /// <summary>A new service instance (each has its own SharedDbContext, like one DI scope per request).</summary>
    public appointmentService NewAppointmentService() =>
        new(Harness.NewSharedDb(), Harness.TenantFactory, new GenerateTheoreticalSlots(), Audit);

    // ---------- seeding ----------

    public async Task<DoctorInfo> AddDoctorAsync(bool active = true)
    {
        await using var db = Harness.Tenant(Id);
        var user = NewUser(userRole.Doctor, active);
        var doctor = new Doctor { user = user, departmentId = DepartmentId, Specialization = "GP", Bio = "bio" };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return new DoctorInfo(user.Id, doctor.Id);
    }

    public async Task<PatientInfo> AddPatientAsync(bool active = true)
    {
        await using var db = Harness.Tenant(Id);
        var user = NewUser(userRole.Patient, active);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var patient = NewPatient(user.Id, parentPatientId: null, notifiableUserId: user.Id);
        db.Patients.Add(patient);
        await db.SaveChangesAsync();
        return new PatientInfo(user.Id, patient.patientId);
    }

    public async Task<int> AddReceptionistAsync(bool active = true)
    {
        await using var db = Harness.Tenant(Id);
        var user = NewUser(userRole.Receptionist, active);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>A dependent (child) record: no login of its own, owned by the parent patient.</summary>
    public async Task<PatientInfo> AddSubPatientAsync(PatientInfo parent)
    {
        await using var db = Harness.Tenant(Id);
        var sub = NewPatient(userId: null, parentPatientId: parent.PatientId, notifiableUserId: parent.UserId);
        db.Patients.Add(sub);
        await db.SaveChangesAsync();
        return new PatientInfo(parent.UserId, sub.patientId);
    }

    public async Task<int> AddScheduleAsync(int doctorId, DayOfWeek day, TimeOnly start, TimeOnly end,
                                            int? slotMinutes = 30, bool isActive = true)
    {
        await using var db = Harness.Tenant(Id);
        var s = new Doctorschedule
        {
            DoctorId = doctorId, DayOfWeek = day, StartTime = start, EndTime = end,
            SlotDurationMinutes = slotMinutes, IsActive = isActive
        };
        db.DoctorSchedules.Add(s);
        await db.SaveChangesAsync();
        return s.Id;
    }

    /// <summary>timeSlot = null blocks the whole day; otherwise just that slot.</summary>
    public async Task BlockAsync(int doctorId, DateOnly date, TimeOnly? slot = null)
    {
        await using var db = Harness.Tenant(Id);
        db.DoctorBlockedDays.Add(new DoctorBlockedDay { doctorId = doctorId, date = date, timeSlot = slot, isBlocked = true });
        await db.SaveChangesAsync();
    }

    public async Task<int> AddAppointmentAsync(int doctorId, int patientId, DateOnly date, TimeOnly? slot,
                                               AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        await using var db = Harness.Tenant(Id);
        var a = new Appointment
        {
            doctorId = doctorId, patientId = patientId, appointmentDate = date,
            timeSlot = slot, appointmentStatus = status
        };
        db.Appointments.Add(a);
        await db.SaveChangesAsync();
        return a.Id;
    }

    /// <summary>Creates a Completed original appointment plus a Pending follow-up valid until <paramref name="allowUntil"/>.</summary>
    public async Task<int> AddFollowUpAsync(int doctorId, int patientId, DateOnly allowUntil)
    {
        var originalId = await AddAppointmentAsync(doctorId, patientId, ClinicClock.Today.AddDays(-10), null, AppointmentStatus.Completed);
        await using var db = Harness.Tenant(Id);
        var f = new FollowUp
        {
            OriginalAppointmentId = originalId, PatientId = patientId, DoctorId = doctorId,
            AllowFollowUpDate = allowUntil, Status = FollowUpStatus.Pending, UpdatedAt = DateTime.UtcNow
        };
        db.FollowUps.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    /// <summary>First date at least 3 days ahead (clinic time) that falls on <paramref name="day"/>.</summary>
    public static DateOnly FutureDate(DayOfWeek day)
    {
        var d = ClinicClock.Today.AddDays(3);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d;
    }

    // ---------- factories ----------

    private static User NewUser(userRole role, bool active = true)
    {
        var n = Interlocked.Increment(ref _seq);
        return new User
        {
            firstName = $"First{n}", lastName = $"Last{n}",
            email = $"u{n}.{Guid.NewGuid():N}@test.local",
            passwordHash = "hash-hash-hash",
            phoneNumber = $"059{n:D7}",
            address = "addr", city = "Hebron",
            role = role, isActive = active
        };
    }

    private static Patient NewPatient(int? userId, int? parentPatientId, int notifiableUserId) => new()
    {
        userId = userId, ParentPatientId = parentPatientId, NotifiableUserId = notifiableUserId,
        FirstName = "P", LastName = "Test", DateOfBith = new DateOnly(1990, 1, 1),
        WhatsappNumber = "0599000000", BloodType = BloodType.O_Positive, Gender = Gender.Male
    };
}
