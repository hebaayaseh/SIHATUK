using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Sehatak.API.Security;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.ApiSecurity;

/// <summary>Reflection checks on how endpoints receive data and which HTTP verb changes data.</summary>
public class HttpContractTests
{
    private static IEnumerable<(Type Controller, MethodInfo Method)> Actions() =>
        typeof(CenterAccessFilter).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().OfType<IActionHttpMethodProvider>().Any())
                .Select(m => (t, m)));

    private static string Verb(MethodInfo m) =>
        m.GetCustomAttributes().OfType<IActionHttpMethodProvider>().First().HttpMethods.First();

    private static string Route(MethodInfo m) =>
        m.GetCustomAttributes().OfType<IRouteTemplateProvider>().Select(a => a.Template).FirstOrDefault(t => t != null) ?? "";

    private static bool IsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
            || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(Guid) || t == typeof(TimeSpan)
            || t == typeof(CancellationToken) || t == typeof(DateTimeOffset);
    }

    /// <summary>With [ApiController] a complex parameter is read from the BODY by default; a GET has no body, so paging/filter objects never arrive (415).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void Complex_parameters_of_GET_endpoints_come_from_the_query_string()
    {
        var bad = new List<string>();
        foreach (var (c, m) in Actions().Where(a => Verb(a.Method) == "GET"))
            foreach (var p in m.GetParameters().Where(p => !IsSimple(p.ParameterType)))
                if (!p.GetCustomAttributes().Any(a => a is IBindingSourceMetadata))
                    bad.Add($"{c.Name}.{m.Name}({p.ParameterType.Name} {p.Name})  [{Route(m)}]  -> حط [FromQuery]");
        bad.ShouldBeEmpty(string.Join("\n", bad));
    }

    /// <summary>A GET can be fired by a link prefetch, a crawler or a cross-site image tag: it must never delete or change data.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void Endpoints_that_delete_or_change_data_are_not_GET()
    {
        var words = new[] { "delete", "remove", "cancel", "approve", "reject", "confirm", "activate", "deactivate", "suspend" };
        var bad = Actions()
            .Where(a => Verb(a.Method) == "GET")
            .Where(a => words.Any(w => Route(a.Method).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(a => $"{a.Controller.Name}.{a.Method.Name}  [GET {Route(a.Method)}]")
            .ToList();
        bad.ShouldBeEmpty(string.Join("\n", bad));
    }
}
