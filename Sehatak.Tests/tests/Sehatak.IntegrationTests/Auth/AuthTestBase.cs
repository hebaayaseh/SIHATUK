using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Security;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using AuditSvc = Sehatak.Infrastructure.Services.AuditLogService.AuditLogService;
using LoginSvc = Sehatak.Infrastructure.Services.StaffLogin.StaffAuthService;
using TokenSvc = Sehatak.Infrastructure.Services.tokenService.TokenService;

namespace Sehatak.IntegrationTests.Auth;

/// <summary>
/// Center + helpers to create users with a REAL bcrypt password and read the shared-DB token / login-attempt tables.
/// The lockout/attempt methods of the real AuditLogService are used (they only need the SharedDbContext).
/// </summary>
public abstract class AuthTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected const string Pwd = "Str0ng!Pass";
    private static int _seq;

    protected static JwtTokenGenerator NewJwt()
    {
        var cfg = Substitute.For<IConfiguration>();
        cfg["Jwt:SecretKey"].Returns(new string('k', 48));
        cfg["Jwt:Issuer"].Returns("sehatak-tests");
        cfg["Jwt:Audience"].Returns("sehatak-tests");
        return new JwtTokenGenerator(cfg);
    }

    protected TokenSvc NewTokens(SharedDbContext? shared = null) =>
        new(shared ?? Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, NewJwt());

    /// <summary>One shared context for login + audit + token services, like one DI scope per request.</summary>
    protected LoginSvc NewLogin(int? centerId = null)
    {
        var shared = Fx.Harness.NewSharedDb();
        var audit = new AuditSvc(null!, null!, shared);
        return new LoginSvc(Fx.Harness.TenantFactory, Substitute.For<IEmailService>(), shared, NewTokens(shared), audit);
    }

    protected async Task<(int Id, string Email)> AddLoginUserAsync(userRole role = userRole.Receptionist, bool active = true,
        string? email = null, string password = Pwd)
    {
        var n = Interlocked.Increment(ref _seq);
        email ??= $"login{n}.{Guid.NewGuid():N}@test.local";
        await using var db = Db();
        var u = new User
        {
            firstName = $"Fn{n}", lastName = $"Ln{n}", email = email,
            passwordHash = BCrypt.Net.BCrypt.HashPassword(password), phoneNumber = $"056{n:D7}",
            address = "addr", city = "Hebron", role = role, isActive = active
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return (u.Id, email);
    }

    protected async Task SetCenterStatusAsync(CenterStatus status)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var c = await shared.MedicalCenters.SingleAsync(x => x.Id == C.Id);
        c.CenterStatus = status;
        await shared.SaveChangesAsync();
    }
    protected async Task<User> UserAsync(int userId)
    {
        await using var db = Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    protected async Task SeedAttemptsAsync(string email, int count, bool success = false, double minutesAgo = 1,
        LoginUserType type = LoginUserType.Staff, int? centerId = null)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        for (var i = 0; i < count; i++)
            shared.LoginAttempts.Add(new LoginAttempt
            {
                CenterId = centerId ?? C.Id, Email = email, Success = success, UserType = type,
                AttemptedAt = DateTime.UtcNow.AddMinutes(-minutesAgo)
            });
        await shared.SaveChangesAsync();
    }

    protected async Task<List<LoginAttempt>> AttemptsOfAsync(string email)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.LoginAttempts.AsNoTracking().Where(a => a.Email == email && a.CenterId == C.Id).ToListAsync();
    }

    protected async Task<List<RefreshToken>> TokensOfAsync(int userId)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.RefreshTokens.AsNoTracking().Where(t => t.UserId == userId && t.CenterId == C.Id).OrderBy(t => t.Id).ToListAsync();
    }

    protected async Task<RefreshToken> InsertTokenAsync(int userId, string value, bool revoked = false, DateTime? expiresAt = null, int? centerId = null)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var t = new RefreshToken
        {
            Token = value, OwnerType = TokenOwnerType.TenantUser, UserId = userId,
            CenterId = centerId ?? C.Id, IsRevoked = revoked, ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1)
        };
        shared.RefreshTokens.Add(t);
        await shared.SaveChangesAsync();
        return t;
    }

    protected static string? Claim(string jwt, params string[] types)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        return token.Claims.FirstOrDefault(c => types.Contains(c.Type))?.Value;
    }

    protected static string? RoleOf(string jwt) => Claim(jwt, "role", ClaimTypes.Role);
    protected static string? NameOf(string jwt) => Claim(jwt, "unique_name", ClaimTypes.Name);
    protected static string? UserIdOf(string jwt) => Claim(jwt, "nameid", ClaimTypes.NameIdentifier);
}
