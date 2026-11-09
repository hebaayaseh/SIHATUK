using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.General;
using Sehatak.Domain.Entities.TenantEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using AuditSvc = Sehatak.Infrastructure.Services.AuditLogService.AuditLogService;
using PatientAuthSvc = Sehatak.Infrastructure.Services.PatientService.PatientRegisterAuth.AuthService;

namespace Sehatak.IntegrationTests.Auth;

/// <summary>Patient self-service: register, e-mail code, login, deactivate and re-activate the account.</summary>
[Collection(SqlServerCollection.Name)]
public class PatientAuthTests(SqlServerFixture fx) : AuthTestBase(fx)
{
    private IEmailService Mail { get; } = Substitute.For<IEmailService>();

    private PatientAuthSvc NewAuth()
    {
        var shared = Fx.Harness.NewSharedDb();
        var audit = new AuditSvc(null!, null!, shared);
        return new PatientAuthSvc(shared, Fx.Harness.TenantFactory, Mail, NewTokens(shared),
                                  Substitute.For<IWebHostEnvironment>(), audit);
    }

    private static PatientRequestDto Login(string email, string password = Pwd) => new() { email = email, password = password };

    private static int _phoneSeq = 1000;

    /// <summary>The phone number is unique per account, so every call gets its own unless one is given.</summary>
    private static RegisterRequestDto Reg(string email, string password = Pwd, IFormFile? image = null, string? phone = null) => new()
    {
        firstName = "Rana", lastName = "Sami", email = email,
        phoneNumber = phone ?? $"059{Interlocked.Increment(ref _phoneSeq):D7}", password = password,
        address = "Main st", city = "Hebron", gender = Gender.Female, bloodType = BloodType.A_Positive,
        DateOfBith = new DateOnly(1995, 3, 4), ProfileImage = image
    };

    private static string NewEmail() => $"pat.{Guid.NewGuid():N}@test.local";

    private async Task<User> UserByEmailAsync(string email)
    {
        await using var db = Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.email == email);
    }

    private async Task<User> UserAsync(int userId)
    {
        await using var db = Db();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    private async Task<List<EmailVerificationCode>> CodesAsync(int userId)
    {
        await using var db = Db();
        return await db.EmailVerificationCodes.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.Id).ToListAsync();
    }

    private async Task<EmailVerificationCode> AddCodeAsync(int userId, string purpose = "register", bool used = false, int minutesLeft = 10)
    {
        await using var db = Db();
        var c = new EmailVerificationCode
        {
            UserId = userId, Code = "123456", Purpose = purpose, IsUsed = used, ExpiresAt = DateTime.UtcNow.AddMinutes(minutesLeft)
        };
        db.EmailVerificationCodes.Add(c);
        await db.SaveChangesAsync();
        return c;
    }

    private async Task<(int UserId, string Email)> AddPendingPatientAsync()
    {
        var email = NewEmail();
        var r = await NewAuth().RegisterAsync(C.Id, Reg(email));
        return (r.UserId, email);
    }

    private async Task<string> ErrorOfAsync(Func<Task> act) => (await Should.ThrowAsync<BusinessException>(act)).Message;

    // ---------- login ----------

    [Fact]
    public async Task An_active_patient_logs_in_and_the_token_says_who_they_are()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);

        var r = await NewAuth().LoginPatientAsync(C.Id, Login(email), "1.2.3.4");

        RoleOf(r.AccessToken).ShouldBe("Patient");
        UserIdOf(r.AccessToken).ShouldBe(id.ToString());
        (await TokensOfAsync(id)).ShouldHaveSingleItem().Token.ShouldBe(r.RefreshToken);
        var attempt = (await AttemptsOfAsync(email)).ShouldHaveSingleItem();
        attempt.Success.ShouldBeTrue();
        attempt.UserType.ShouldBe(LoginUserType.Patient);
    }

    [Fact]
    public async Task The_email_is_not_case_sensitive()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient);

        var r = await NewAuth().LoginPatientAsync(C.Id, Login(email.ToUpperInvariant()), null);

        r.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_recorded_as_a_failed_attempt()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient);

        await Should.ThrowAsync<BusinessException>(() => NewAuth().LoginPatientAsync(C.Id, Login(email, "wrong"), null));

        var attempt = (await AttemptsOfAsync(email)).ShouldHaveSingleItem();
        attempt.Success.ShouldBeFalse();
        attempt.UserType.ShouldBe(LoginUserType.Patient);
    }

    [Fact]
    public async Task A_patient_who_has_not_verified_the_email_cannot_log_in()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient, active: false);

        await Should.ThrowAsync<BusinessException>(() => NewAuth().LoginPatientAsync(C.Id, Login(email), null));
    }

    [Fact]
    public async Task Five_recent_failures_lock_the_patient_even_for_the_right_password()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient);
        await SeedAttemptsAsync(email, 5, type: LoginUserType.Patient);

        await ShouldFailWith("Auth.TooManyAttempts", () => NewAuth().LoginPatientAsync(C.Id, Login(email), null));
    }

    [Fact]
    public async Task Failures_on_the_staff_door_do_not_lock_the_patient_door()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient);
        await SeedAttemptsAsync(email, 5, type: LoginUserType.Staff);

        (await NewAuth().LoginPatientAsync(C.Id, Login(email), null)).AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Unknown_or_suspended_center_is_refused()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient);
        await ShouldFailWith("Center.NotFound", () => NewAuth().LoginPatientAsync(999_999, Login(email), null));

        await SetCenterStatusAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewAuth().LoginPatientAsync(C.Id, Login(email), null));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Wrong_password_and_unknown_email_give_the_same_error_so_emails_cannot_be_discovered()
    {
        // the staff door already does this; the patient door answers "Unauthorized" for an unknown email
        // but "PasswordMismatch" for a known one
        var (_, email) = await AddLoginUserAsync(userRole.Patient);

        var wrongPassword = await ErrorOfAsync(() => NewAuth().LoginPatientAsync(C.Id, Login(email, "wrong"), null));
        var unknown = await ErrorOfAsync(() => NewAuth().LoginPatientAsync(C.Id, Login(NewEmail()), null));

        wrongPassword.ShouldBe(unknown);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_staff_email_on_the_patient_door_looks_the_same_as_an_unknown_email()
    {
        // today the role check comes before the password check, so typing any password with a staff email says "Forbidden"
        var (_, staffEmail) = await AddLoginUserAsync(userRole.Doctor);

        var staff = await ErrorOfAsync(() => NewAuth().LoginPatientAsync(C.Id, Login(staffEmail, "any-password"), null));
        var unknown = await ErrorOfAsync(() => NewAuth().LoginPatientAsync(C.Id, Login(NewEmail(), "any-password"), null));

        staff.ShouldBe(unknown);
    }

    [Fact]
    public async Task A_staff_account_with_the_right_password_cannot_get_a_patient_token()
    {
        var (_, staffEmail) = await AddLoginUserAsync(userRole.Doctor);

        await Should.ThrowAsync<BusinessException>(() => NewAuth().LoginPatientAsync(C.Id, Login(staffEmail), null));
    }

    // ---------- register ----------

    [Fact]
    public async Task Register_creates_an_inactive_account_a_patient_row_and_mails_a_six_digit_code()
    {
        var email = NewEmail();

        var r = await NewAuth().RegisterAsync(C.Id, Reg(email, phone: "0591234567"));

        var u = await UserByEmailAsync(email);
        r.UserId.ShouldBe(u.Id);
        r.Email.ShouldBe(email);
        u.isActive.ShouldBeFalse();
        u.role.ShouldBe(userRole.Patient);
        u.firstName.ShouldBe("Rana");
        u.city.ShouldBe("Hebron");
        u.passwordHash.ShouldNotBe(Pwd);
        BCrypt.Net.BCrypt.Verify(Pwd, u.passwordHash).ShouldBeTrue();

        await using var db = Db();
        var p = await db.Patients.AsNoTracking().SingleAsync(x => x.userId == u.Id);
        p.NotifiableUserId.ShouldBe(u.Id);
        p.DateOfBith.ShouldBe(new DateOnly(1995, 3, 4));
        p.Gender.ShouldBe(Gender.Female);
        p.BloodType.ShouldBe(BloodType.A_Positive);
        p.WhatsappNumber.ShouldBe("0591234567");

        var code = (await CodesAsync(u.Id)).ShouldHaveSingleItem();
        code.Code.Length.ShouldBe(6);
        code.Purpose.ShouldBe("register");
        code.IsUsed.ShouldBeFalse();
        code.ExpiresAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow.AddMinutes(11));
        await Mail.Received(1).SendOtpAsync(email, code.Code, "register");
    }

    [Fact]
    public async Task An_email_that_is_already_registered_and_active_is_refused_and_nothing_is_created()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient);

        await ShouldFailWith("Auth.EmailExists", () => NewAuth().RegisterAsync(C.Id, Reg(email)));

        await using var db = Db();
        (await db.Users.CountAsync(u => u.email == email)).ShouldBe(1);
        await Mail.DidNotReceiveWithAnyArgs().SendOtpAsync(default!, default!, default!);
    }

    [Fact]
    public async Task Registering_again_before_verifying_updates_the_same_account_and_sends_a_new_code()
    {
        var (userId, email) = await AddPendingPatientAsync();

        var r = await NewAuth().RegisterAsync(C.Id, Reg(email, password: "An0ther!Pass"));

        r.UserId.ShouldBe(userId);
        await using var db = Db();
        (await db.Users.CountAsync(u => u.email == email)).ShouldBe(1);
        (await db.Patients.CountAsync(p => p.userId == userId)).ShouldBe(1);
        (await CodesAsync(userId)).Count.ShouldBe(2);
        await Mail.Received(2).SendOtpAsync(email, Arg.Any<string>(), "register");
    }

    [Fact]
    public async Task An_account_that_was_verified_and_later_deactivated_cannot_be_registered_again()
    {
        var (userId, email) = await AddPendingPatientAsync();
        await AddCodeAsync(userId, used: true);

        await ShouldFailWith("Auth.AccountDeactivated", () => NewAuth().RegisterAsync(C.Id, Reg(email)));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Registering_with_the_email_of_a_deactivated_staff_account_changes_nothing_on_it()
    {
        // staff created by the admin never verified a code, so a deactivated staff account looks like "pending registration"
        var staff = await C.AddDoctorAsync(active: false);
        var before = await UserAsync(staff.UserId);

        await Should.ThrowAsync<BusinessException>(() => NewAuth().RegisterAsync(C.Id, Reg(before.email)));

        var after = await UserAsync(staff.UserId);
        after.passwordHash.ShouldBe(before.passwordHash);
        after.firstName.ShouldBe(before.firstName);
        after.role.ShouldBe(userRole.Doctor);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_phone_number_that_belongs_to_another_account_is_a_clean_error()
    {
        // the phone number has a unique index; today the second person gets a raw DbUpdateException (HTTP 500)
        var (_, _) = await AddLoginUserAsync(userRole.Patient);
        await using var db = Db();
        var taken = await db.Users.AsNoTracking().OrderByDescending(u => u.Id).Select(u => u.phoneNumber).FirstAsync();

        await ShouldFailWith("Auth.PhoneExists", () => NewAuth().RegisterAsync(C.Id, Reg(NewEmail(), phone: taken)));
    }

    [Fact]
    public async Task Register_fails_for_an_unknown_or_suspended_center()
    {
        await ShouldFailWith("Center.NotFound", () => NewAuth().RegisterAsync(999_999, Reg(NewEmail())));

        await SetCenterStatusAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewAuth().RegisterAsync(C.Id, Reg(NewEmail())));
    }

    [Fact]
    public async Task A_profile_picture_is_saved_under_uploads()
    {
        var email = NewEmail();
        var image = new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "ProfileImage", "me.png");

        await NewAuth().RegisterAsync(C.Id, Reg(email, image: image));

        var url = (await UserByEmailAsync(email)).ProfileImageUrl;
        url.ShouldNotBeNull().ShouldStartWith("/uploads/profileImage/");
        url.ShouldEndWith(".png");
        var file = Path.Combine(AppContext.BaseDirectory, "wwwroot", url.TrimStart('/'));
        try { File.Exists(file).ShouldBeTrue(); }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    // ---------- verify the e-mail code ----------

    [Fact]
    public async Task The_right_code_activates_the_account_uses_the_code_up_and_returns_working_tokens()
    {
        var (userId, email) = await AddPendingPatientAsync();
        var code = (await CodesAsync(userId)).Single().Code;

        var r = await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = code });

        r.ShouldNotBeNull();
        RoleOf(r!.AccessToken).ShouldBe("Patient");
        UserIdOf(r.AccessToken).ShouldBe(userId.ToString());
        (await UserAsync(userId)).isActive.ShouldBeTrue();
        (await CodesAsync(userId)).Single().IsUsed.ShouldBeTrue();
        (await NewAuth().LoginPatientAsync(C.Id, Login(email), null)).AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_wrong_expired_or_used_code_gives_nothing_and_the_account_stays_inactive()
    {
        var (userId, _) = await AddPendingPatientAsync();
        var real = (await CodesAsync(userId)).Single().Code;
        var wrong = real == "000000" ? "111111" : "000000";

        (await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = wrong })).ShouldBeNull();

        await using (var db = Db())
        {
            var c = await db.EmailVerificationCodes.SingleAsync(x => x.UserId == userId);
            c.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        (await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = real })).ShouldBeNull();

        await using (var db = Db())
        {
            var c = await db.EmailVerificationCodes.SingleAsync(x => x.UserId == userId);
            c.ExpiresAt = DateTime.UtcNow.AddMinutes(5);
            c.IsUsed = true;
            await db.SaveChangesAsync();
        }
        (await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = real })).ShouldBeNull();

        (await UserAsync(userId)).isActive.ShouldBeFalse();
    }

    [Fact]
    public async Task A_code_works_only_once()
    {
        var (userId, _) = await AddPendingPatientAsync();
        var code = (await CodesAsync(userId)).Single().Code;
        await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = code });

        (await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = code })).ShouldBeNull();
    }

    [Fact]
    public async Task The_code_of_another_user_does_not_verify_this_user()
    {
        var (userA, _) = await AddPendingPatientAsync();
        var (userB, _) = await AddPendingPatientAsync();
        var codeOfA = (await CodesAsync(userA)).Single().Code;
        var codeOfB = (await CodesAsync(userB)).Single().Code;
        if (codeOfA == codeOfB) return;   // one in a million: same random code, nothing to prove

        (await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userB, code = codeOfA })).ShouldBeNull();
        (await UserAsync(userB)).isActive.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_user_gives_nothing()
    {
        (await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = 999_999, code = "123456" })).ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_password_reset_code_cannot_be_used_here_to_get_logged_in()
    {
        // the code lookup ignores Purpose: any live 6-digit code of the account (change-password, change-email...)
        // returns a full login without the password
        var (userId, _) = await AddLoginUserAsync(userRole.Patient);
        await AddCodeAsync(userId, purpose: "change-password");

        var r = await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = userId, code = "123456" });

        r.ShouldBeNull();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task This_door_never_switches_on_or_logs_in_a_staff_account()
    {
        var staff = await C.AddDoctorAsync(active: false);
        await AddCodeAsync(staff.UserId, purpose: "register");

        var r = await NewAuth().VerifyOtpAsync(C.Id, new VerifyOtpRequestDto { UserId = staff.UserId, code = "123456" });

        r.ShouldBeNull();
        (await UserAsync(staff.UserId)).isActive.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Verifying_in_an_unknown_center_is_a_clean_error()
    {
        await ShouldFailWith("Center.NotFound",
            () => NewAuth().VerifyOtpAsync(999_999, new VerifyOtpRequestDto { UserId = 1, code = "123456" }));
    }

    // ---------- deactivate / re-activate ----------

    [Fact]
    public async Task A_patient_can_deactivate_their_own_account_and_then_cannot_log_in()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);

        (await NewAuth().PatientDeactiveProfileAsync(C.Id, id)).ShouldNotBeNullOrWhiteSpace();

        (await UserAsync(id)).isActive.ShouldBeFalse();
        await Should.ThrowAsync<BusinessException>(() => NewAuth().LoginPatientAsync(C.Id, Login(email), null));
    }

    [Fact]
    public async Task Deactivating_an_unknown_or_already_inactive_account_fails()
    {
        var (inactive, _) = await AddLoginUserAsync(userRole.Patient, active: false);

        await ShouldFailWith("User.NotFound", () => NewAuth().PatientDeactiveProfileAsync(C.Id, inactive));
        await ShouldFailWith("User.NotFound", () => NewAuth().PatientDeactiveProfileAsync(C.Id, 999_999));
        await ShouldFailWith("Center.NotFound", () => NewAuth().PatientDeactiveProfileAsync(999_999, inactive));
    }

    [Fact]
    public async Task A_deactivated_patient_comes_back_with_email_and_password()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient, active: false);

        var r = await NewAuth().PatientActiveProfileAsync(C.Id, Login(email), "1.2.3.4");

        (await UserAsync(id)).isActive.ShouldBeTrue();
        UserIdOf(r.AccessToken).ShouldBe(id.ToString());
        (await AttemptsOfAsync(email)).ShouldHaveSingleItem().Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Re_activating_needs_the_right_password_and_a_deactivated_patient_account()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient, active: false);
        var (_, activeEmail) = await AddLoginUserAsync(userRole.Patient, active: true);
        var (_, staffEmail) = await AddLoginUserAsync(userRole.Doctor, active: false);

        await Should.ThrowAsync<BusinessException>(() => NewAuth().PatientActiveProfileAsync(C.Id, Login(email, "wrong"), null));
        await Should.ThrowAsync<BusinessException>(() => NewAuth().PatientActiveProfileAsync(C.Id, Login(activeEmail), null));   // already active
        await Should.ThrowAsync<BusinessException>(() => NewAuth().PatientActiveProfileAsync(C.Id, Login(staffEmail), null));    // staff cannot use this door

        (await UserAsync(id)).isActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Five_recent_failures_lock_re_activation_too()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Patient, active: false);
        await SeedAttemptsAsync(email, 5, type: LoginUserType.Patient);

        await ShouldFailWith("Auth.TooManyAttempts", () => NewAuth().PatientActiveProfileAsync(C.Id, Login(email), null));
    }
}
