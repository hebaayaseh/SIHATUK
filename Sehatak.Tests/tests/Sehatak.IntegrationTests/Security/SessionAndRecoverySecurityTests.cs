using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.EditProfileDto.EditEmailOrPasswored;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Auth;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using ProfileSvc = Sehatak.Infrastructure.Services.PatientService.PatientProfile.PatientProfileService;

namespace Sehatak.IntegrationTests.Security;

/// <summary>
/// Sessions after the account changes, and the "forgot password" flow.
/// Rule: when an account is deactivated or its password changes, old sessions must stop working.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SessionAndRecoverySecurityTests(SqlServerFixture fx) : AuthTestBase(fx)
{
    private IEmailService Mail { get; } = Substitute.For<IEmailService>();

    private ProfileSvc NewProfile() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Mail);

    private List<string> SentCodes(string email) => Mail.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(IEmailService.SendOtpAsync) && (string)c.GetArguments()[0]! == email)
        .Select(c => (string)c.GetArguments()[1]!).ToList();

    private async Task<string> RefreshTokenOfAsync(int userId, string email, string role)
    {
        var t = await NewTokens().IssueTokensAsync(userId, "Some One", email, role, C.Id, TokenOwnerType.TenantUser);
        return t.RefreshToken;
    }

    private async Task DeactivateAsync(int userId)
    {
        await using var db = Db();
        (await db.Users.SingleAsync(u => u.Id == userId)).isActive = false;
        await db.SaveChangesAsync();
    }

    // ============================================================ deactivated account vs refresh token

    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData(userRole.Patient, "Patient")]
    [InlineData(userRole.Doctor, "Doctor")]
    [InlineData(userRole.Receptionist, "Receptionist")]
    public async Task A_deactivated_account_cannot_get_a_new_access_token_with_its_old_refresh_token(userRole role, string roleName)
    {
        var (id, email) = await AddLoginUserAsync(role);
        var refresh = await RefreshTokenOfAsync(id, email, roleName);
        await DeactivateAsync(id);

        await Should.ThrowAsync<BusinessException>(() => NewTokens().RefreshAsync(refresh));
    }

    [Fact]
    public async Task An_active_account_can_refresh_and_the_old_refresh_token_dies()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Doctor);
        var refresh = await RefreshTokenOfAsync(id, email, "Doctor");

        (await NewTokens().RefreshAsync(refresh)).AccessToken.ShouldNotBeNullOrEmpty();

        await Should.ThrowAsync<BusinessException>(() => NewTokens().RefreshAsync(refresh));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_patient_who_deactivates_his_own_profile_loses_his_sessions()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        var refresh = await RefreshTokenOfAsync(id, email, "Patient");

        await NewPatientAuth().PatientDeactiveProfileAsync(C.Id, id);

        await Should.ThrowAsync<BusinessException>(() => NewTokens().RefreshAsync(refresh));
    }

    private Sehatak.Infrastructure.Services.PatientService.PatientRegisterAuth.AuthService NewPatientAuth()
    {
        var shared = Fx.Harness.NewSharedDb();
        var audit = new Sehatak.Infrastructure.Services.AuditLogService.AuditLogService(null!, null!, shared);
        return new(shared, Fx.Harness.TenantFactory, Mail, NewTokens(shared),
                   Substitute.For<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(), audit);
    }

    // ============================================================ forgot password

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task After_a_password_reset_the_old_sessions_are_closed()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        var refresh = await RefreshTokenOfAsync(id, email, "Patient");

        await NewProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = email, newPassword = "N3w!Passw0rd", confirmPassword = "N3w!Passw0rd" });
        await NewProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = SentCodes(email).Single() });

        await Should.ThrowAsync<BusinessException>(() => NewTokens().RefreshAsync(refresh));
    }

    [Fact]
    public async Task The_reset_changes_the_password_only_after_the_right_code()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        var before = await HashAsync(id);

        await NewProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = email, newPassword = "N3w!Passw0rd", confirmPassword = "N3w!Passw0rd" });
        (await HashAsync(id)).ShouldBe(before);            // asking is not enough

        await NewProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = SentCodes(email).Single() });
        BCrypt.Net.BCrypt.Verify("N3w!Passw0rd", await HashAsync(id)).ShouldBeTrue();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task After_five_wrong_reset_codes_even_the_right_code_is_refused()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        await NewProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = email, newPassword = "N3w!Passw0rd", confirmPassword = "N3w!Passw0rd" });
        var real = SentCodes(email).Single();
        var wrong = real == "000000" ? "000001" : "000000";

        for (var i = 0; i < 5; i++)
            await Should.ThrowAsync<BusinessException>(() => NewProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = wrong }));

        await Should.ThrowAsync<BusinessException>(() => NewProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = real }));
        BCrypt.Net.BCrypt.Verify(Pwd, await HashAsync(id)).ShouldBeTrue("the password must not have changed");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_new_reset_request_cancels_the_previous_code()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        await NewProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = email, newPassword = "First!Passw0rd", confirmPassword = "First!Passw0rd" });
        await NewProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = email, newPassword = "Second!Passw0rd", confirmPassword = "Second!Passw0rd" });
        var codes = SentCodes(email);

        await Should.ThrowAsync<BusinessException>(() => NewProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = codes[0] }));
    }

    // ============================================================ privacy decision: no e-mail enumeration

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Forgot_password_answers_the_same_for_a_known_and_an_unknown_email()
    {
        var (_, known) = await AddLoginUserAsync(userRole.Patient);
        var unknown = $"nobody.{Guid.NewGuid():N}@test.local";

        var a = await Answer(known);
        var b = await Answer(unknown);

        b.ShouldBe(a);
        SentCodes(unknown).ShouldBeEmpty();
    }

    private async Task<string> Answer(string email)
    {
        try
        {
            return await NewProfile().ForgetPasswordAsync(C.Id,
                new ForgetPasswordRequest { Email = email, newPassword = "N3w!Passw0rd", confirmPassword = "N3w!Passw0rd" });
        }
        catch (BusinessException e) { return "ERR:" + e.Message; }
    }

    private async Task<string> HashAsync(int userId)
    {
        await using var db = Db();
        return (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).passwordHash;
    }
}
