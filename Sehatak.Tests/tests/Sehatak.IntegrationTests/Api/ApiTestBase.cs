using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sehatak.Infrastructure.Security;
using Sehatak.IntegrationTests.Auth;
using Sehatak.IntegrationTests.Infrastructure;

namespace Sehatak.IntegrationTests.Api;

/// <summary>A real center (from AuthTestBase) plus an HttpClient talking to the real API.</summary>
public abstract class ApiTestBase(SqlServerFixture fx) : AuthTestBase(fx)
{
    protected ApiFactory Api => ApiHost.Get(Fx);
    protected HttpClient Http => _http ??= Api.NewClient();
    private HttpClient? _http;

    protected string Issuer => Fx.Harness.Config["Jwt:Issuer"]!;
    protected string Audience => Fx.Harness.Config["Jwt:Audience"]!;
    protected string Secret => Fx.Harness.Config["Jwt:SecretKey"]!;

    /// <summary>A real token made by the API's own generator.</summary>
    protected string Token(string role, int? centerId, int userId = 1)
    {
        return Api.Services.GetRequiredService<JwtTokenGenerator>().GenerateToken(userId, "Test User", "t@test.local", role, centerId);
    }

    protected string Forged(string? key = null, string? issuer = null, string? audience = null,
        DateTime? notBefore = null, DateTime? expires = null, string role = "Receptionist", int? centerId = null, int userId = 1)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        if (centerId != null) claims.Add(new Claim("CenterId", centerId.Value.ToString()));

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? Secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(issuer ?? Issuer, audience ?? Audience, claims,
            notBefore ?? DateTime.UtcNow.AddMinutes(-1), expires ?? DateTime.UtcNow.AddMinutes(30), creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    /// <summary>A token with "alg":"none" and no signature at all.</summary>
    protected string UnsignedToken(string role, int? centerId)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = new Dictionary<string, object>
        {
            [ClaimTypes.NameIdentifier] = "1",
            [ClaimTypes.Role] = role,
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds()
        };
        if (centerId != null) payload["CenterId"] = centerId.Value.ToString();
        return $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64(JsonSerializer.Serialize(payload))}.";
    }

    protected Task<HttpResponseMessage> GetAsync(string url, string? token = null, string? language = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (language != null) req.Headers.AcceptLanguage.ParseAdd(language);
        return Http.SendAsync(req);
    }

    protected Task<HttpResponseMessage> PostAsync<T>(string url, T body, string? token = null, string? language = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (language != null) req.Headers.AcceptLanguage.ParseAdd(language);
        return Http.SendAsync(req);
    }

    protected string InboxUrl(int? centerId = null) => $"/Chat/inbox/{centerId ?? C.Id}";

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
}
