using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Sehatak.Infrastructure.Security;
using Shouldly;

namespace Sehatak.UnitTests;

public class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator Create() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "unit-test-secret-key-at-least-32-chars-long!!",
            ["Jwt:Issuer"] = "sehatak-test",
            ["Jwt:Audience"] = "sehatak-test-clients"
        }).Build());

    private static JwtSecurityToken Read(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt);

    [Fact]
    public void Token_contains_identity_role_and_center_claims()
    {
        var jwt = Read(Create().GenerateToken(7, "Heba Ali", "heba@x.com", "Doctor", 12));

        jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "nameid").Value.ShouldBe("7");
        jwt.Claims.First(c => c.Type == ClaimTypes.Role || c.Type == "role").Value.ShouldBe("Doctor");
        jwt.Claims.First(c => c.Type == "CenterId").Value.ShouldBe("12");
        jwt.Issuer.ShouldBe("sehatak-test");
        jwt.Audiences.ShouldContain("sehatak-test-clients");
    }

    [Fact]
    public void SuperAdmin_token_has_no_CenterId_claim()
    {
        var jwt = Read(Create().GenerateToken(1, "Root", "root@x.com", "SuperAdmin", null));

        jwt.Claims.ShouldNotContain(c => c.Type == "CenterId");
    }

    [Fact]
    public void Token_expires_in_about_30_minutes()
    {
        var jwt = Read(Create().GenerateToken(1, "n", "e@x.com", "Patient", 1));

        (jwt.ValidTo - DateTime.UtcNow).ShouldBeInRange(TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }
}
