using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sehatak.Domain.Enums;
using Sehatak.Infrastructure.Security;
using Sehatak.IntegrationTests.Auth;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

[Collection(SqlServerCollection.Name)]
public class ApiAccountRevocationTests(SqlServerFixture fx) : AuthTestBase(fx)
{
    private string Url => $"/MedicalRecord/patient-get-medical-history/{C.Id}?PageNumber=1&PageSize=10";

    private async Task<HttpStatusCode> CallAsync(ApiFactory factory, int userId, string email, int? centerId = null)
    {
        var token = factory.Services.GetRequiredService<JwtTokenGenerator>()
            .GenerateToken(userId, "Test User", email, "Patient", centerId ?? C.Id);
        var http = factory.NewClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await http.GetAsync(Url)).StatusCode;
    }

    [Fact]
    public async Task An_active_account_keeps_working()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        using var factory = new ApiFactory(Fx, verifyActiveAccount: true);

        (await CallAsync(factory, id, email)).ShouldNotBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_deactivated_account_is_refused_with_a_valid_token()
    {
        var (id, email) = await AddLoginUserAsync(userRole.Patient);
        await using (var db = Db())
        {
            (await db.Users.SingleAsync(u => u.Id == id)).isActive = false;
            await db.SaveChangesAsync();
        }
        using var factory = new ApiFactory(Fx, verifyActiveAccount: true);

        (await CallAsync(factory, id, email)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_token_for_a_user_that_does_not_exist_is_refused()
    {
        using var factory = new ApiFactory(Fx, verifyActiveAccount: true);

        (await CallAsync(factory, 9_999_999, "ghost@test.local")).ShouldBe(HttpStatusCode.Unauthorized);
    }
}