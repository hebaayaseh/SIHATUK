using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ActivationJob = Sehatak.Infrastructure.Services.SuperAdminService.Background.SubscriptionActivationService;
using Sehatak.Application.Common;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// Helpers for flows that create REAL centers over HTTP (public request -> payment -> super admin approval).
/// The pre-made center C of the base class is not used by these suites.
/// </summary>
public abstract class OnboardingFlowBase(SqlServerFixture fx) : FlowTestBase(fx)
{
    protected record Onboarded(int CenterId, string Name, string AdminEmail, int RequestId, int PlanId, string SuperToken);

    private static int _n;

    protected static DateOnly NextMonday()
    {
        var d = ClinicClock.Today.AddDays(8);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        return d;
    }

    // ---------- super admin ----------

    protected async Task<string> SuperAdminLoginAsync()
    {
        var n = Interlocked.Increment(ref _n);
        var email = $"super.e2e{n}.{Guid.NewGuid():N}@test.local";
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            shared.SuperAdmins.Add(new SuperAdmin
            {
                Name = $"Super{n}", Email = email, phone = $"057{Random.Shared.Next(1_000_000, 9_999_999)}",
                role = userRole.SuperAdmin, PasswordHash = BCrypt.Net.BCrypt.HashPassword(Pwd), IsActive = true, CreateAt = DateTime.UtcNow
            });
            await shared.SaveChangesAsync();
        }

        var res = await PostAsync("/Auth/superAdmin_login", new { email, password = Pwd });
        return (await OkJsonAsync(res)).GetProperty("accessToken").GetString()!;
    }

    protected async Task<int> AddPlanAsync(decimal price = 200m, int days = 30)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var plan = new SubscriptionPlan { Name = $"E2E {Guid.NewGuid():N}"[..20], Price = price, DurationDays = days, IsActive = true };
        shared.SubscriptionPlans.Add(plan);
        await shared.SaveChangesAsync();
        return plan.Id;
    }

    // ---------- public request + payment ----------

    protected Task<HttpResponseMessage> RequestCenterAsync(int planId, string name, string email, string password = Pwd)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(name), "CenterName" },
            { new StringContent("Hebron"), "CenterAddress" },
            { new StringContent("022222222"), "CenterPhone" },
            { new StringContent("Omar"), "AdminFirstName" },
            { new StringContent("Owner"), "AdminLastName" },
            { new StringContent(email), "AdminEmail" },
            { new StringContent("0599999999"), "AdminPhone" },
            { new StringContent(password), "PasswordHash" },
            { new StringContent(planId.ToString()), "PlanId" },
        };
        return Http.PostAsync("/CenterRegistration/center-registration", form);
    }

    protected Task<HttpResponseMessage> RecordRequestPaymentAsync(int requestId)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("cash"), "PaymentMethod" },
            { new StringContent("paid at the office"), "Notes" },
        };
        return Http.PostAsync($"/CenterRegistration/{requestId}/record-payment", form);
    }

    protected async Task<int> NewRequestAsync(int planId, string name, string email)
    {
        var res = await RequestCenterAsync(planId, name, email);
        return (await OkJsonAsync(res)).GetProperty("id").GetInt32();
    }

    protected async Task<int> PaymentIdOfRequestAsync(int requestId)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.subscriptionPayments.Where(p => p.RequestId == requestId).Select(p => p.Id).SingleAsync();
    }

    /// <summary>Request -> payment -> confirm -> approve, all over HTTP. Returns the real new center.</summary>
    protected async Task<Onboarded> OnboardAsync(string? superToken = null, int? planId = null, int planDays = 30)
    {
        superToken ??= await SuperAdminLoginAsync();
        planId ??= await AddPlanAsync(days: planDays);
        var name = $"E2E Clinic {Guid.NewGuid():N}"[..24];
        var email = $"owner.{Guid.NewGuid():N}@test.local";

        var requestId = await NewRequestAsync(planId.Value, name, email);
        (await RecordRequestPaymentAsync(requestId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var paymentId = await PaymentIdOfRequestAsync(requestId);

        (await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-confirm-payments/{paymentId}", superToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var approve = await SendAsync(HttpMethod.Post, $"/CenterRegistration/superAdmin-approve-requests/{requestId}", superToken);
        (await OkJsonAsync(approve)).GetProperty("success").GetBoolean().ShouldBeTrue();

        await using var shared = Fx.Harness.NewSharedDb();
        var centerId = await shared.MedicalCenters.Where(c => c.Name == name).Select(c => c.Id).SingleAsync();
        return new Onboarded(centerId, name, email, requestId, planId.Value, superToken);
    }

    protected async Task<MedicalCenter> CenterAsync(int id)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.MedicalCenters.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    protected async Task<List<CenterSubscription>> SubscriptionsAsync(int centerId)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.CenterSubscriptions.AsNoTracking().Where(s => s.CenterId == centerId).OrderBy(s => s.Id).ToListAsync();
    }

    /// <summary>Runs the daily expiry pass of the real background service (hosted jobs are removed in the test host).</summary>
    protected async Task RunExpiryJobAsync()
    {
        var job = new ActivationJob(Api.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ActivationJob>.Instance);
        var method = typeof(ActivationJob).GetMethod("CheckAndActivateSubscriptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(job, null)!;
    }

    protected async Task SetSubscriptionDatesAsync(int subscriptionId, DateOnly start, DateOnly end, SubscriptionStatus? status = null)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var s = await shared.CenterSubscriptions.SingleAsync(x => x.Id == subscriptionId);
        s.StartDate = start;
        s.EndDate = end;
        if (status != null) s.Status = status.Value;
        await shared.SaveChangesAsync();
    }

    /// <summary>Owner records a payment for a subscription (admin endpoint), returns the payment id.</summary>
    protected async Task<int> OwnerPaysAsync(Onboarded o, string adminToken, int subscriptionId)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(o.CenterId.ToString()), "centerId" },
            { new StringContent(subscriptionId.ToString()), "SubscriptionId" },
            { new StringContent("cash"), "PaymentMethod" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/SubscriptionPayment/admin-record-payment/{o.CenterId}") { Content = form };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var res = await Http.SendAsync(req);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.subscriptionPayments.Where(p => p.SubscriptionId == subscriptionId).Select(p => p.Id).SingleAsync();
    }

    // ---------- inside the new center ----------

    protected async Task<string> OwnerLoginAsync(Onboarded o)
    {
        var res = await PostAsync($"/Auth/staff-login/{o.CenterId}", new { Email = o.AdminEmail, Password = Pwd });
        return (await OkJsonAsync(res)).GetProperty("accessToken").GetString()!;
    }

    protected async Task<int> AdminUserIdAsync(Onboarded o)
    {
        await using var db = Fx.Harness.Tenant(o.CenterId);
        return await db.Users.Where(u => u.email == o.AdminEmail).Select(u => u.Id).SingleAsync();
    }

    protected async Task<string> AdminTokenAsync(Onboarded o) => Token("Admin", o.CenterId, await AdminUserIdAsync(o));

    protected async Task<int> AddDepartmentAsync(Onboarded o, string adminToken, string name = "Cardiology")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(name), "departmentName" },
            { new StringContent("Heart care"), "departmentDescription" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/Department/admin-add-department/{o.CenterId}") { Content = form };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var res = await Http.SendAsync(req);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        await using var db = Fx.Harness.Tenant(o.CenterId);
        return await db.Departments.Where(d => d.Name == name).Select(d => d.Id).SingleAsync();
    }

    /// <summary>Adds a doctor through the admin endpoint; returns (userId, doctorId, email).</summary>
    protected async Task<(int UserId, int DoctorId, string Email)> AddDoctorViaAdminAsync(Onboarded o, string adminToken, int departmentId)
    {
        var email = $"doc.{Guid.NewGuid():N}@test.local";
        var form = new MultipartFormDataContent
        {
            { new StringContent("Layla"), "doctorFirstName" },
            { new StringContent("Haddad"), "doctorLastName" },
            { new StringContent(email), "email" },
            { new StringContent("0598888888"), "phoneNumber" },
            { new StringContent("Main street"), "address" },
            { new StringContent("Hebron"), "city" },
            { new StringContent("Cardiologist"), "Specialization" },
            { new StringContent("Ten years of experience"), "Bio" },
            { new StringContent(departmentId.ToString()), "departmentId" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/Department/admin-add-doctor-to-department/{o.CenterId}") { Content = form };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var res = await Http.SendAsync(req);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        await using var db = Fx.Harness.Tenant(o.CenterId);
        var row = await db.Doctors.Where(d => d.user.email == email).Select(d => new { d.Id, d.userId }).SingleAsync();
        return (row.userId, row.Id, email);
    }

    protected async Task<(int UserId, string Email)> AddReceptionistViaAdminAsync(Onboarded o, string adminToken)
    {
        var email = $"rec.{Guid.NewGuid():N}@test.local";
        var form = new MultipartFormDataContent
        {
            { new StringContent("Receptionist"), "userRole" },
            { new StringContent("Nour"), "FirstName" },
            { new StringContent("Desk"), "LastName" },
            { new StringContent(email), "email" },
            { new StringContent("0597777777"), "phoneNumber" },
            { new StringContent("Main street"), "address" },
            { new StringContent("Hebron"), "city" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/Staff/admin-add-staff/{o.CenterId}") { Content = form };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);
        var res = await Http.SendAsync(req);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        await using var db = Fx.Harness.Tenant(o.CenterId);
        var id = await db.Users.Where(u => u.email == email).Select(u => u.Id).SingleAsync();
        return (id, email);
    }

    /// <summary>The e-mail with the generated password goes to a fake, so the test sets a known password in the tenant DB.</summary>
    protected async Task SetPasswordAsync(int centerId, string email)
    {
        await using var db = Fx.Harness.Tenant(centerId);
        var user = await db.Users.SingleAsync(u => u.email == email);
        user.passwordHash = BCrypt.Net.BCrypt.HashPassword(Pwd);
        user.isActive = true;
        await db.SaveChangesAsync();
    }

    protected Task<HttpResponseMessage> StaffLoginAsync(int centerId, string email) =>
        PostAsync($"/Auth/staff-login/{centerId}", new { Email = email, Password = Pwd });

    protected async Task AddPricesAsync(Onboarded o, string adminToken)
    {
        var res = await SendAsync(HttpMethod.Post, $"/ServicePrice/admin-add-service-price/{o.CenterId}", adminToken,
            new { Type = (int)ServiceType.Appointment, Items = new[] { new { ServiceName = "Visit", Price = 80m } } });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    protected async Task AddMondayHoursAsync(Onboarded o, string adminToken, int doctorId)
    {
        var res = await SendAsync(HttpMethod.Post, $"/DoctorDailyHours/admin-add-doctor-hours/{o.CenterId}/{doctorId}", adminToken,
            new { DayOfWeek = (int)DayOfWeek.Monday, StartTime = "09:00:00", EndTime = "11:00:00", SlotDurationMinutes = 30 });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    /// <summary>Public patient registration at the given center: register -> code from DB -> verify -> login.</summary>
    protected async Task<(int PatientId, string Token)> RegisterPatientAtAsync(int centerId)
    {
        var email = $"pat.{Guid.NewGuid():N}@test.local";
        var form = new MultipartFormDataContent
        {
            { new StringContent("Rana"), "firstName" },
            { new StringContent("Sami"), "lastName" },
            { new StringContent(email), "email" },
            { new StringContent($"059{Random.Shared.Next(1_000_000, 9_999_999)}"), "phoneNumber" },
            { new StringContent(Pwd), "password" },
            { new StringContent("Main street"), "address" },
            { new StringContent("Hebron"), "city" },
            { new StringContent("Female"), "gender" },
            { new StringContent("A_Positive"), "bloodType" },
            { new StringContent("1995-03-04"), "DateOfBith" },
        };
        var reg = await Http.PostAsync($"/Auth/patient-register-patient/{centerId}", form);
        reg.StatusCode.ShouldBe(HttpStatusCode.OK, await reg.Content.ReadAsStringAsync());
        var userId = (await JsonAsync(reg)).GetProperty("userId").GetInt32();

        await using var db = Fx.Harness.Tenant(centerId);
        var code = await db.EmailVerificationCodes.Where(c => c.UserId == userId).Select(c => c.Code).SingleAsync();
        (await PostAsync($"/Auth/patient-verify-code/{centerId}", new { userId, code })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var login = await PostAsync($"/Auth/patient-login-patient/{centerId}", new { email, password = Pwd });
        var token = (await OkJsonAsync(login)).GetProperty("accessToken").GetString()!;
        var patientId = await db.Patients.Where(p => p.userId == userId).Select(p => p.patientId).SingleAsync();
        return (patientId, token);
    }

    /// <summary>Everything a center needs before the first booking: department, doctor with Monday hours, price.</summary>
    protected async Task<(string AdminToken, int DoctorId)> SetUpBookableAsync(Onboarded o)
    {
        var admin = await AdminTokenAsync(o);
        var dept = await AddDepartmentAsync(o, admin);
        var doc = await AddDoctorViaAdminAsync(o, admin, dept);
        await AddMondayHoursAsync(o, admin, doc.DoctorId);
        await AddPricesAsync(o, admin);
        return (admin, doc.DoctorId);
    }

    protected Task<HttpResponseMessage> BookAtAsync(int centerId, int doctorId, string patientToken, DateOnly date, string slot = "09:00:00") =>
        PostAsync($"/Appointment/patient-book/{centerId}/{doctorId}",
            new { timeSlot = slot, dateOnly = date.ToString("yyyy-MM-dd"), subPatientId = (int?)null }, patientToken);

    protected async Task<bool> SearchListsAsync(string name)
    {
        var res = await GetAsync($"/Center/patient-search-centers?Search={Uri.EscapeDataString(name)}");
        return JsonContains(await OkJsonAsync(res), name);
    }
}
