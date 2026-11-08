using System.Net;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>JWT validation, policies and the center filter, exercised through real HTTP requests.</summary>
[Collection(SqlServerCollection.Name)]
public class ApiAuthenticationTests(SqlServerFixture fx) : ApiTestBase(fx)
{
    // ------------------------------------------------------------------ open / closed

    [Fact]
    public async Task Health_is_open_to_everybody()
    {
        var r = await GetAsync("/health");
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_protected_endpoint_without_a_token_gives_401_json()
    {
        var r = await GetAsync(InboxUrl());
        r.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await JsonAsync(r)).GetProperty("status").GetInt32().ShouldBe(401);
    }

    [Fact]
    public async Task A_valid_token_of_the_right_role_and_center_gets_in()
    {
        var r = await GetAsync(InboxUrl(), Token("Receptionist", C.Id));
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).ShouldBe("[]");
    }

    // ------------------------------------------------------------------ tokens that must be refused

    [Fact]
    public async Task Garbage_is_refused()
        => (await GetAsync(InboxUrl(), "not.a.jwt")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
        => (await GetAsync(InboxUrl(), Forged(key: new string('z', 48), centerId: C.Id))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task A_token_for_another_issuer_or_audience_is_refused()
    {
        (await GetAsync(InboxUrl(), Forged(issuer: "someone-else", centerId: C.Id))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(InboxUrl(), Forged(audience: "another-app", centerId: C.Id))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        var expired = Forged(notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddMinutes(-10), centerId: C.Id);
        (await GetAsync(InboxUrl(), expired)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_without_a_signature_alg_none_is_refused()
        => (await GetAsync(InboxUrl(), UnsignedToken("Admin", C.Id))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    /// <summary>?access_token= is only meant for the SignalR hub; on a normal endpoint it must not log anybody in (it ends up in URLs and logs).</summary>
    [Fact]
    public async Task The_token_in_the_query_string_works_only_for_the_chat_hub()
    {
        var t = Token("Receptionist", C.Id);
        (await GetAsync($"{InboxUrl()}?access_token={t}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ roles

    [Fact]
    public async Task The_wrong_role_gets_403_json()
    {
        var r = await GetAsync(InboxUrl(), Token("Patient", C.Id));
        r.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await JsonAsync(r)).GetProperty("status").GetInt32().ShouldBe(403);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Receptionist")]
    [InlineData("Doctor")]
    [InlineData("Patient")]
    public async Task Only_the_super_admin_can_list_all_centers(string role)
    {
        var r = await GetAsync("/Center/superAdmin-get-all-centers", Token(role, C.Id));
        r.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_super_admin_can_list_all_centers()
    {
        var r = await GetAsync("/Center/superAdmin-get-all-centers", Token("SuperAdmin", null));
        r.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ centers

    [Fact]
    public async Task A_token_of_one_center_is_refused_on_another_center()
    {
        var r = await GetAsync(InboxUrl(C.Id + 1), Token("Receptionist", C.Id));
        r.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await r.Content.ReadAsStringAsync()).ShouldContain("Auth.CenterMismatch");
    }

    [Fact]
    public async Task A_staff_token_without_a_center_is_refused_on_a_center_endpoint()
    {
        var r = await GetAsync(InboxUrl(), Token("Receptionist", null));
        r.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_super_admin_can_enter_any_center_endpoint()
    {
        var r = await GetAsync($"/Center/superAdmin-admin-get-center-by-Id/{C.Id}", Token("SuperAdmin", null));
        r.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
        r.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    // ------------------------------------------------------------------ the chat hub

    [Fact]
    public async Task The_chat_hub_needs_a_staff_token_and_accepts_it_in_the_query_string()
    {
        var url = "/hubs/chat/negotiate?negotiateVersion=1";

        (await Http.PostAsync(url, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var patient = await Http.PostAsync($"{url}&access_token={Token("Patient", C.Id)}", null);
        patient.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var staff = await Http.PostAsync($"{url}&access_token={Token("Receptionist", C.Id)}", null);
        staff.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
