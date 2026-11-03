using Microsoft.EntityFrameworkCore;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Auth;

[Collection(SqlServerCollection.Name)]
public class TokenServiceTests(SqlServerFixture fx) : AuthTestBase(fx)
{
    private async Task<(int Id, string Refresh)> LoginAsync(userRole role = userRole.Receptionist)
    {
        var (id, email) = await AddLoginUserAsync(role);
        var r = await NewLogin().StaffLoginAsync(C.Id, new Sehatak.Application.DTOs.StaffLogIn.StaffLoginRequestDto { Email = email, Password = Pwd }, null);
        return (id, r.RefreshToken);
    }

    // ---------- issue ----------

    [Fact]
    public async Task Issue_returns_an_access_token_and_stores_a_long_random_refresh_token()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Nurse);
        var a = await NewTokens().IssueTokensAsync(id, "Nur Se", email, "Nurse", C.Id, TokenOwnerType.TenantUser);
        var b = await NewTokens().IssueTokensAsync(id, "Nur Se", email, "Nurse", C.Id, TokenOwnerType.TenantUser);

        a.RefreshToken.Length.ShouldBeGreaterThan(80);
        a.RefreshToken.ShouldNotBe(b.RefreshToken);
        RoleOf(a.AccessToken).ShouldBe("Nurse");
        (await TokensOfAsync(id)).Count.ShouldBe(2);
    }

    // ---------- refresh ----------

    [Fact]
    public async Task Refresh_rotates_the_token_old_one_is_revoked_new_one_is_stored()
    {
        var (id, old) = await LoginAsync();

        var r = await NewTokens().RefreshAsync(old);

        r.RefreshToken.ShouldNotBe(old);
        r.AccessToken.ShouldNotBeNullOrEmpty();
        var rows = await TokensOfAsync(id);
        rows.Count.ShouldBe(2);
        rows.Single(t => t.Token == old).IsRevoked.ShouldBeTrue();
        var fresh = rows.Single(t => t.Token == r.RefreshToken);
        fresh.IsRevoked.ShouldBeFalse();
        fresh.ExpiresAt.ShouldBe(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Refresh_reads_the_role_from_the_database_not_from_the_old_token()
    {
        var (id, old) = await LoginAsync(userRole.Receptionist);
        await using (var db = Db())
        {
            var u = await db.Users.SingleAsync(x => x.Id == id);
            u.role = userRole.Nurse;
            await db.SaveChangesAsync();
        }

        var r = await NewTokens().RefreshAsync(old);

        RoleOf(r.AccessToken).ShouldBe("Nurse");
    }

    [Fact]
    public async Task A_refresh_token_can_be_used_only_once()
    {
        var (_, old) = await LoginAsync();
        await NewTokens().RefreshAsync(old);

        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync(old));
    }

    [Fact]
    public async Task Unknown_revoked_and_expired_tokens_are_rejected()
    {
        var (id, _) = await AddLoginUserAsync();
        await InsertTokenAsync(id, "revoked-token", revoked: true);
        await InsertTokenAsync(id, "expired-token", expiresAt: DateTime.UtcNow.AddMinutes(-1));

        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync("never-issued"));
        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync("revoked-token"));
        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync("expired-token"));
    }

    [Fact]
    public async Task Token_without_a_center_or_for_a_deleted_user_is_rejected()
    {
        var (id, _) = await AddLoginUserAsync();
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            shared.RefreshTokens.Add(new Sehatak.Domain.Entities.SharedEntities.RefreshToken
                { Token = "no-center", OwnerType = TokenOwnerType.TenantUser, UserId = id, CenterId = null, ExpiresAt = DateTime.UtcNow.AddDays(1) });
            shared.RefreshTokens.Add(new Sehatak.Domain.Entities.SharedEntities.RefreshToken
                { Token = "ghost-user", OwnerType = TokenOwnerType.TenantUser, UserId = 999_999, CenterId = C.Id, ExpiresAt = DateTime.UtcNow.AddDays(1) });
            await shared.SaveChangesAsync();
        }

        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync("no-center"));
        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync("ghost-user"));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_deactivated_user_cannot_refresh()
    {
        var (id, old) = await LoginAsync();
        await using (var db = Db())
        {
            (await db.Users.SingleAsync(x => x.Id == id)).isActive = false;
            await db.SaveChangesAsync();
        }

        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync(old));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_name_in_the_token_is_the_same_after_a_refresh()
    {
        var (_, email) = await AddLoginUserAsync();
        var login = await NewLogin().StaffLoginAsync(C.Id, new Sehatak.Application.DTOs.StaffLogIn.StaffLoginRequestDto { Email = email, Password = Pwd }, null);

        var refreshed = await NewTokens().RefreshAsync(login.RefreshToken);

        NameOf(refreshed.AccessToken).ShouldBe(NameOf(login.AccessToken));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_refreshes_of_the_same_token_at_once_give_only_one_new_token()
    {
        var (id, old) = await LoginAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await NewTokens().RefreshAsync(old); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        results.Count(e => e == null).ShouldBe(1);
        results.Single(e => e != null).ShouldBeOfType<BusinessException>();
        (await TokensOfAsync(id)).Count(t => !t.IsRevoked).ShouldBe(1);
    }

    // ---------- logout ----------

    [Fact]
    public async Task Logout_revokes_the_token_so_it_cannot_refresh_anymore()
    {
        var (id, token) = await LoginAsync();

        await NewTokens().LogoutAsync(token);

        (await TokensOfAsync(id)).Single().IsRevoked.ShouldBeTrue();
        await ShouldFailWith("Auth.InvalidRefreshToken", () => NewTokens().RefreshAsync(token));
    }

    [Fact]
    public async Task Logout_with_an_unknown_token_does_nothing()
    {
        await Should.NotThrowAsync(() => NewTokens().LogoutAsync("never-issued"));
    }

    [Fact]
    public async Task Logout_revokes_only_that_session()
    {
        var (id, email) = await AddLoginUserAsync();
        var req = new Sehatak.Application.DTOs.StaffLogIn.StaffLoginRequestDto { Email = email, Password = Pwd };
        var phone = await NewLogin().StaffLoginAsync(C.Id, req, null);
        var laptop = await NewLogin().StaffLoginAsync(C.Id, req, null);

        await NewTokens().LogoutAsync(phone.RefreshToken);

        var rows = await TokensOfAsync(id);
        rows.Single(t => t.Token == phone.RefreshToken).IsRevoked.ShouldBeTrue();
        rows.Single(t => t.Token == laptop.RefreshToken).IsRevoked.ShouldBeFalse();
    }
}
