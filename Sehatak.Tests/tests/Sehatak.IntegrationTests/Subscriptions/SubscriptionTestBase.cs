using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using ActivationJob = Sehatak.Infrastructure.Services.SuperAdminService.Background.SubscriptionActivationService;
using PaymentSvc = Sehatak.Infrastructure.Services.SuperAdminService.SubscriptionPaymentService.SubscriptionPaymentService;
using PlanSvc = Sehatak.Infrastructure.Services.SuperAdminService.Plans.ListOfPlanService;
using RenewSvc = Sehatak.Infrastructure.Services.SuperAdminService.Plans.RenewSubscriptionService;

namespace Sehatak.IntegrationTests.Subscriptions;

/// <summary>
/// Center (C, Active, with its admin) + shared-DB seeding helpers for plans, subscriptions, payments and features.
/// Dates are relative to the clinic's "today" so the tests never depend on the calendar.
/// </summary>
public abstract class SubscriptionTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    private static int _seq;

    protected IEmailService Email { get; } = Substitute.For<IEmailService>();
    protected static DateOnly Today => ClinicClock.Today;

    protected RenewSvc NewRenew() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory);
    protected PaymentSvc NewPayments() => new(Fx.Harness.NewSharedDb(), Substitute.For<Microsoft.Extensions.Hosting.IHostEnvironment>(), Email);
    protected PlanSvc NewPlans() => new(Fx.Harness.NewSharedDb());

    /// <summary>Runs one of the private passes of the daily background service against the real DB.</summary>
    protected async Task RunJobAsync(string privateMethod)
    {
        var services = new ServiceCollection();
        services.AddScoped<SharedDbContext>(_ => Fx.Harness.NewSharedDb());
        services.AddSingleton(Email);
        using var provider = services.BuildServiceProvider();

        var job = new ActivationJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ActivationJob>.Instance);
        var method = typeof(ActivationJob).GetMethod(privateMethod, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(job, null)!;
    }

    // ---------- seeding ----------

    protected async Task<int> AddPlanAsync(decimal price = 100m, int days = 30, bool active = true, params int[] featureIds)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var shared = Fx.Harness.NewSharedDb();
        var plan = new SubscriptionPlan { Name = $"Plan{n}", Price = price, DurationDays = days, IsActive = active };
        shared.SubscriptionPlans.Add(plan);
        await shared.SaveChangesAsync();
        foreach (var f in featureIds) shared.PlanFeatures.Add(new PlanFeature { PlanId = plan.Id, FeatureId = f });
        await shared.SaveChangesAsync();
        return plan.Id;
    }


    protected async Task<int> AddFeatureAsync(string? name = null)
    {
        var n = Interlocked.Increment(ref _seq);
        await using var shared = Fx.Harness.NewSharedDb();
        var f = new PlatformFeature { NameOfFeature = name ?? $"Feature{n}" };
        shared.PlatformFeatures.Add(f);
        await shared.SaveChangesAsync();
        return f.Id;
    }
    protected async Task<int> AddSubscriptionAsync(int planId, SubscriptionStatus status, DateOnly start, DateOnly end,
        decimal amount = 100m, int? centerId = null)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var s = new CenterSubscription
        {
            CenterId = centerId ?? C.Id, PlanId = planId, Status = status,
            StartDate = start, EndDate = end, AmountPaid = amount
        };
        shared.CenterSubscriptions.Add(s);
        await shared.SaveChangesAsync();
        return s.Id;
    }

    protected async Task<int> AddSuperAdminAsync()
    {
        var n = Interlocked.Increment(ref _seq);
        await using var shared = Fx.Harness.NewSharedDb();
        var sa = new SuperAdmin
        {
            Name = $"Super{n}", Email = $"super{n}.{Guid.NewGuid():N}@test.local", phone = $"058{n:D7}",
            role = userRole.SuperAdmin, PasswordHash = "hash-hash-hash", IsActive = true, CreateAt = DateTime.UtcNow
        };
        shared.SuperAdmins.Add(sa);
        await shared.SaveChangesAsync();
        return sa.Id;
    }

    protected async Task<int> AddSubPaymentAsync(int subscriptionId, decimal amount = 100m, int? confirmedBy = null)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var p = new SubscriptionPayment
        {
            CenterId = C.Id, SubscriptionId = subscriptionId, Amount = amount, PaymentMethod = "bank",
            PaidAt = DateTime.UtcNow, RecordedBySuperAdminId = confirmedBy
        };
        shared.subscriptionPayments.Add(p);
        await shared.SaveChangesAsync();
        return p.Id;
    }

    /// <summary>Sets the center status and a UNIQUE admin e-mail (jobs scan every center, so mail assertions are per address).</summary>
    protected async Task<string> SetCenterAsync(CenterStatus status, bool withEmail = true)
    {
        var email = withEmail ? $"admin.{Guid.NewGuid():N}@center.test" : null;
        await using var shared = Fx.Harness.NewSharedDb();
        var c = await shared.MedicalCenters.SingleAsync(x => x.Id == C.Id);
        c.CenterStatus = status;
        c.AdminEmail = email;
        await shared.SaveChangesAsync();
        return email ?? string.Empty;
    }

    // ---------- reading ----------

    protected async Task<CenterStatus> CenterStatusAsync(int? centerId = null)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return (await shared.MedicalCenters.AsNoTracking().SingleAsync(c => c.Id == (centerId ?? C.Id))).CenterStatus;
    }

    protected async Task<CenterSubscription> SubscriptionAsync(int id)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.CenterSubscriptions.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    protected async Task<List<CenterSubscription>> SubscriptionsOfCenterAsync()
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.CenterSubscriptions.AsNoTracking().Where(s => s.CenterId == C.Id).OrderBy(s => s.Id).ToListAsync();
    }

    protected async Task<List<int>> EnabledFeaturesAsync()
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.CenterFeatures.AsNoTracking().Where(f => f.CenterId == C.Id && f.IsEnabled).Select(f => f.FeatureId).OrderBy(x => x).ToListAsync();
    }
}
