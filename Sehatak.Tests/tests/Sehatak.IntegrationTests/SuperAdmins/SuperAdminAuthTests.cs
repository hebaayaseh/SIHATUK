using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.SuperAdminDto;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using AuthSvc = Sehatak.Infrastructure.Services.SuperAdminService.SuperAdminAuth.SuperAdminAuthService;

namespace Sehatak.IntegrationTests.SuperAdmins;

/// <summary>SuperAdminAuthService: login for the platform owner and the setup-key protected registration.</summary>
[Collection(SqlServerCollection.Name)]
public class SuperAdminAuthTests(SqlServerFixture fx) : SuperAdminTestBase(fx)
{
    private AuthSvc NewAuth(string? setupKey = SetupKey)
    {
        var shared = Shared();
        return new AuthSvc(shared, NewTokens(shared), NewConfig(setupKey));
    }

    private static RegisterSuperAdminRequestDto Reg(string? email = null, string? phone = null, string key = SetupKey, IFormFile? image = null) => new()
    {
        name = "New Admin", email = email ?? NewEmail(), phoneNumber = phone ?? NewPhone(),
        SuperAdminKey = key, password = Pwd, ProfileImageUrl = image
    };

    private static FormFile File(string name, int length = 10) =>
        new(new MemoryStream(new byte[length]), 0, length, "ProfileImageUrl", name);

    private static void EnsureUploadFolder() => Directory.CreateDirectory(Path.Combine("wwwroot", "uploads", "profileImage"));

    private static void DeleteUpload(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        var p = Path.Combine("wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
    }

    // ---------- login ----------

    [Fact]
    public async Task The_right_email_and_password_give_tokens_for_a_super_admin()
    {
        var sa = await AddSuperAdminAsync();

        var r = await NewAuth().LoginAsync(new SuperAdminLoginRequestDto { email = sa.Email, password = Pwd });

        r.ShouldNotBeNull();
        r.AccessToken.ShouldNotBeNullOrWhiteSpace();
        r.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        new JwtSecurityTokenHandler().ReadJwtToken(r.AccessToken).Claims.ShouldContain(c => c.Value == "SuperAdmin");
        await using var db = Shared();
        var rt = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Token == r.RefreshToken);
        rt.UserId.ShouldBe(sa.Id);
        rt.OwnerType.ShouldBe(TokenOwnerType.SuperAdmin);
        rt.CenterId.ShouldBeNull();
        rt.IsRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task A_wrong_password_an_unknown_email_and_an_inactive_account_all_give_nothing()
    {
        var sa = await AddSuperAdminAsync();
        var off = await AddSuperAdminAsync(active: false);

        (await NewAuth().LoginAsync(new() { email = sa.Email, password = "Wrong!Pass1" })).ShouldBeNull();
        (await NewAuth().LoginAsync(new() { email = "nobody@test.local", password = Pwd })).ShouldBeNull();
        (await NewAuth().LoginAsync(new() { email = off.Email, password = Pwd })).ShouldBeNull();

        await using var db = Shared();
        (await db.RefreshTokens.CountAsync(t => t.OwnerType == TokenOwnerType.SuperAdmin && (t.UserId == sa.Id || t.UserId == off.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Each_login_gets_its_own_refresh_token()
    {
        var sa = await AddSuperAdminAsync();

        var a = await NewAuth().LoginAsync(new() { email = sa.Email, password = Pwd });
        var b = await NewAuth().LoginAsync(new() { email = sa.Email, password = Pwd });

        a!.RefreshToken.ShouldNotBe(b!.RefreshToken);
    }

    
    [Fact]
    public async Task Register_creates_an_active_super_admin_with_a_hashed_password()
    {
        var email = NewEmail();

        var r = await NewAuth().RegisterAsync(Reg(email));

        r.email.ShouldBe(email);
        await using var db = Shared();
        var row = await db.SuperAdmins.AsNoTracking().SingleAsync(s => s.Email == email);
        row.role.ShouldBe(userRole.SuperAdmin);
        row.IsActive.ShouldBeTrue();
        row.Name.ShouldBe("New Admin");
        row.PasswordHash.ShouldNotBe(Pwd);
        BCrypt.Net.BCrypt.Verify(Pwd, row.PasswordHash).ShouldBeTrue();
        row.CreateAt.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task A_new_super_admin_can_log_in_right_away()
    {
        var email = NewEmail();
        await NewAuth().RegisterAsync(Reg(email));

        var r = await NewAuth().LoginAsync(new() { email = email, password = Pwd });

        r.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("")]
    public async Task A_wrong_setup_key_is_refused_and_nothing_is_created(string key)
    {
        var email = NewEmail();

        await Should.ThrowAsync<UnauthorizedAccessException>(() => NewAuth().RegisterAsync(Reg(email, key: key)));

        await using var db = Shared();
        (await db.SuperAdmins.AnyAsync(s => s.Email == email)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task When_no_setup_key_is_configured_nobody_can_register_not_even_with_an_empty_key(string? configured)
    {
        await Should.ThrowAsync<UnauthorizedAccessException>(() => NewAuth(configured).RegisterAsync(Reg(key: "")));
    }

    /// <summary>"Auth.Forbidden" tells the person they are not allowed, but the real reason is that the email is taken.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Registering_with_an_email_that_is_taken_says_the_email_exists()
    {
        var sa = await AddSuperAdminAsync();

        await ShouldFailWith("Auth.EmailExists", () => NewAuth().RegisterAsync(Reg(sa.Email)));
    }

    /// <summary>super_admins.phone has a unique index, nothing checks it first: raw DbUpdateException (HTTP 500).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Registering_with_a_phone_that_is_taken_says_the_phone_exists()
    {
        var sa = await AddSuperAdminAsync();

        await ShouldFailWith("Auth.PhoneExists", () => NewAuth().RegisterAsync(Reg(phone: sa.phone)));
    }

    [Fact]
    public async Task A_profile_picture_is_saved_under_uploads_and_its_url_is_stored()
    {
        EnsureUploadFolder();
        var email = NewEmail();

        await NewAuth().RegisterAsync(Reg(email, image: File("me.png")));

        await using var db = Shared();
        var url = (await db.SuperAdmins.AsNoTracking().SingleAsync(s => s.Email == email)).ProfileImageUrl;
        try
        {
            url.ShouldNotBeNull();
            url.ShouldStartWith("/uploads/profileImage/");
            url.ShouldEndWith(".png");
            System.IO.File.Exists(Path.Combine("wwwroot", url.TrimStart('/'))).ShouldBeTrue();
        }
        finally { DeleteUpload(url); }
    }

    /// <summary>The edit-profile-image method only allows jpg/jpeg/png up to 5 MB; registration saves any file type of any size under wwwroot, so a ".html" or ".svg" file is served from the site.</summary>
    [Theory]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("run.aspx")]
    [Trait("Category", "KnownBug")]
    public async Task Registration_refuses_a_profile_picture_that_is_not_an_image(string fileName)
    {
        EnsureUploadFolder();
        var email = NewEmail();
        string? url = null;
        try
        {
            await Should.ThrowAsync<Exception>(() => NewAuth().RegisterAsync(Reg(email, image: File(fileName))));
        }
        finally
        {
            await using var db = Shared();
            url = (await db.SuperAdmins.AsNoTracking().SingleOrDefaultAsync(s => s.Email == email))?.ProfileImageUrl;
            DeleteUpload(url);
        }
        url.ShouldBeNull();
    }
}
