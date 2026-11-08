using System.Net;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public class ApiHttpBehaviourTests(SqlServerFixture fx) : ApiTestBase(fx)
{
    // ------------------------------------------------------------------ CORS

    private async Task<HttpResponseMessage> PreflightAsync(string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, InboxUrl());
        req.Headers.Add("Origin", origin);
        req.Headers.Add("Access-Control-Request-Method", "GET");
        req.Headers.Add("Access-Control-Request-Headers", "authorization");
        return await Http.SendAsync(req);
    }

    [Fact]
    public async Task An_allowed_origin_is_answered_with_credentials()
    {
        var r = await PreflightAsync(ApiFactory.AllowedOrigin);
        r.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe(new[] { ApiFactory.AllowedOrigin });
        r.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(new[] { "true" });
    }

    [Theory]
    [InlineData("https://evil.test")]
    [InlineData("null")]
    [InlineData("https://allowed.test.evil.test")]
    public async Task Any_other_origin_gets_no_CORS_permission(string origin)
    {
        var r = await PreflightAsync(origin);
        r.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    // ------------------------------------------------------------------ what a production server must not show

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/")]
    public async Task Production_does_not_publish_the_API_documentation(string url)
    {
        var r = await GetAsync(url);
        r.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_route_is_a_plain_404()
    {
        var r = await GetAsync("/no/such/route", Token("Admin", C.Id));
        r.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>A token for a center whose database does not exist: the client gets the generic localized error, never SQL text, paths or a stack trace.</summary>
    [Fact]
    public async Task A_server_error_never_leaks_details()
    {
        var r = await GetAsync(InboxUrl(987654), Token("Receptionist", 987654));
        r.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        var body = await r.Content.ReadAsStringAsync();
        var json = await JsonAsync(r);
        json.GetProperty("status").GetInt32().ShouldBe(500);
        foreach (var leak in new[] { "Exception", "SqlException", "   at ", "Sehatak.", "sehatak_center", "Server=", "Database=", ".cs:line" })
            body.ShouldNotContain(leak);
    }

    [Fact]
    public async Task The_server_does_not_announce_its_technology()
    {
        var r = await GetAsync("/health");
        r.Headers.Contains("X-Powered-By").ShouldBeFalse();
        r.Headers.Contains("X-AspNet-Version").ShouldBeFalse();
    }
}
