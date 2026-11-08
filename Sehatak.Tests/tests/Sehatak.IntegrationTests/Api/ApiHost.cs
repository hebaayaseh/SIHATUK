using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Sehatak.API;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Application.Interfaces.IPushNotification;
using Sehatak.IntegrationTests.Infrastructure;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// The real API (Program.cs, middleware, JWT, policies, CenterAccessFilter, validators, localization) running in memory
/// against the same SQL Server container as the service tests. Only the outside world is replaced:
/// background jobs are removed, e-mail and push are fakes, Firebase gets a throw-away key.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqlServerFixture _fx;
    private readonly int _loginPermitLimit;
    public const string AllowedOrigin = "https://allowed.test";

    public ApiFactory(SqlServerFixture fx, int loginPermitLimit = 100_000)
    {
        _fx = fx;
        _loginPermitLimit = loginPermitLimit;
    }

    private static readonly Lazy<string> FirebaseKeyFile = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"sehatak-test-firebase-{Guid.NewGuid():N}.json");
        using var rsa = RSA.Create(2048);
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = "sehatak-test",
            private_key_id = "test",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            client_email = "test@sehatak-test.iam.gserviceaccount.com",
            client_id = "1",
            token_uri = "https://oauth2.googleapis.com/token"
        });
        File.WriteAllText(path, json);
        return path;
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var cfg = _fx.Harness.Config;

        // Program.cs reads these straight from builder.Configuration before Build(), so they must be HOST settings (UseSetting).
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:SharedDb", cfg["ConnectionStrings:SharedDb"]);
        builder.UseSetting("ConnectionStrings:TenantDbTemplate", cfg["ConnectionStrings:TenantDbTemplate"]);
        builder.UseSetting("Jwt:SecretKey", cfg["Jwt:SecretKey"]);
        builder.UseSetting("Jwt:Issuer", cfg["Jwt:Issuer"]);
        builder.UseSetting("Jwt:Audience", cfg["Jwt:Audience"]);
        builder.UseSetting("Firebase:CredentialsPath", FirebaseKeyFile.Value);
        builder.UseSetting("RateLimiting:LoginPermitLimit", _loginPermitLimit.ToString());
        builder.UseSetting("RateLimiting:LoginWindowSeconds", "60");
        builder.UseSetting("RateLimiting:GeneralPermitLimit", "100000");
        builder.UseSetting("RateLimiting:GeneralWindowSeconds", "60");
        builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin);
        builder.UseSetting("SuperAdminSetup:SetupKey", "");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();                        // no subscription / delay / reminder jobs in a test host
            services.RemoveAll<IEmailService>();
            services.AddScoped(_ => Substitute.For<IEmailService>());
            services.RemoveAll<IPushNotification>();
            services.AddSingleton<IPushNotification>(_fx.Harness.Push);
        });
    }

    public HttpClient NewClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}

/// <summary>One shared in-memory API for all tests (starting it takes a second or two).</summary>
public static class ApiHost
{
    private static ApiFactory? _factory;
    private static readonly object Gate = new();

    public static ApiFactory Get(SqlServerFixture fx)
    {
        lock (Gate) return _factory ??= new ApiFactory(fx);
    }
}
