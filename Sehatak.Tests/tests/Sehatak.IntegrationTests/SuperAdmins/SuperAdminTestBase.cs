using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using TokenSvc = Sehatak.Infrastructure.Services.tokenService.TokenService;

namespace Sehatak.IntegrationTests.SuperAdmins;

/// <summary>Shared-DB only: super admins live outside the tenant databases. Every test creates its own super admins.</summary>
public abstract class SuperAdminTestBase(SqlServerFixture fx)
{
    protected const string Pwd = "Str0ng!Pass";
    protected const string SetupKey = "test-setup-key";
    private static int _seq = Random.Shared.Next(1_000_000, 8_000_000);
    protected SqlServerFixture Fx { get; } = fx;
    protected SharedDbContext Shared() => Fx.Harness.NewSharedDb();

    protected static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    protected static IConfiguration NewConfig(string? setupKey = SetupKey)
    {
        var cfg = Substitute.For<IConfiguration>();
        cfg["SuperAdminSetup:SetupKey"].Returns(setupKey);
        return cfg;
    }

    protected static JwtTokenGenerator NewJwt()
    {
        var cfg = Substitute.For<IConfiguration>();
        cfg["Jwt:SecretKey"].Returns(new string('k', 48));
        cfg["Jwt:Issuer"].Returns("sehatak-tests");
        cfg["Jwt:Audience"].Returns("sehatak-tests");
        return new JwtTokenGenerator(cfg);
    }

    protected TokenSvc NewTokens(SharedDbContext shared) => new(shared, Fx.Harness.TenantFactory, NewJwt());

    protected static string NewEmail() => $"sa{Interlocked.Increment(ref _seq)}.{Guid.NewGuid():N}@test.local";
    protected static string NewPhone() => $"058{Interlocked.Increment(ref _seq):D7}";

    protected async Task<SuperAdmin> AddSuperAdminAsync(string? email = null, string password = Pwd, bool active = true, string name = "Root Admin")
    {
        await using var db = Shared();
        var s = new SuperAdmin
        {
            Name = name, Email = email ?? NewEmail(), phone = NewPhone(), role = userRole.SuperAdmin,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), CreateAt = DateTime.UtcNow, IsActive = active
        };
        db.SuperAdmins.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    protected async Task<SuperAdmin> ReloadAsync(int id)
    {
        await using var db = Shared();
        return await db.SuperAdmins.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    protected async Task<int> AddCodeAsync(int superAdminId, string code, string purpose, string? pending,
        bool used = false, double expiresInMinutes = 10)
    {
        await using var db = Shared();
        var c = new emailVerificationCode
        {
            SuperAdminId = superAdminId, Code = code, Purpose = purpose, PendingValue = pending,
            IsUsed = used, ExpiresAt = DateTime.UtcNow.AddMinutes(expiresInMinutes)
        };
        db.emailVerificationCodes.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    protected async Task<List<emailVerificationCode>> CodesOfAsync(int superAdminId, string purpose)
    {
        await using var db = Shared();
        return await db.emailVerificationCodes.AsNoTracking()
            .Where(c => c.SuperAdminId == superAdminId && c.Purpose == purpose).OrderBy(c => c.Id).ToListAsync();
    }
}
