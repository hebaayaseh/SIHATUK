using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Enums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// Sessions and roles across real flows: a user is switched off in the middle of a session, tokens expire and are
/// refreshed, logout kills the refresh token, and every role stays inside its own doors.
/// The calls that must check "is this account still active" go through a host that has that check turned on
/// (the shared test host keeps it off); tokens are signed with the same key so both hosts accept them.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AccountSecurityFlowTests(SqlServerFixture fx) : OnboardingFlowBase(fx)
{
    private string StaffUrl => $"/Chat/inbox/{C.Id}";
    private string PatientUrl => $"/MedicalRecord/patient-get-medical-history/{C.Id}?PageNumber=1&PageSize=10";

    private static Task<HttpResponseMessage> Call(ApiFactory f, HttpMethod method, string url, string? token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body != null) req.Content = JsonContent.Create(body);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return f.NewClient().SendAsync(req);
    }

    private async Task<(string Access, string Refresh)> StaffLoginTokensAsync(string email)
    {
        var json = await OkJsonAsync(await PostAsync($"/Auth/staff-login/{C.Id}", new { Email = email, Password = Pwd }));
        return (json.GetProperty("accessToken").GetString()!, json.GetProperty("refreshToken").GetString()!);
    }

    private async Task<JsonElement> RefreshOkAsync(string refresh) =>
        await OkJsonAsync(await PostAsync("/Token/refresh", new { refreshToken = refresh }));

    // ---------- the owner switches a staff member off in the middle of a session ----------

    [Fact]
    public async Task A_staff_member_switched_off_by_the_owner_is_locked_out_everywhere()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Receptionist);
        var (access, refresh) = await StaffLoginTokensAsync(email);
        await using var strict = new ApiFactory(Fx, verifyActiveAccount: true);
        var admin = AdminToken;

        // the owner switches him off while he is logged in
        (await Call(strict, HttpMethod.Put, $"/Staff/admin-remove-staff-from-center/{C.Id}", admin, new { userId = id })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // his access token is refused, his refresh token is refused (and burned), and he cannot log in again
        (await Call(strict, HttpMethod.Get, StaffUrl, access)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PostAsync("/Token/refresh", new { refreshToken = refresh })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync($"/Auth/staff-login/{C.Id}", new { Email = email, Password = Pwd })).StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_staff_member_switched_back_on_gets_a_new_session_and_everything_works_again()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Receptionist);
        await using var strict = new ApiFactory(Fx, verifyActiveAccount: true);
        var admin = AdminToken;

        await Call(strict, HttpMethod.Put, $"/Staff/admin-remove-staff-from-center/{C.Id}", admin, new { userId = id });
        (await PostAsync($"/Auth/staff-login/{C.Id}", new { Email = email, Password = Pwd })).StatusCode.ShouldNotBe(HttpStatusCode.OK);

        (await Call(strict, HttpMethod.Put, $"/Staff/admin-active-staff-from-center/{C.Id}", admin, new { userId = id })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var (access, _) = await StaffLoginTokensAsync(email);
        (await Call(strict, HttpMethod.Get, StaffUrl, access)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_user_who_no_longer_exists_in_the_center_is_refused_even_with_a_valid_signed_token()
    {
        await using var strict = new ApiFactory(Fx, verifyActiveAccount: true);

        (await Call(strict, HttpMethod.Get, StaffUrl, Token("Receptionist", C.Id, 9_999_999))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- patient switches his own profile off ----------

    [Fact]
    public async Task A_patient_who_deactivates_his_profile_is_locked_out_until_he_reactivates_it()
    {
        var p = await RegisterVerifyLoginAsync();
        var login = await OkJsonAsync(await PostAsync($"/Auth/patient-login-patient/{C.Id}", new { email = p.Email, password = Pwd }));
        var refresh = login.GetProperty("refreshToken").GetString()!;
        await using var strict = new ApiFactory(Fx, verifyActiveAccount: true);

        (await Call(strict, HttpMethod.Put, $"/Auth/patient-deactive-profile/{C.Id}", p.Token)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PostAsync($"/Auth/patient-login-patient/{C.Id}", new { email = p.Email, password = Pwd })).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        (await PostAsync("/Token/refresh", new { refreshToken = refresh })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // he comes back with his password and the new session works
        var back = await Call(strict, HttpMethod.Put, $"/Auth/patient-active-profile/{C.Id}", null, new { email = p.Email, password = Pwd });
        back.StatusCode.ShouldBe(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());

        var again = await OkJsonAsync(await PostAsync($"/Auth/patient-login-patient/{C.Id}", new { email = p.Email, password = Pwd }));
        (await Call(strict, HttpMethod.Get, PatientUrl, again.GetProperty("accessToken").GetString())).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- super admin ----------

    [Fact]
    public async Task A_deactivated_super_admin_is_locked_out_of_everything()
    {
        var access = await SuperAdminLoginAsync();
        await using var strict = new ApiFactory(Fx, verifyActiveAccount: true);

        // the account is switched off (the super admin id is inside the token)
        var superId = int.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(access)
            .Claims.First(c => c.Type is "nameid" or System.Security.Claims.ClaimTypes.NameIdentifier).Value);
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            (await shared.SuperAdmins.SingleAsync(s => s.Id == superId)).IsActive = false;
            await shared.SaveChangesAsync();
        }

        (await Call(strict, HttpMethod.Get, "/Center/superAdmin-get-all-centers", access)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- refresh / logout / expiry ----------

    [Fact]
    public async Task An_expired_access_token_is_replaced_through_the_refresh_token_and_the_old_refresh_token_dies()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Receptionist);
        var (_, refresh) = await StaffLoginTokensAsync(email);

        // the access token ran out
        var expired = Forged(expires: DateTime.UtcNow.AddMinutes(-10), notBefore: DateTime.UtcNow.AddMinutes(-40), centerId: C.Id);
        (await GetAsync(StaffUrl, expired)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // the app refreshes and carries on
        var fresh = await RefreshOkAsync(refresh);
        var newAccess = fresh.GetProperty("accessToken").GetString()!;
        var newRefresh = fresh.GetProperty("refreshToken").GetString()!;
        (await GetAsync(StaffUrl, newAccess)).StatusCode.ShouldBe(HttpStatusCode.OK);
        newRefresh.ShouldNotBe(refresh);

        // the old refresh token cannot be used again (rotation), the new one can
        (await PostAsync("/Token/refresh", new { refreshToken = refresh })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync("/Token/refresh", new { refreshToken = newRefresh })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_kills_the_refresh_token()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Receptionist);
        var (_, refresh) = await StaffLoginTokensAsync(email);

        (await PostAsync("/Token/logout", new { refreshToken = refresh })).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await PostAsync("/Token/refresh", new { refreshToken = refresh })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // logging out twice, or with garbage, is harmless
        (await PostAsync("/Token/logout", new { refreshToken = refresh })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PostAsync("/Token/logout", new { refreshToken = "garbage" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refresh_token_that_expired_or_never_existed_is_refused()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Receptionist);
        var (_, refresh) = await StaffLoginTokensAsync(email);

        (await PostAsync("/Token/refresh", new { refreshToken = "never-issued" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using (var shared = Fx.Harness.NewSharedDb())
        {
            var row = await shared.RefreshTokens.SingleAsync(t => t.Token == refresh);
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await shared.SaveChangesAsync();
        }
        (await PostAsync("/Token/refresh", new { refreshToken = refresh })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_refresh_token_cannot_carry_a_user_into_another_center()
    {
        var (_, email) = await AddLoginUserAsync(userRole.Receptionist);
        var (_, refresh) = await StaffLoginTokensAsync(email);

        var fresh = await RefreshOkAsync(refresh);
        var access = fresh.GetProperty("accessToken").GetString()!;

        (await GetAsync(StaffUrl, access)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync($"/Chat/inbox/{C.Id + 1}", access)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---------- every role stays inside its own doors ----------

    public static IEnumerable<object[]> WrongRoleCalls()
    {
        // role, method, url (C.Id is replaced at run time)
        foreach (var role in new[] { "Doctor", "Receptionist", "LabTechnician", "Patient" })
        {
            yield return new object[] { role, "POST", "/ServicePrice/admin-add-service-price/{c}" };
            yield return new object[] { role, "PUT", "/Staff/admin-remove-staff-from-center/{c}" };
            yield return new object[] { role, "POST", "/DoctorDailyHours/admin-add-doctor-hours/{c}/1" };
            yield return new object[] { role, "GET", "/Center/superAdmin-get-all-centers" };
        }
        foreach (var role in new[] { "Doctor", "Receptionist", "LabTechnician", "Admin" })
            yield return new object[] { role, "PUT", "/Auth/patient-deactive-profile/{c}" };
        foreach (var role in new[] { "Admin", "Doctor", "Receptionist", "LabTechnician", "Patient" })
            yield return new object[] { role, "POST", "/CenterRegistration/superAdmin-approve-requests/1" };
    }

    [Theory]
    [MemberData(nameof(WrongRoleCalls))]
    public async Task A_role_is_refused_at_a_door_that_belongs_to_another_role(string role, string method, string url)
    {
        var res = await SendAsync(new HttpMethod(method), url.Replace("{c}", C.Id.ToString()), Token(role, C.Id, 1), method == "GET" ? null : new { });

        res.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{role} {method} {url}");
    }

    [Fact]
    public async Task Nobody_without_a_token_gets_into_a_protected_door()
    {
        foreach (var (method, url) in new[]
        {
            ("GET", StaffUrl), ("GET", PatientUrl), ("GET", "/Center/superAdmin-get-all-centers"),
            ("PUT", $"/Staff/admin-remove-staff-from-center/{C.Id}"), ("POST", $"/ServicePrice/admin-add-service-price/{C.Id}"),
        })
            (await SendAsync(new HttpMethod(method), url, null, method == "GET" ? null : new { })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, url);
    }
}
