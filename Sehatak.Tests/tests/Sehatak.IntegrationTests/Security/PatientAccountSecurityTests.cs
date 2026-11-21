using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Auth;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using AuditSvc = Sehatak.Infrastructure.Services.AuditLogService.AuditLogService;
using PatientAuthSvc = Sehatak.Infrastructure.Services.PatientService.PatientRegisterAuth.AuthService;

namespace Sehatak.IntegrationTests.Security;

/// <summary>
/// Security decisions for the patient account (written by behaviour, not by implementation):
///  1) nobody can take over an e-mail that someone else started to register
///  2) a 6-digit code cannot be brute-forced
///  3) registration does not reveal whether an e-mail is already in use (privacy decision)
/// Tests marked KnownBug are red until the production fix is applied.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PatientAccountSecurityTests(SqlServerFixture fx) : AuthTestBase(fx)
{
    private const string VictimPwd = "Victim!Pass1";
    private const string AttackerPwd = "Attack!Pass2";
    private IEmailService Mail { get; } = Substitute.For<IEmailService>();

    private PatientAuthSvc NewAuth()
    {
        var shared = Fx.Harness.NewSharedDb();
        var audit = new AuditSvc(null!, null!, shared);
        return new PatientAuthSvc(shared, Fx.Harness.TenantFactory, Mail, NewTokens(shared),
                                  Substitute.For<IWebHostEnvironment>(), audit);
    }

    private static int _phone = 5000;
    private static RegisterRequestDto Reg(string email, string password, string first = "Rana") => new()
    {
        firstName = first, lastName = "Sami", email = email, password = password,
        phoneNumber = $"059{Interlocked.Increment(ref _phone):D7}", address = "Main st", city = "Hebron",
        gender = Gender.Female, bloodType = BloodType.A_Positive, DateOfBith = new DateOnly(1995, 3, 4)
    };

    private static string NewEmail() => $"sec.{Guid.NewGuid():N}@test.local";

    /// <summary>The code that the e-mail service was asked to send, in the order they were sent.</summary>
    private List<string> SentCodes(string email) => Mail.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(IEmailService.SendOtpAsync) && (string)c.GetArguments()[0]! == email)
        .Select(c => (string)c.GetArguments()[1]!).ToList();

    private async Task<User> UserAsync(string email)
    {
        await using var db = Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.email == email);
    }

    private static string WrongCodeFor(string real) => real == "000000" ? "000001" : "000000";

    // =========================================================== (1) takeover by re-registering

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_second_registration_with_the_same_email_does_not_overwrite_the_first_ones_data()
    {
        var email = NewEmail();
        await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd, "Victim"));
        var before = await UserAsync(email);

        try { await NewAuth().RegisterAsync(C.Id, Reg(email, AttackerPwd, "Attacker")); } catch (BusinessException) { }

        var after = await UserAsync(email);
        after.passwordHash.ShouldBe(before.passwordHash);
        after.firstName.ShouldBe("Victim");
        after.phoneNumber.ShouldBe(before.phoneNumber);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task After_a_second_registration_the_first_persons_code_still_gives_the_first_persons_password()
    {
        var email = NewEmail();
        var victim = await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd, "Victim"));
        var victimCode = SentCodes(email).Single();

        try { await NewAuth().RegisterAsync(C.Id, Reg(email, AttackerPwd, "Attacker")); } catch (BusinessException) { }

        // the victim types the code from their own mail box
        var ok = await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = victim.UserId, code = victimCode });
        ok.ShouldNotBeNull();

        // victim's password works, attacker's does not
        (await NewAuth().LoginPatientAsync(C.Id, new PatientRequestDto { email = email, password = VictimPwd }, null)).ShouldNotBeNull();
        await Should.ThrowAsync<BusinessException>(() =>
            NewAuth().LoginPatientAsync(C.Id, new PatientRequestDto { email = email, password = AttackerPwd }, null));
    }

    [Fact]
    public async Task The_attacker_cannot_activate_the_account_with_a_code_that_was_sent_to_the_victim_mailbox()
    {
        var email = NewEmail();
        var victim = await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd, "Victim"));
        var victimCode = SentCodes(email).Single();
        try { await NewAuth().RegisterAsync(C.Id, Reg(email, AttackerPwd, "Attacker")); } catch (BusinessException) { }

        // المهاجم ما بشوف الإيميل، فبخمّن بس: التخمين الغلط ما بفعّل الحساب
        var guess = WrongCodeFor(victimCode);
        (await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = victim.UserId, code = guess })).ShouldBeNull();

        (await UserAsync(email)).isActive.ShouldBeFalse();
    }

    // =========================================================== (2) OTP brute force

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task After_five_wrong_codes_even_the_right_code_is_refused()
    {
        var email = NewEmail();
        var r = await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd));
        var real = SentCodes(email).Single();

        for (var i = 0; i < 5; i++)
            await TryVerifyAsync(r.UserId, WrongCodeFor(real));

        (await TryVerifyAsync(r.UserId, real)).ShouldBeNull("5 wrong guesses must burn the code");
        (await UserAsync(email)).isActive.ShouldBeFalse();
    }

    [Fact]
    public async Task A_few_typos_do_not_block_the_real_owner()
    {
        var email = NewEmail();
        var r = await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd));
        var real = SentCodes(email).Single();

        await TryVerifyAsync(r.UserId, WrongCodeFor(real));
        await TryVerifyAsync(r.UserId, WrongCodeFor(real));

        (await TryVerifyAsync(r.UserId, real)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        var email = NewEmail();
        var r = await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd));
        var real = SentCodes(email).Single();

        (await TryVerifyAsync(r.UserId, real)).ShouldNotBeNull();
        (await TryVerifyAsync(r.UserId, real)).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Asking_for_a_new_code_cancels_the_old_one()
    {
        var email = NewEmail();
        var r = await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd));
        var first = SentCodes(email).Single();
        try { await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd)); } catch (BusinessException) { }

        var codes = SentCodes(email);
        if (codes.Count < 2) return;     // the fix may refuse the second request completely: nothing to check
        var second = codes[1];
        if (second == first) return;

        (await TryVerifyAsync(r.UserId, first)).ShouldBeNull("only the newest code may work");
        (await TryVerifyAsync(r.UserId, second)).ShouldNotBeNull();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Requesting_codes_again_and_again_is_limited()
    {
        var email = NewEmail();
        await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd));

        var refused = false;
        for (var i = 0; i < 10 && !refused; i++)
        {
            try { await NewAuth().RegisterAsync(C.Id, Reg(email, VictimPwd)); }
            catch (BusinessException) { refused = true; }
        }

        refused.ShouldBeTrue("an e-mail box can not be flooded with unlimited codes");
        SentCodes(email).Count.ShouldBeLessThan(10);
    }

    private async Task<VerifyOtpResponseDto?> TryVerifyAsync(int userId, string code)
    {
        try { return await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = code }); }
        catch (BusinessException) { return null; }
    }

    // =========================================================== (3) enumeration (privacy decision)

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Registering_an_email_that_exists_gives_the_same_answer_as_a_new_email()
    {
        var (_, existing) = await AddLoginUserAsync(userRole.Patient);

        // decision: the API answers "we sent a code if this is possible" in both cases, so nobody can probe e-mails.
        var fresh = await Record(() => NewAuth().RegisterAsync(C.Id, Reg(NewEmail(), VictimPwd)));
        var taken = await Record(() => NewAuth().RegisterAsync(C.Id, Reg(existing, VictimPwd)));

        taken.ShouldBe(fresh);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Registering_an_active_email_never_sends_a_code_to_it()
    {
        var (_, existing) = await AddLoginUserAsync(userRole.Patient);
        try { await NewAuth().RegisterAsync(C.Id, Reg(existing, VictimPwd)); } catch (BusinessException) { }

        SentCodes(existing).ShouldBeEmpty();
    }

    private static async Task<string> Record(Func<Task<RegisterResponseDto>> act)
    {
        try { await act(); return "ok"; }
        catch (BusinessException e) { return e.Message; }
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_look_the_same_on_login()
    {
        var (_, existing) = await AddLoginUserAsync(userRole.Patient);

        var wrongPwd = await Should.ThrowAsync<BusinessException>(() =>
            NewAuth().LoginPatientAsync(C.Id, new PatientRequestDto { email = existing, password = "Wrong!Pass9" }, null));
        var unknown = await Should.ThrowAsync<BusinessException>(() =>
            NewAuth().LoginPatientAsync(C.Id, new PatientRequestDto { email = NewEmail(), password = "Wrong!Pass9" }, null));

        wrongPwd.Message.ShouldBe(unknown.Message);
    }
}
