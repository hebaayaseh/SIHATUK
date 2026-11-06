using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sehatak.API.Security;
using Shouldly;
using Xunit;

namespace Sehatak.IntegrationTests.ApiSecurity;

/// <summary>
/// فحص "سطح الـ API" بالـ reflection: بدون DB وبدون سيرفر.
/// بيمسك أخطاء زي: endpoint نسي حد يحط عليه Authorize، policy مكتوبة غلط،
/// أو endpoint بدون centerId.
/// </summary>
public class AuthorizationSurfaceTests
{
    // السياسات المعرفة في Program.cs
    private static readonly HashSet<string> KnownPolicies = new()
    {
        "SuperAdminOnly","AdminOrAbove","AdminOnly","MedicalStaff","StaffShift","DoctorOnly",
        "ReceptionistOnly","PatientOnly","Emergency","LabRequest","TechnicianOnly"
    };

    private static readonly HashSet<string> KnownRoles = new()
    {
        "SuperAdmin","Admin","Doctor","Receptionist","LabTechnician","Patient","Nurse","GeneralDoctor"
    };

    private record Action(Type Controller, MethodInfo Method)
    {
        public string Name => $"{Controller.Name}.{Method.Name}";
        public IEnumerable<AuthorizeAttribute> Authorize =>
            Controller.GetCustomAttributes<AuthorizeAttribute>(true)
                .Concat(Method.GetCustomAttributes<AuthorizeAttribute>(true));
        public bool Anonymous =>
            Method.GetCustomAttribute<AllowAnonymousAttribute>(true) != null ||
            Controller.GetCustomAttribute<AllowAnonymousAttribute>(true) != null;
        public string Route
        {
            get
            {
                var c = Controller.GetCustomAttribute<RouteAttribute>(true)?.Template ?? "";
                var m = Method.GetCustomAttributes()
                    .OfType<Microsoft.AspNetCore.Mvc.Routing.IRouteTemplateProvider>()
                    .Select(a => a.Template).FirstOrDefault(t => t != null) ?? "";
                return c + "/" + m;
            }
        }
    }

    private static List<Action> AllActions()
    {
        var asm = typeof(CenterAccessFilter).Assembly;
        return asm.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().OfType<Microsoft.AspNetCore.Mvc.Routing.IActionHttpMethodProvider>().Any())
                .Select(m => new Action(t, m)))
            .ToList();
    }

    [Fact]
    public void There_are_controller_actions_to_check()
    {
        AllActions().Count.ShouldBeGreaterThan(50);
    }

    [Fact]
    public void Every_Authorize_uses_a_real_policy_and_never_a_policy_name_as_a_role()
    {
        var bad = new List<string>();
        foreach (var a in AllActions())
            foreach (var auth in a.Authorize)
            {
                if (!string.IsNullOrEmpty(auth.Policy) && !KnownPolicies.Contains(auth.Policy))
                    bad.Add($"{a.Name}: policy '{auth.Policy}' غير معرفة");
                if (!string.IsNullOrEmpty(auth.Roles))
                    foreach (var r in auth.Roles.Split(',').Select(x => x.Trim()))
                        if (!KnownRoles.Contains(r))
                            bad.Add($"{a.Name}: Roles = \"{r}\" مش رول حقيقي (إذا هاد اسم policy استعمل Policy =)");
            }
        bad.ShouldBeEmpty(string.Join("\n", bad));
    }

    /// <summary>
    /// كل endpoint لازم يكون صريح: يا Authorize يا AllowAnonymous.
    /// "بدون ولا شي" معناها مفتوح للكل بالغلط ممكن.
    /// </summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void Every_action_is_explicitly_Authorize_or_AllowAnonymous()
    {
        var open = AllActions()
            .Where(a => !a.Anonymous && !a.Authorize.Any())
            .Select(a => $"{a.Name}  [{a.Route}]")
            .ToList();
        open.ShouldBeEmpty("حط [AllowAnonymous] صريح (إذا مقصود) أو [Authorize(Policy=...)]:\n" + string.Join("\n", open));
    }

    /// <summary>
    /// أي endpoint محمي (مش anonymous) ومش SuperAdminOnly لازم يكون فيه {centerId} بالـ route
    /// أو [CenterAgnostic]، وإلا CenterAccessFilter رح يرفضه دايماً (403).
    /// </summary>
    [Fact]
    public void Center_scoped_actions_have_centerId_in_route_or_are_CenterAgnostic()
    {
        var bad = AllActions()
            .Where(a => !a.Anonymous && a.Authorize.Any())
            .Where(a => !a.Authorize.Any(x => x.Policy == "SuperAdminOnly" || (x.Roles ?? "") == "SuperAdmin"))
            .Where(a => !a.Route.Contains("{centerId", StringComparison.OrdinalIgnoreCase))
            .Where(a => a.Method.GetCustomAttribute<CenterAgnosticAttribute>() == null)
            .Select(a => $"{a.Name}  [{a.Route}]")
            .ToList();
        bad.ShouldBeEmpty(string.Join("\n", bad));
    }

    /// <summary>الـ endpoints الي مفروض تكون مفتوحة للزوار (قائمة بيضاء). أي إضافة لازم تكون مقصودة.</summary>
    [Fact]
    public void Anonymous_actions_are_only_the_expected_ones()
    {
        var allowed = new[]
{
    "Refresh","Logout","VerifyCode","ForgetPassword","ConfirmForgetPassword","SearchDoctor",
    "superAdmin_login","superAdmin-register","RecordRegistrationPayment","record-payment",
    "Login","PatientLogin","Register","PatientActiveProfile","SubmitGuestFeedback",
    // الأربعة الي ضفتلهم AllowAnonymous
    "patient-get-doctors","patient-get-doctor","patient-get-available-doctor-slot",
    "center-registration"
};
        var unexpected = AllActions()
            .Where(a => a.Anonymous)
            .Where(a => !allowed.Any(w => a.Method.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                       || a.Route.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(a => $"{a.Name}  [{a.Route}]")
            .ToList();
        unexpected.ShouldBeEmpty("endpoints مفتوحة للزوار ومش بالقائمة:\n" + string.Join("\n", unexpected));
    }

    [Fact]
    public void Admin_only_endpoints_never_allow_other_roles_by_accident()
    {
        // أي endpoint فيه "admin" بالـ route لازم يكون محمي بـ AdminOnly/AdminOrAbove/SuperAdminOnly
        var weak = AllActions()
            .Where(a => a.Route.Contains("admin", StringComparison.OrdinalIgnoreCase)
                     && !a.Route.Contains("superAdmin", StringComparison.OrdinalIgnoreCase))
            .Where(a => !a.Anonymous)
            .Where(a => a.Authorize.Any() &&
                        !a.Authorize.Any(x => x.Policy is "AdminOnly" or "AdminOrAbove" or "SuperAdminOnly"))
            .Select(a => $"{a.Name} [{a.Route}] -> {string.Join(",", a.Authorize.Select(x => x.Policy ?? x.Roles))}")
            .ToList();
        weak.ShouldBeEmpty(string.Join("\n", weak));
    }
}
