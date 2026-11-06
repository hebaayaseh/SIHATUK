using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Sehatak.API.Security;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.ApiSecurity;

public class CenterAccessFilterTests
{
    private class Dummy
    {
        public void Plain() { }
        [CenterAgnostic] public void Agnostic() { }
    }

    private static ClaimsPrincipal User(string? role, int? centerId, bool authenticated = true)
    {
        var claims = new List<Claim>();
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
        if (centerId != null) claims.Add(new Claim("CenterId", centerId.Value.ToString()));
        return new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "test") : new ClaimsIdentity());
    }

    private static AuthorizationFilterContext Ctx(ClaimsPrincipal user, object? routeCenterId, string method = nameof(Dummy.Plain))
    {
        var http = new DefaultHttpContext { User = user };
        var rd = new RouteData();
        if (routeCenterId != null) rd.Values["centerId"] = routeCenterId;
        var ad = new ControllerActionDescriptor { MethodInfo = typeof(Dummy).GetMethod(method)! };
        return new AuthorizationFilterContext(new ActionContext(http, rd, ad), new List<IFilterMetadata>());
    }

    private static async Task<AuthorizationFilterContext> Run(AuthorizationFilterContext c)
    {
        await new CenterAccessFilter().OnAuthorizationAsync(c);
        return c;
    }

    private static string? Error(AuthorizationFilterContext c) =>
        (c.Result as ObjectResult)?.Value?.GetType().GetProperty("error")?.GetValue((c.Result as ObjectResult)!.Value)?.ToString();

    [Fact]
    public async Task Unauthenticated_requests_pass_through_to_the_authorize_attribute()
        => (await Run(Ctx(User(null, null, false), 5))).Result.ShouldBeNull();

    [Fact]
    public async Task SuperAdmin_can_open_any_center()
        => (await Run(Ctx(User("SuperAdmin", null), 99))).Result.ShouldBeNull();

    [Fact]
    public async Task Staff_can_open_their_own_center()
        => (await Run(Ctx(User("Doctor", 5), "5"))).Result.ShouldBeNull();

    [Fact]
    public async Task Staff_cannot_open_another_center()
    {
        var c = await Run(Ctx(User("Admin", 5), "6"));
        ((ObjectResult)c.Result!).StatusCode.ShouldBe(403);
        Error(c).ShouldBe("Auth.CenterMismatch");
    }

    [Fact]
    public async Task Token_without_CenterId_claim_is_rejected()
    {
        var c = await Run(Ctx(User("Admin", null), 5));
        Error(c).ShouldBe("Auth.CenterMismatch");
    }

    [Fact]
    public async Task Non_numeric_center_in_route_is_rejected()
    {
        var c = await Run(Ctx(User("Admin", 5), "abc"));
        Error(c).ShouldBe("Auth.CenterIdRequired");
    }

    [Fact]
    public async Task Route_without_centerId_is_rejected_unless_CenterAgnostic()
    {
        var c1 = await Run(Ctx(User("Patient", 5), null));
        Error(c1).ShouldBe("Auth.CenterIdRequired");

        var c2 = await Run(Ctx(User("Patient", 5), null, nameof(Dummy.Agnostic)));
        c2.Result.ShouldBeNull();
    }
}
