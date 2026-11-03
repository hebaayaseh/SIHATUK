using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.StaffLogIn;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Auth;

[Collection(SqlServerCollection.Name)]
public class StaffLoginTests(SqlServerFixture fx) : AuthTestBase(fx)
{
    private static StaffLoginRequestDto Req(string email, string password = Pwd) => new() { Email = email, Password = password };

    // ---------- success ----------

    [Theory]
    [InlineData(userRole.Admin)]
    [InlineData(userRole.Doctor)]
    [InlineData(userRole.Receptionist)]
    [InlineData(userRole.Nurse)]
    [InlineData(userRole.LabTechnician)]
    [InlineData(userRole.GeneralDoctor)]
    public async Task Staff_of_every_role_can_log_in_and_the_token_carries_their_identity(userRole role)
    {
        var (id, email) = await AddLoginUserAsync(role);

        var r = await NewLogin().StaffLoginAsync(C.Id, Req(email), "10.0.0.1");

        r.AccessToken.ShouldNotBeNullOrEmpty();
        r.RefreshToken.ShouldNotBeNullOrEmpty();
        RoleOf(r.AccessToken).ShouldBe(role.ToString());
        UserIdOf(r.AccessToken).ShouldBe(id.ToString());
        Claim(r.AccessToken, "CenterId").ShouldBe(C.Id.ToString());
    }

    [Fact]
    public async Task Login_stores_one_refresh_token_for_seven_days_and_records_a_success_attempt()
    {
        var (id, email) = await AddLoginUserAsync();
        var r = await NewLogin().StaffLoginAsync(C.Id, Req(email), "10.0.0.1");

        var tokens = await TokensOfAsync(id);
        tokens.Count.ShouldBe(1);
        tokens[0].Token.ShouldBe(r.RefreshToken);
        tokens[0].OwnerType.ShouldBe(TokenOwnerType.TenantUser);
        tokens[0].IsRevoked.ShouldBeFalse();
        tokens[0].ExpiresAt.ShouldBe(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(2));

        var attempts = await AttemptsOfAsync(email);
        attempts.Count.ShouldBe(1);
        attempts[0].Success.ShouldBeTrue();
        attempts[0].IpAddress.ShouldBe("10.0.0.1");
        attempts[0].UserType.ShouldBe(LoginUserType.Staff);
    }

    [Fact]
    public async Task Email_is_not_case_sensitive()
    {
        var (_, email) = await AddLoginUserAsync();
        var r = await NewLogin().StaffLoginAsync(C.Id, Req(email.ToUpperInvariant()), null);
        r.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_name_in_the_token_has_a_space_between_first_and_last_name()
    {
        var (_, email) = await AddLoginUserAsync();
        var r = await NewLogin().StaffLoginAsync(C.Id, Req(email), null);

        var name = NameOf(r.AccessToken)!;
        name.ShouldContain(" ");
        name.ShouldMatch(@"^Fn\d+ Ln\d+$");
    }

    // ---------- failures ----------

    [Fact]
    public async Task Wrong_password_is_rejected_and_recorded_as_a_failed_attempt()
    {
        var (id, email) = await AddLoginUserAsync();
        await ShouldFailWith("Auth.Unauthorized", () => NewLogin().StaffLoginAsync(C.Id, Req(email, "wrong-pass"), "1.2.3.4"));

        (await AttemptsOfAsync(email)).ShouldHaveSingleItem().Success.ShouldBeFalse();
        (await TokensOfAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_email_is_rejected_and_recorded()
    {
        var email = $"nobody.{Guid.NewGuid():N}@test.local";
        await ShouldFailWith("Auth.Unauthorized", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
        (await AttemptsOfAsync(email)).ShouldHaveSingleItem().Success.ShouldBeFalse();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Wrong_password_and_unknown_email_give_the_same_error_so_emails_cannot_be_discovered()
    {
        var (_, email) = await AddLoginUserAsync();
        var wrongPassword = await Should.ThrowAsync<Sehatak.Application.DTOs.Exceptions.BusinessException>(
            () => NewLogin().StaffLoginAsync(C.Id, Req(email, "wrong-pass"), null));
        var unknown = await Should.ThrowAsync<Sehatak.Application.DTOs.Exceptions.BusinessException>(
            () => NewLogin().StaffLoginAsync(C.Id, Req($"nobody.{Guid.NewGuid():N}@test.local"), null));

        wrongPassword.Message.ShouldBe(unknown.Message);
    }

    [Fact]
    public async Task Inactive_staff_cannot_log_in()
    {
        var (_, email) = await AddLoginUserAsync(active: false);
        await ShouldFailWith("Auth.Unauthorized", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
    }

    [Fact]
    public async Task A_patient_account_cannot_log_in_through_the_staff_door()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        await ShouldFailWith("Auth.Forbidden", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
        (await TokensOfAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_of_another_center_cannot_log_in_here()
    {
        var other = await Fx.Harness.CreateCenterAsync();
        await using var db = Fx.Harness.Tenant(other.Id);
        var (_, email) = (0, $"other.{Guid.NewGuid():N}@test.local");
        db.Users.Add(new Sehatak.Domain.Entities.TenantEntities.User
        {
            firstName = "O", lastName = "O", email = email, passwordHash = BCrypt.Net.BCrypt.HashPassword(Pwd),
            phoneNumber = "0560000001", address = "a", city = "c", role = userRole.Receptionist, isActive = true
        });
        await db.SaveChangesAsync();

        await ShouldFailWith("Auth.Unauthorized", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
    }

    [Fact]
    public async Task Unknown_center_is_rejected()
    {
        var (_, email) = await AddLoginUserAsync();
        await ShouldFailWith("Center.NotFound", () => NewLogin().StaffLoginAsync(999_999, Req(email), null));
    }

    // ---------- suspended center (code intends: admin may still log in, everybody else gets Center.Suspended) ----------

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Admin_can_still_log_in_while_the_center_is_suspended()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Admin);
        await SetCenterStatusAsync(CenterStatus.Suspended);

        (await NewLogin().StaffLoginAsync(C.Id, Req(email), null)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Other_staff_are_told_the_center_is_suspended()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Receptionist);
        await SetCenterStatusAsync(CenterStatus.Suspended);

        await ShouldFailWith("Center.Suspended", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
    }

    // ---------- lockout ----------

    [Fact]
    public async Task Five_recent_failures_lock_the_account_even_for_the_right_password()
    {
        var (id, email) = await AddLoginUserAsync();
        await SeedAttemptsAsync(email, 5);

        await ShouldFailWith("Auth.TooManyAttempts", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
        (await TokensOfAsync(id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Four_failures_do_not_lock_and_the_fifth_real_failure_does()
    {
        var (_, email) = await AddLoginUserAsync();
        await SeedAttemptsAsync(email, 4);

        await ShouldFailWith("Auth.Unauthorized", () => NewLogin().StaffLoginAsync(C.Id, Req(email, "bad"), null));
        await ShouldFailWith("Auth.TooManyAttempts", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
    }

    [Fact]
    public async Task Failures_older_than_fifteen_minutes_are_forgotten()
    {
        var (_, email) = await AddLoginUserAsync();
        await SeedAttemptsAsync(email, 5, minutesAgo: 16);

        (await NewLogin().StaffLoginAsync(C.Id, Req(email), null)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Lockout_counts_only_failures_of_the_same_email_center_and_user_type()
    {
        var (_, email) = await AddLoginUserAsync();
        var other = await Fx.Harness.CreateCenterAsync();
        await SeedAttemptsAsync(email, 3);                                          // same email, counts
        await SeedAttemptsAsync(email, 3, centerId: other.Id);                      // other center
        await SeedAttemptsAsync(email, 3, type: LoginUserType.Patient);             // patient door
        await SeedAttemptsAsync(email, 3, success: true);                           // successes
        await SeedAttemptsAsync("someone.else@test.local", 5);                      // other email

        (await NewLogin().StaffLoginAsync(C.Id, Req(email), null)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Lockout_ignores_email_letter_case()
    {
        var (_, email) = await AddLoginUserAsync();
        await SeedAttemptsAsync(email.ToUpperInvariant(), 5);

        await ShouldFailWith("Auth.TooManyAttempts", () => NewLogin().StaffLoginAsync(C.Id, Req(email), null));
    }

    [Fact]
    public async Task Attempts_made_while_locked_out_are_not_recorded_so_the_lock_does_not_extend()
    {
        var (_, email) = await AddLoginUserAsync();
        await SeedAttemptsAsync(email, 5);

        await Should.ThrowAsync<Exception>(() => NewLogin().StaffLoginAsync(C.Id, Req(email, "bad"), null));

        (await AttemptsOfAsync(email)).Count.ShouldBe(5);
    }
}
