using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using CenterListSvc = Sehatak.Infrastructure.Services.PtientCenterService.GetPatientCenterService;
using EditStaffSvc = Sehatak.Infrastructure.Services.EditProfileService.EditStaffService;
using PatientProfileSvc = Sehatak.Infrastructure.Services.PatientService.PatientProfile.PatientProfileService;
using SubPatientSvc = Sehatak.Infrastructure.Services.SupPatientService.SubPatientService;
using ViewProfileSvc = Sehatak.Infrastructure.Services.ViewProfileService.ViewProfileService;

namespace Sehatak.IntegrationTests.Patients;

/// <summary>
/// Center + doctor + patient from the base, plus factories for the patient / profile services
/// (mocked email sender, so the OTP is read back from the EmailVerificationCodes table).
/// </summary>
public abstract class PatientsTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected IEmailService Email { get; } = Substitute.For<IEmailService>();

    protected SubPatientSvc NewSubPatients() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, C.Audit);
    protected CenterListSvc NewCenterList() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);
    protected ViewProfileSvc NewViewProfile() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);
    protected PatientProfileSvc NewPatientProfile() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Email);
    protected EditStaffSvc NewEditStaff() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Email);

    protected async Task<User> UserAsync(int userId)
    {
        await using var db = Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    protected async Task<Patient> PatientRowAsync(int patientId)
    {
        await using var db = Db();
        return await db.Patients.AsNoTracking().SingleAsync(p => p.patientId == patientId);
    }

    protected async Task SetPasswordAsync(int userId, string plain)
    {
        await using var db = Db();
        var u = await db.Users.SingleAsync(x => x.Id == userId);
        u.passwordHash = BCrypt.Net.BCrypt.HashPassword(plain);
        await db.SaveChangesAsync();
    }

    protected async Task SetProfileImageAsync(int userId, string? url)
    {
        await using var db = Db();
        var u = await db.Users.SingleAsync(x => x.Id == userId);
        u.ProfileImageUrl = url;
        await db.SaveChangesAsync();
    }

    protected async Task<EmailVerificationCode> LastCodeAsync(int userId, string purpose)
    {
        await using var db = Db();
        return await db.EmailVerificationCodes.AsNoTracking()
            .Where(c => c.UserId == userId && c.Purpose == purpose)
            .OrderByDescending(c => c.Id).FirstAsync();
    }

    protected async Task<int> CodeCountAsync(int userId)
    {
        await using var db = Db();
        return await db.EmailVerificationCodes.CountAsync(c => c.UserId == userId);
    }

    protected async Task ExpireCodeAsync(int codeId)
    {
        await using var db = Db();
        var c = await db.EmailVerificationCodes.SingleAsync(x => x.Id == codeId);
        c.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    protected async Task<MedicalCenter> CenterRowAsync()
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.MedicalCenters.AsNoTracking().SingleAsync(c => c.Id == C.Id);
    }

    protected async Task UpdateCenterRowAsync(Action<MedicalCenter> change)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var c = await shared.MedicalCenters.SingleAsync(x => x.Id == C.Id);
        change(c);
        await shared.SaveChangesAsync();
    }

    /// <summary>A fake upload. Only the claimed length matters for the size check.</summary>
    protected static IFormFile Image(string fileName, long claimedLength = 10) =>
        new FormFile(new MemoryStream(new byte[10]), 0, claimedLength, "profileImage", fileName);
}
