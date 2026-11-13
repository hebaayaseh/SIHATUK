using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfile.EditSuperAdmin;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using ProfileSvc = Sehatak.Infrastructure.Services.SuperAdminService.SuperAdminAuth.SuperAdminProfileService;

namespace Sehatak.IntegrationTests.SuperAdmins;

/// <summary>SuperAdminProfileService: view / edit name / picture, and change email or password with a mailed 6-digit code.</summary>
[Collection(SqlServerCollection.Name)]
public class SuperAdminProfileTests(SqlServerFixture fx) : SuperAdminTestBase(fx)
{
    private readonly IEmailService _mail = Substitute.For<IEmailService>();

    private ProfileSvc NewSvc() => new(Shared(), _mail);

    private static FormFile Image(string name, long length = 10) =>
        new(new MemoryStream(new byte[length]), 0, length, "ImageFile", name);

    private static void EnsureUploadFolder() => Directory.CreateDirectory(Path.Combine("wwwroot", "uploads", "profileImage"));

    private static void DeleteUpload(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        var p = Path.Combine("wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
    }

    // ---------- view ----------

    [Fact]
    public async Task View_profile_returns_the_saved_details()
    {
        var sa = await AddSuperAdminAsync(name: "Dina Owner");

        var r = await NewSvc().ViewProfile(sa.Id);

        r.Name.ShouldBe("Dina Owner");
        r.Email.ShouldBe(sa.Email);
        r.Phone.ShouldBe(sa.phone);
        r.ProfileImage.ShouldBeNull();
    }

    [Fact]
    public async Task Every_method_refuses_an_unknown_super_admin()
    {
        await ShouldFailWith("Auth.Unauthorized", () => NewSvc().ViewProfile(987654));
        await ShouldFailWith("Auth.Unauthorized", () => NewSvc().EditName(987654, new() { Name = "X" }));
        await ShouldFailWith("Auth.Unauthorized", () => NewSvc().EditProfileImage(987654, new() { ImageFile = Image("a.png") }));
        await ShouldFailWith("Auth.Unauthorized", () => NewSvc().RequestEditEmail(987654, new() { Email = "x@test.local" }));
        await ShouldFailWith("Auth.Unauthorized", () => NewSvc().ConfirmEditEmail(987654, new() { Code = "123456" }));
        await ShouldFailWith("Auth.Unauthorized", () => NewSvc().RequestEditPassword(987654, new() { PasswordHash = "N3w!Passw0rd", ConfirmPassword = "N3w!Passw0rd" }));
        await ShouldFailWith("Auth.Unauthorized", () => NewSvc().ConfirmEditPassword(987654, new() { Code = "123456" }));
    }

    // ---------- name ----------

    [Fact]
    public async Task Edit_name_saves_the_new_name()
    {
        var sa = await AddSuperAdminAsync(name: "Old Name");

        var r = await NewSvc().EditName(sa.Id, new() { Name = "New Name" });

        r.Name.ShouldBe("New Name");
        (await ReloadAsync(sa.Id)).Name.ShouldBe("New Name");
    }

    [Fact]
    public async Task Editing_to_the_same_name_is_refused_and_changes_nothing()
    {
        var sa = await AddSuperAdminAsync(name: "Same Name");

        await Should.ThrowAsync<BusinessException>(() => NewSvc().EditName(sa.Id, new() { Name = "Same Name" }));

        (await ReloadAsync(sa.Id)).Name.ShouldBe("Same Name");
    }

    [Fact]
    public async Task One_super_admin_cannot_change_another_ones_name()
    {
        var a = await AddSuperAdminAsync(name: "Admin A");
        var b = await AddSuperAdminAsync(name: "Admin B");

        await NewSvc().EditName(a.Id, new() { Name = "Renamed" });

        (await ReloadAsync(b.Id)).Name.ShouldBe("Admin B");
    }

    // ---------- picture ----------

    [Theory]
    [InlineData("me.png")]
    [InlineData("me.jpg")]
    [InlineData("me.jpeg")]
    [InlineData("ME.PNG")]
    public async Task A_png_or_jpeg_picture_is_saved_and_its_url_returned(string fileName)
    {
        EnsureUploadFolder();
        var sa = await AddSuperAdminAsync();

        var r = await NewSvc().EditProfileImage(sa.Id, new() { ImageFile = Image(fileName) });

        try
        {
            r.profileImage.ShouldStartWith("/uploads/profileImage/");
            (await ReloadAsync(sa.Id)).ProfileImageUrl.ShouldBe(r.profileImage);
            System.IO.File.Exists(Path.Combine("wwwroot", r.profileImage.TrimStart('/'))).ShouldBeTrue();
            (await NewSvc().ViewProfile(sa.Id)).ProfileImage.ShouldBe(r.profileImage);
        }
        finally { DeleteUpload(r.profileImage); }
    }

    [Theory]
    [InlineData("me.gif")]
    [InlineData("me.html")]
    [InlineData("me.svg")]
    [InlineData("me")]
    public async Task Other_file_types_are_refused(string fileName)
    {
        var sa = await AddSuperAdminAsync();

        await ShouldFailWith("Validation.InvalidFileType", () => NewSvc().EditProfileImage(sa.Id, new() { ImageFile = Image(fileName) }));

        (await ReloadAsync(sa.Id)).ProfileImageUrl.ShouldBeNull();
    }

    [Fact]
    public async Task The_size_limit_is_5_MB_inclusive()
    {
        EnsureUploadFolder();
        var sa = await AddSuperAdminAsync();

        await ShouldFailWith("Validation.FileTooLarge", () => NewSvc().EditProfileImage(sa.Id, new() { ImageFile = Image("big.png", 5 * 1024 * 1024 + 1) }));
        (await ReloadAsync(sa.Id)).ProfileImageUrl.ShouldBeNull();

        var ok = await NewSvc().EditProfileImage(sa.Id, new() { ImageFile = Image("edge.png", 5 * 1024 * 1024) });
        DeleteUpload(ok.profileImage);
        ok.profileImage.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_missing_file_is_refused()
    {
        var sa = await AddSuperAdminAsync();

        await ShouldFailWith("General.NotFound", () => NewSvc().EditProfileImage(sa.Id, new() { ImageFile = null! }));
    }

    // ---------- change email: request ----------

    [Fact]
    public async Task Requesting_an_email_change_stores_a_six_digit_code_and_mails_it()
    {
        var sa = await AddSuperAdminAsync();
        var newEmail = NewEmail();

        (await NewSvc().RequestEditEmail(sa.Id, new() { Email = newEmail })).ShouldBeTrue();

        var row = (await CodesOfAsync(sa.Id, "change-email")).Single();
        row.Code.Length.ShouldBe(6);
        row.Code.ShouldAllBe(ch => char.IsDigit(ch));
        row.PendingValue.ShouldBe(newEmail);
        row.IsUsed.ShouldBeFalse();
        row.ExpiresAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(8), DateTime.UtcNow.AddMinutes(11));
        (await ReloadAsync(sa.Id)).Email.ShouldBe(sa.Email);
        await _mail.Received(1).SendOtpAsync(sa.Email, row.Code, "change-email");
    }

    [Fact]
    public async Task An_email_of_another_super_admin_cannot_be_requested()
    {
        var a = await AddSuperAdminAsync();
        var b = await AddSuperAdminAsync();

        await ShouldFailWith("Auth.EmailExists", () => NewSvc().RequestEditEmail(a.Id, new() { Email = b.Email }));

        (await CodesOfAsync(a.Id, "change-email")).ShouldBeEmpty();
        await _mail.DidNotReceive().SendOtpAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------- change email: confirm ----------

    [Fact]
    public async Task The_right_code_changes_the_email_and_cannot_be_used_twice()
    {
        var sa = await AddSuperAdminAsync();
        var newEmail = NewEmail();
        await AddCodeAsync(sa.Id, "246810", "change-email", newEmail);

        var r = await NewSvc().ConfirmEditEmail(sa.Id, new() { Code = "246810" });

        r.Email.ShouldBe(newEmail);
        (await ReloadAsync(sa.Id)).Email.ShouldBe(newEmail);
        (await CodesOfAsync(sa.Id, "change-email")).Single().IsUsed.ShouldBeTrue();

        await ShouldFailWith("Verfiy.Code", () => NewSvc().ConfirmEditEmail(sa.Id, new() { Code = "246810" }));
    }

    [Fact]
    public async Task A_wrong_expired_used_or_foreign_code_does_not_change_the_email()
    {
        var sa = await AddSuperAdminAsync();
        var other = await AddSuperAdminAsync();
        await AddCodeAsync(sa.Id, "111111", "change-email", NewEmail(), expiresInMinutes: -1);
        await AddCodeAsync(sa.Id, "222222", "change-email", NewEmail(), used: true);
        await AddCodeAsync(other.Id, "333333", "change-email", NewEmail());
        await AddCodeAsync(sa.Id, "444444", "change-password", NewEmail());
        await AddCodeAsync(sa.Id, "555555", "change-email", null);

        foreach (var code in new[] { "000000", "111111", "222222", "333333", "444444", "555555" })
            await ShouldFailWith("Verfiy.Code", () => NewSvc().ConfirmEditEmail(sa.Id, new() { Code = code }));

        (await ReloadAsync(sa.Id)).Email.ShouldBe(sa.Email);
        (await ReloadAsync(other.Id)).Email.ShouldBe(other.Email);
    }

    /// <summary>Two requests in a row leave two live codes: the older code still works and sets the OLD pending email.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_new_email_request_cancels_the_previous_code()
    {
        var sa = await AddSuperAdminAsync();
        var first = NewEmail();
        var second = NewEmail();
        await NewSvc().RequestEditEmail(sa.Id, new() { Email = first });
        var firstCode = (await CodesOfAsync(sa.Id, "change-email")).Single().Code;
        await NewSvc().RequestEditEmail(sa.Id, new() { Email = second });

        await ShouldFailWith("Verfiy.Code", () => NewSvc().ConfirmEditEmail(sa.Id, new() { Code = firstCode }));

        (await ReloadAsync(sa.Id)).Email.ShouldBe(sa.Email);
    }

    /// <summary>The "email exists" check only runs at request time. If someone else takes the address in between, confirm hits the unique index (raw DbUpdateException).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task An_email_taken_after_the_request_is_a_clean_error_at_confirm()
    {
        var sa = await AddSuperAdminAsync();
        var wanted = NewEmail();
        await AddCodeAsync(sa.Id, "135790", "change-email", wanted);
        await AddSuperAdminAsync(email: wanted);

        await ShouldFailWith("Auth.EmailExists", () => NewSvc().ConfirmEditEmail(sa.Id, new() { Code = "135790" }));

        (await ReloadAsync(sa.Id)).Email.ShouldBe(sa.Email);
    }

    // ---------- change password: request ----------

    [Fact]
    public async Task Requesting_a_password_change_stores_a_hash_not_the_password_and_mails_a_code()
    {
        var sa = await AddSuperAdminAsync();

        (await NewSvc().RequestEditPassword(sa.Id, new() { PasswordHash = "N3w!Passw0rd", ConfirmPassword = "N3w!Passw0rd" })).ShouldBeTrue();

        var row = (await CodesOfAsync(sa.Id, "change-password")).Single();
        row.PendingValue.ShouldNotBe("N3w!Passw0rd");
        BCrypt.Net.BCrypt.Verify("N3w!Passw0rd", row.PendingValue!).ShouldBeTrue();
        row.Code.Length.ShouldBe(6);
        row.ExpiresAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(8), DateTime.UtcNow.AddMinutes(11));
        BCrypt.Net.BCrypt.Verify(Pwd, (await ReloadAsync(sa.Id)).PasswordHash).ShouldBeTrue();
        await _mail.Received(1).SendOtpAsync(sa.Email, row.Code, "change-password");
    }

    [Fact]
    public async Task A_mismatch_or_the_same_password_is_refused_without_a_code()
    {
        var sa = await AddSuperAdminAsync();

        await ShouldFailWith("Validation.PasswordMismatch", () => NewSvc().RequestEditPassword(sa.Id, new() { PasswordHash = "N3w!Passw0rd", ConfirmPassword = "Other!Passw0rd" }));
        await ShouldFailWith("Validation.SamePassword", () => NewSvc().RequestEditPassword(sa.Id, new() { PasswordHash = Pwd, ConfirmPassword = Pwd }));

        (await CodesOfAsync(sa.Id, "change-password")).ShouldBeEmpty();
        await _mail.DidNotReceive().SendOtpAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------- change password: confirm ----------

    [Fact]
    public async Task The_right_code_sets_the_new_password_and_cannot_be_used_twice()
    {
        var sa = await AddSuperAdminAsync();
        await NewSvc().RequestEditPassword(sa.Id, new() { PasswordHash = "N3w!Passw0rd", ConfirmPassword = "N3w!Passw0rd" });
        var code = (await CodesOfAsync(sa.Id, "change-password")).Single().Code;

        var r = await NewSvc().ConfirmEditPassword(sa.Id, new() { Code = code });

        r.message.ShouldNotBeNullOrWhiteSpace();
        var hash = (await ReloadAsync(sa.Id)).PasswordHash;
        BCrypt.Net.BCrypt.Verify("N3w!Passw0rd", hash).ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(Pwd, hash).ShouldBeFalse();
        await ShouldFailWith("Verfiy.Code", () => NewSvc().ConfirmEditPassword(sa.Id, new() { Code = code }));
    }

    [Fact]
    public async Task A_wrong_expired_used_or_foreign_code_does_not_change_the_password()
    {
        var sa = await AddSuperAdminAsync();
        var other = await AddSuperAdminAsync();
        var h = BCrypt.Net.BCrypt.HashPassword("N3w!Passw0rd");
        await AddCodeAsync(sa.Id, "111111", "change-password", h, expiresInMinutes: -1);
        await AddCodeAsync(sa.Id, "222222", "change-password", h, used: true);
        await AddCodeAsync(other.Id, "333333", "change-password", h);
        await AddCodeAsync(sa.Id, "444444", "change-email", h);
        await AddCodeAsync(sa.Id, "555555", "change-password", null);

        foreach (var code in new[] { "000000", "111111", "222222", "333333", "444444", "555555" })
            await ShouldFailWith("Verfiy.Code", () => NewSvc().ConfirmEditPassword(sa.Id, new() { Code = code }));

        BCrypt.Net.BCrypt.Verify(Pwd, (await ReloadAsync(sa.Id)).PasswordHash).ShouldBeTrue();
        BCrypt.Net.BCrypt.Verify(Pwd, (await ReloadAsync(other.Id)).PasswordHash).ShouldBeTrue();
    }

    [Fact]
    public async Task After_the_change_the_new_password_logs_in_and_the_old_one_does_not()
    {
        var sa = await AddSuperAdminAsync();
        await NewSvc().RequestEditPassword(sa.Id, new() { PasswordHash = "N3w!Passw0rd", ConfirmPassword = "N3w!Passw0rd" });
        await NewSvc().ConfirmEditPassword(sa.Id, new() { Code = (await CodesOfAsync(sa.Id, "change-password")).Single().Code });
        var shared = Shared();
        var auth = new Sehatak.Infrastructure.Services.SuperAdminService.SuperAdminAuth.SuperAdminAuthService(shared, NewTokens(shared), NewConfig());

        (await auth.LoginAsync(new() { email = sa.Email, password = "N3w!Passw0rd" })).ShouldNotBeNull();
        (await auth.LoginAsync(new() { email = sa.Email, password = Pwd })).ShouldBeNull();
    }

    /// <summary>If the password was changed because it leaked, the thief's refresh token keeps working for 7 days: nothing revokes the existing tokens.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Changing_the_password_signs_out_the_other_sessions()
    {
        var sa = await AddSuperAdminAsync();
        var shared = Shared();
        var tokens = await NewTokens(shared).IssueTokensAsync(sa.Id, sa.Name, sa.Email, "SuperAdmin", null, TokenOwnerType.SuperAdmin);
        await NewSvc().RequestEditPassword(sa.Id, new() { PasswordHash = "N3w!Passw0rd", ConfirmPassword = "N3w!Passw0rd" });

        await NewSvc().ConfirmEditPassword(sa.Id, new() { Code = (await CodesOfAsync(sa.Id, "change-password")).Single().Code });

        await using var db = Shared();
        (await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Token == tokens.RefreshToken)).IsRevoked.ShouldBeTrue();
    }
}
