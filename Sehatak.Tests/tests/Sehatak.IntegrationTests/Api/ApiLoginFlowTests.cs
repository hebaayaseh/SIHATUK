using System.Net;
using System.Text.RegularExpressions;
using Sehatak.Application.DTOs.StaffLogIn;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public class ApiLoginFlowTests(SqlServerFixture fx) : ApiTestBase(fx)
{
    private string LoginUrl => $"/Auth/staff-login/{C.Id}";
    private static readonly Regex LooksLikeACode = new(@"^[A-Za-z]+\.[A-Za-z]+$");

    [Fact]
    public async Task Login_gives_tokens_and_the_access_token_opens_a_protected_endpoint()
    {
        var u = await AddLoginUserAsync();

        var login = await PostAsync(LoginUrl, new StaffLoginRequestDto { Email = u.Email, Password = Pwd });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await JsonAsync(login);
        var access = json.GetProperty("accessToken").GetString()!;
        json.GetProperty("refreshToken").GetString().ShouldNotBeNullOrWhiteSpace();

        (await GetAsync(InboxUrl(), access)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(InboxUrl(C.Id + 1), access)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);   // and only for ITS center
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_look_exactly_the_same()
    {
        var u = await AddLoginUserAsync();

        var wrongPwd = await PostAsync(LoginUrl, new StaffLoginRequestDto { Email = u.Email, Password = "nope-nope-1A!" });
        var unknown = await PostAsync(LoginUrl, new StaffLoginRequestDto { Email = "nobody@test.local", Password = "nope-nope-1A!" });

        ((int)wrongPwd.StatusCode).ShouldBeInRange(400, 401);
        wrongPwd.StatusCode.ShouldBe(unknown.StatusCode);
        (await JsonAsync(wrongPwd)).GetProperty("message").GetString().ShouldBe((await JsonAsync(unknown)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Errors_are_readable_text_in_the_language_asked_for()
    {
        var u = await AddLoginUserAsync();
        var bad = new StaffLoginRequestDto { Email = u.Email, Password = "nope-nope-1A!" };

        var en = (await JsonAsync(await PostAsync(LoginUrl, bad, language: "en"))).GetProperty("message").GetString()!;
        var ar = (await JsonAsync(await PostAsync(LoginUrl, bad, language: "ar"))).GetProperty("message").GetString()!;

        LooksLikeACode.IsMatch(en).ShouldBeFalse($"الرسالة الإنجليزية طلعت كود مش نص: {en}");
        LooksLikeACode.IsMatch(ar).ShouldBeFalse($"الرسالة العربية طلعت كود مش نص: {ar}");
        ar.ShouldNotBe(en);
    }

    /// <summary>Whatever garbage a client sends, the answer is a 4xx, never a 500 with a crash behind it.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData("{}")]
    [InlineData("{\"email\":null,\"password\":null}")]
    [InlineData("{\"email\":\"\",\"password\":\"\"}")]
    [InlineData("{\"email\":\"not-an-email\",\"password\":\"x\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{broken json")]
    public async Task Bad_login_bodies_never_cause_a_server_error(string body)
    {
        var r = await Http.PostAsync(LoginUrl, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        ((int)r.StatusCode).ShouldBeInRange(400, 499, $"body: {body}");
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_415()
    {
        var r = await Http.PostAsync(LoginUrl, new StringContent("email=a&password=b", System.Text.Encoding.UTF8, "text/plain"));
        r.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task After_five_wrong_passwords_even_the_right_one_is_locked_out()
    {
        var u = await AddLoginUserAsync();
        for (var i = 0; i < 5; i++)
            await PostAsync(LoginUrl, new StaffLoginRequestDto { Email = u.Email, Password = "wrong-1A!" + i });

        var r = await PostAsync(LoginUrl, new StaffLoginRequestDto { Email = u.Email, Password = Pwd });
        ((int)r.StatusCode).ShouldBeInRange(400, 429);
        r.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_login_endpoint_is_rate_limited_per_client()
    {
        await using var limited = new ApiFactory(Fx, loginPermitLimit: 3);
        var client = limited.NewClient();
        var u = await AddLoginUserAsync();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            var r = await client.PostAsJsonAsyncCompat(LoginUrl, new StaffLoginRequestDto { Email = u.Email, Password = Pwd });
            codes.Add(r.StatusCode);
        }

        codes.Take(3).ShouldAllBe(c => c != HttpStatusCode.TooManyRequests);
        codes.Skip(3).ShouldAllBe(c => c == HttpStatusCode.TooManyRequests);
    }
}

internal static class HttpClientExtensions
{
    public static Task<HttpResponseMessage> PostAsJsonAsyncCompat<T>(this HttpClient c, string url, T body) =>
        System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(c, url, body);
}
