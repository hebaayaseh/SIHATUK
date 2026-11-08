using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Application.DTOs.EditProfileDto.EditEmailOrPasswored;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Patients;

/// <summary>Patient profile edit, e-mail change, password change and "forgot password".</summary>
[Collection(SqlServerCollection.Name)]
public class PatientProfileServiceTests(SqlServerFixture fx) : PatientsTestBase(fx)
{
    private const string OldPassword = "Old@12345";
    private const string NewPassword = "New@12345";

    // ---------- edit information ----------

    [Fact]
    public async Task Edit_changes_only_the_fields_that_were_sent()
    {
        var before = await UserAsync(Pat.UserId);

        var r = await NewPatientProfile().EditPatientInformation(C.Id, Pat.UserId,
            new EditPatientInformationRequest { firstNmae = "Rana", city = "Nablus" });

        var after = await UserAsync(Pat.UserId);
        after.firstName.ShouldBe("Rana");
        after.city.ShouldBe("Nablus");
        after.lastName.ShouldBe(before.lastName);
        after.address.ShouldBe(before.address);
        after.phoneNumber.ShouldBe(before.phoneNumber);
        r.FullName.ShouldBe($"Rana {before.lastName}");
        r.City.ShouldBe("Nablus");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_edit_response_says_which_patient_was_edited()
    {
        var r = await NewPatientProfile().EditPatientInformation(C.Id, Pat.UserId, new EditPatientInformationRequest { city = "Nablus" });

        r.PatientId.ShouldBe(Pat.PatientId);   // never filled: always 0
    }

    [Fact]
    public async Task A_picture_that_is_not_jpg_or_png_is_refused_and_nothing_is_saved()
    {
        var before = await UserAsync(Pat.UserId);

        await ShouldFailWith("Validation.InvalidFileType", () => NewPatientProfile().EditPatientInformation(C.Id, Pat.UserId,
            new EditPatientInformationRequest { firstNmae = "Changed", profileImage = Image("virus.exe") }));

        (await UserAsync(Pat.UserId)).firstName.ShouldBe(before.firstName);
    }

    [Fact]
    public async Task A_picture_over_five_megabytes_is_refused()
    {
        await ShouldFailWith("Validation.FileTooLarge", () => NewPatientProfile().EditPatientInformation(C.Id, Pat.UserId,
            new EditPatientInformationRequest { profileImage = Image("big.png", claimedLength: 5 * 1024 * 1024 + 1) }));
    }

    [Fact]
    public async Task A_png_is_stored_under_uploads_and_the_old_picture_file_is_removed()
    {
        var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "profileImage");
        Directory.CreateDirectory(folder);
        var oldName = $"old-{Guid.NewGuid():N}.png";
        var oldFile = Path.Combine(folder, oldName);
        await File.WriteAllBytesAsync(oldFile, new byte[] { 1 });
        await SetProfileImageAsync(Pat.UserId, $"/uploads/profileImage/{oldName}");
        string? newFile = null;
        try
        {
            var r = await NewPatientProfile().EditPatientInformation(C.Id, Pat.UserId,
                new EditPatientInformationRequest { profileImage = Image("me.PNG") });

            r.ProfileImageUrl.ShouldNotBeNull().ShouldStartWith("/uploads/profileImage/");
            r.ProfileImageUrl.ShouldEndWith(".PNG");
            newFile = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", r.ProfileImageUrl.TrimStart('/'));
            File.Exists(newFile).ShouldBeTrue();
            File.Exists(oldFile).ShouldBeFalse();
            (await UserAsync(Pat.UserId)).ProfileImageUrl.ShouldBe(r.ProfileImageUrl);
        }
        finally
        {
            if (File.Exists(oldFile)) File.Delete(oldFile);
            if (newFile != null && File.Exists(newFile)) File.Delete(newFile);
        }
    }

    [Fact]
    public async Task The_remove_flag_clears_the_picture()
    {
        await SetProfileImageAsync(Pat.UserId, "/uploads/profileImage/missing-file.png");

        var r = await NewPatientProfile().EditPatientInformation(C.Id, Pat.UserId,
            new EditPatientInformationRequest { RemoveProfileImage = true });

        r.ProfileImageUrl.ShouldBeNull();
        (await UserAsync(Pat.UserId)).ProfileImageUrl.ShouldBeNull();
    }

    [Fact]
    public async Task A_request_without_a_picture_keeps_the_existing_one()
    {
        await SetProfileImageAsync(Pat.UserId, "/uploads/profileImage/keep.png");

        await NewPatientProfile().EditPatientInformation(C.Id, Pat.UserId, new EditPatientInformationRequest { city = "Jenin" });

        (await UserAsync(Pat.UserId)).ProfileImageUrl.ShouldBe("/uploads/profileImage/keep.png");
    }

    [Fact]
    public async Task Edit_and_view_fail_for_deactivated_users_and_unavailable_centers()
    {
        var gone = await C.AddPatientAsync(active: false);

        await ShouldFailWith("User.NotFound", () => NewPatientProfile().EditPatientInformation(C.Id, gone.UserId, new EditPatientInformationRequest()));
        await ShouldFailWith("User.NotFound", () => NewPatientProfile().ViewPatientInformation(C.Id, gone.UserId));
        await ShouldFailWith("Center.NotFound", () => NewPatientProfile().EditPatientInformation(999_999, Pat.UserId, new EditPatientInformationRequest()));
        await ShouldFailWith("Center.NotFound", () => NewPatientProfile().ViewPatientInformation(999_999, Pat.UserId));
    }

    [Fact]
    public async Task View_returns_the_name_contact_and_picture()
    {
        await SetProfileImageAsync(Pat.UserId, "/uploads/profileImage/p.png");
        var u = await UserAsync(Pat.UserId);

        var r = await NewPatientProfile().ViewPatientInformation(C.Id, Pat.UserId);

        r.FullName.ShouldBe($"{u.firstName} {u.lastName}");
        r.ProfileImageUrl.ShouldBe("/uploads/profileImage/p.png");
        r.Address.ShouldBe("addr");
        r.City.ShouldBe("Hebron");
        r.PhoneNumber.ShouldBe(u.phoneNumber);
    }

    // ---------- change e-mail ----------

    [Fact]
    public async Task Requesting_an_email_change_stores_a_ten_minute_code_and_mails_it_to_the_current_address()
    {
        var u = await UserAsync(Pat.UserId);

        (await NewPatientProfile().RequestEditEmail(C.Id, Pat.UserId, new EditEmailRequest { Email = "new@test.local" })).ShouldBeTrue();

        var code = await LastCodeAsync(Pat.UserId, "change-email");
        code.PendingValue.ShouldBe("new@test.local");
        code.IsUsed.ShouldBeFalse();
        code.Code.Length.ShouldBe(6);
        code.ExpiresAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(9), DateTime.UtcNow.AddMinutes(11));
        await Email.Received(1).SendOtpAsync(u.email, code.Code, "change-email");
        (await UserAsync(Pat.UserId)).email.ShouldBe(u.email);   // not changed until confirmed
    }

    [Fact]
    public async Task An_address_that_is_already_used_is_refused_in_any_letter_case_and_nothing_is_mailed()
    {
        var other = await UserAsync((await C.AddPatientAsync()).UserId);

        await ShouldFailWith("Email.AlreadyExists", () => NewPatientProfile().RequestEditEmail(C.Id, Pat.UserId, new EditEmailRequest { Email = other.email }));
        await ShouldFailWith("Email.AlreadyExists", () => NewPatientProfile().RequestEditEmail(C.Id, Pat.UserId, new EditEmailRequest { Email = other.email.ToUpperInvariant() }));

        (await CodeCountAsync(Pat.UserId)).ShouldBe(0);
        await Email.DidNotReceiveWithAnyArgs().SendOtpAsync(default!, default!, default!);
    }

    [Fact]
    public async Task The_right_code_changes_the_email_once()
    {
        await NewPatientProfile().RequestEditEmail(C.Id, Pat.UserId, new EditEmailRequest { Email = "new@test.local" });
        var code = (await LastCodeAsync(Pat.UserId, "change-email")).Code;

        var r = await NewPatientProfile().ConfirmEditEmail(C.Id, Pat.UserId, new ConfirmEditEmailRequest { Code = code });

        r.Email.ShouldBe("new@test.local");
        (await UserAsync(Pat.UserId)).email.ShouldBe("new@test.local");
        await ShouldFailWith("Verfiy.Code", () => NewPatientProfile().ConfirmEditEmail(C.Id, Pat.UserId, new ConfirmEditEmailRequest { Code = code }));
    }

    [Fact]
    public async Task A_wrong_or_expired_code_does_not_change_the_email()
    {
        var before = (await UserAsync(Pat.UserId)).email;
        await NewPatientProfile().RequestEditEmail(C.Id, Pat.UserId, new EditEmailRequest { Email = "new@test.local" });
        var row = await LastCodeAsync(Pat.UserId, "change-email");
        var wrong = row.Code == "000000" ? "111111" : "000000";

        await ShouldFailWith("Verfiy.Code", () => NewPatientProfile().ConfirmEditEmail(C.Id, Pat.UserId, new ConfirmEditEmailRequest { Code = wrong }));

        await ExpireCodeAsync(row.Id);
        await ShouldFailWith("Verfiy.Code", () => NewPatientProfile().ConfirmEditEmail(C.Id, Pat.UserId, new ConfirmEditEmailRequest { Code = row.Code }));
        (await UserAsync(Pat.UserId)).email.ShouldBe(before);
    }

    [Fact]
    public async Task A_code_that_belongs_to_someone_else_is_useless()
    {
        var other = await C.AddPatientAsync();
        await NewPatientProfile().RequestEditEmail(C.Id, Pat.UserId, new EditEmailRequest { Email = "mine@test.local" });
        var code = (await LastCodeAsync(Pat.UserId, "change-email")).Code;

        await ShouldFailWith("Verfiy.Code", () => NewPatientProfile().ConfirmEditEmail(C.Id, other.UserId, new ConfirmEditEmailRequest { Code = code }));
    }

    [Fact]
    public async Task A_password_code_cannot_confirm_an_email_change()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);
        await NewPatientProfile().RequestEditPassword(C.Id, Pat.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword });
        var pwCode = (await LastCodeAsync(Pat.UserId, "change-password")).Code;

        await ShouldFailWith("Verfiy.Code", () => NewPatientProfile().ConfirmEditEmail(C.Id, Pat.UserId, new ConfirmEditEmailRequest { Code = pwCode }));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task If_someone_takes_the_address_before_the_code_is_confirmed_the_user_gets_a_clean_error()
    {
        // two accounts ask for the same new address; the first confirms, the second hits the unique index
        await NewPatientProfile().RequestEditEmail(C.Id, Pat.UserId, new EditEmailRequest { Email = "popular@test.local" });
        var code = (await LastCodeAsync(Pat.UserId, "change-email")).Code;
        var other = await C.AddPatientAsync();
        await using (var db = Db())
        {
            var o = await db.Users.SingleAsync(u => u.Id == other.UserId);
            o.email = "popular@test.local";
            await db.SaveChangesAsync();
        }

        await ShouldFailWith("Email.AlreadyExists", () => NewPatientProfile().ConfirmEditEmail(C.Id, Pat.UserId, new ConfirmEditEmailRequest { Code = code }));
    }

    // ---------- change password (signed in) ----------

    [Fact]
    public async Task A_password_request_stores_only_a_hash_and_mails_a_code()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);

        await NewPatientProfile().RequestEditPassword(C.Id, Pat.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword });

        var row = await LastCodeAsync(Pat.UserId, "change-password");
        row.PendingValue.ShouldNotBe(NewPassword);
        BCrypt.Net.BCrypt.Verify(NewPassword, row.PendingValue).ShouldBeTrue();
        await Email.Received(1).SendOtpAsync(Arg.Any<string>(), row.Code, "change-password");
    }

    [Fact]
    public async Task The_code_sets_the_new_password_and_the_old_one_stops_working()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);
        await NewPatientProfile().RequestEditPassword(C.Id, Pat.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword });
        var code = (await LastCodeAsync(Pat.UserId, "change-password")).Code;

        await NewPatientProfile().ConfirmEditPassword(C.Id, Pat.UserId, new ConfirmEditPasswordRequest { Code = code });

        var hash = (await UserAsync(Pat.UserId)).passwordHash;
        BCrypt.Net.BCrypt.Verify(NewPassword, hash).ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(OldPassword, hash).ShouldBeFalse();
        await ShouldFailWith("Verfiy.Code", () => NewPatientProfile().ConfirmEditPassword(C.Id, Pat.UserId, new ConfirmEditPasswordRequest { Code = code }));
    }

    [Fact]
    public async Task A_mismatched_or_unchanged_password_is_refused_before_any_code_is_sent()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);

        await ShouldFailWith("Validation.PasswordMismatch", () => NewPatientProfile().RequestEditPassword(C.Id, Pat.UserId,
            new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = "Other@12345" }));
        await ShouldFailWith("Validation.SamePassword", () => NewPatientProfile().RequestEditPassword(C.Id, Pat.UserId,
            new EditPasswordRequest { PasswordHash = OldPassword, ConfirmPassword = OldPassword }));

        (await CodeCountAsync(Pat.UserId)).ShouldBe(0);
        await Email.DidNotReceiveWithAnyArgs().SendOtpAsync(default!, default!, default!);
    }

    [Fact]
    public async Task An_expired_password_code_does_not_change_the_password()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);
        await NewPatientProfile().RequestEditPassword(C.Id, Pat.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword });
        var row = await LastCodeAsync(Pat.UserId, "change-password");
        await ExpireCodeAsync(row.Id);

        await ShouldFailWith("Verfiy.Code", () => NewPatientProfile().ConfirmEditPassword(C.Id, Pat.UserId, new ConfirmEditPasswordRequest { Code = row.Code }));

        BCrypt.Net.BCrypt.Verify(OldPassword, (await UserAsync(Pat.UserId)).passwordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Password_and_email_requests_fail_for_deactivated_users()
    {
        var gone = await C.AddPatientAsync(active: false);

        await ShouldFailWith("User.NotFound", () => NewPatientProfile().RequestEditEmail(C.Id, gone.UserId, new EditEmailRequest { Email = "x@test.local" }));
        await ShouldFailWith("User.NotFound", () => NewPatientProfile().RequestEditPassword(C.Id, gone.UserId, new EditPasswordRequest { PasswordHash = NewPassword, ConfirmPassword = NewPassword }));
        await ShouldFailWith("User.NotFound", () => NewPatientProfile().ConfirmEditEmail(C.Id, gone.UserId, new ConfirmEditEmailRequest { Code = "123456" }));
        await ShouldFailWith("User.NotFound", () => NewPatientProfile().ConfirmEditPassword(C.Id, gone.UserId, new ConfirmEditPasswordRequest { Code = "123456" }));
    }

    // ---------- forgot password (not signed in) ----------

    [Fact]
    public async Task Forgot_password_mails_a_code_and_the_code_sets_the_new_password()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);
        var email = (await UserAsync(Pat.UserId)).email;

        await NewPatientProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = email, newPassword = NewPassword, confirmPassword = NewPassword });

        var row = await LastCodeAsync(Pat.UserId, "change-password");
        BCrypt.Net.BCrypt.Verify(NewPassword, row.PendingValue).ShouldBeTrue();    // never the plain password
        await Email.Received(1).SendOtpAsync(email, row.Code, "change-password");

        await NewPatientProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = row.Code });

        BCrypt.Net.BCrypt.Verify(NewPassword, (await UserAsync(Pat.UserId)).passwordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Forgot_password_checks_the_two_passwords_and_the_account()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);
        var email = (await UserAsync(Pat.UserId)).email;
        var gone = await UserAsync((await C.AddPatientAsync(active: false)).UserId);

        await ShouldFailWith("Validation.PasswordMismatch", () => NewPatientProfile().ForgetPasswordAsync(C.Id,
            new ForgetPasswordRequest { Email = email, newPassword = NewPassword, confirmPassword = "Other@12345" }));
        await ShouldFailWith("Validation.SamePassword", () => NewPatientProfile().ForgetPasswordAsync(C.Id,
            new ForgetPasswordRequest { Email = email, newPassword = OldPassword, confirmPassword = OldPassword }));
        await ShouldFailWith("User.NotFound", () => NewPatientProfile().ForgetPasswordAsync(C.Id,
            new ForgetPasswordRequest { Email = "nobody@test.local", newPassword = NewPassword, confirmPassword = NewPassword }));
        await ShouldFailWith("User.NotFound", () => NewPatientProfile().ForgetPasswordAsync(C.Id,
            new ForgetPasswordRequest { Email = gone.email, newPassword = NewPassword, confirmPassword = NewPassword }));
        await ShouldFailWith("Center.NotFound", () => NewPatientProfile().ForgetPasswordAsync(999_999,
            new ForgetPasswordRequest { Email = email, newPassword = NewPassword, confirmPassword = NewPassword }));

        (await CodeCountAsync(Pat.UserId)).ShouldBe(0);
    }

    [Fact]
    public async Task A_code_sent_to_one_account_cannot_reset_another_account()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);
        var victim = await C.AddPatientAsync();
        await SetPasswordAsync(victim.UserId, OldPassword);
        var victimEmail = (await UserAsync(victim.UserId)).email;
        var myEmail = (await UserAsync(Pat.UserId)).email;
        await NewPatientProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = myEmail, newPassword = NewPassword, confirmPassword = NewPassword });
        var myCode = (await LastCodeAsync(Pat.UserId, "change-password")).Code;

        await ShouldFailWith("Verify.Code", () => NewPatientProfile().ConfirmForgetPassword(C.Id,
            new ConfirmForgetPasswordRequest { Email = victimEmail, Code = myCode }));

        BCrypt.Net.BCrypt.Verify(OldPassword, (await UserAsync(victim.UserId)).passwordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task Confirm_forgot_password_refuses_a_wrong_used_or_expired_code()
    {
        await SetPasswordAsync(Pat.UserId, OldPassword);
        var email = (await UserAsync(Pat.UserId)).email;
        await NewPatientProfile().ForgetPasswordAsync(C.Id, new ForgetPasswordRequest { Email = email, newPassword = NewPassword, confirmPassword = NewPassword });
        var row = await LastCodeAsync(Pat.UserId, "change-password");
        var wrong = row.Code == "000000" ? "111111" : "000000";

        await ShouldFailWith("Verify.Code", () => NewPatientProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = wrong }));

        await ExpireCodeAsync(row.Id);
        await ShouldFailWith("Verify.Code", () => NewPatientProfile().ConfirmForgetPassword(C.Id, new ConfirmForgetPasswordRequest { Email = email, Code = row.Code }));
        BCrypt.Net.BCrypt.Verify(OldPassword, (await UserAsync(Pat.UserId)).passwordHash).ShouldBeTrue();
    }
}
