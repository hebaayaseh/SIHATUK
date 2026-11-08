using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Sehatak.API.Security;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.ApiSecurity;

/// <summary>
/// Program.cs defines a "GeneralPolicy" but nothing applies it (no global limiter, no attribute), and only a few login endpoints use "LoginPolicy".
/// Everything else that a stranger can call is unlimited: password reset, code verification, registration, the receipt upload...
/// </summary>
public class RateLimitCoverageTests
{
    private static IEnumerable<(Type Controller, MethodInfo Method)> Actions() =>
        typeof(CenterAccessFilter).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().OfType<IActionHttpMethodProvider>().Any())
                .Select(m => (t, m)));

    private static bool Anonymous((Type Controller, MethodInfo Method) a) =>
        a.Method.GetCustomAttribute<AllowAnonymousAttribute>(true) != null || a.Controller.GetCustomAttribute<AllowAnonymousAttribute>(true) != null;

    private static bool Limited((Type Controller, MethodInfo Method) a) =>
        a.Method.GetCustomAttribute<EnableRateLimitingAttribute>(true) != null || a.Controller.GetCustomAttribute<EnableRateLimitingAttribute>(true) != null;

    private static string Route(MethodInfo m) =>
        m.GetCustomAttributes().OfType<IRouteTemplateProvider>().Select(a => a.Template).FirstOrDefault(t => t != null) ?? "";

    [Fact]
    [Trait("Category", "KnownBug")]
    public void Every_anonymous_action_that_writes_data_is_rate_limited()
    {
        var open = Actions()
            .Where(Anonymous)
            .Where(a => a.Method.GetCustomAttributes().OfType<IActionHttpMethodProvider>().First().HttpMethods.First() != "GET")
            .Where(a => !Limited(a))
            .Select(a => $"{a.Controller.Name}.{a.Method.Name}  [{Route(a.Method)}]  -> [EnableRateLimiting(\"LoginPolicy\")]")
            .ToList();

        open.ShouldBeEmpty(string.Join("\n", open));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public void The_general_policy_is_actually_applied_somewhere()
    {
        var used = Actions().Any(a =>
            a.Method.GetCustomAttributes<EnableRateLimitingAttribute>(true).Any(r => r.PolicyName == "GeneralPolicy")
            || a.Controller.GetCustomAttributes<EnableRateLimitingAttribute>(true).Any(r => r.PolicyName == "GeneralPolicy"));

        used.ShouldBeTrue("GeneralPolicy معرّفة بالـ Program.cs بس ما في ولا endpoint بيستعملها، يعني ما في حد عام للطلبات.");
    }
}
