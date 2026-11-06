using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using Sehatak.Application.DTOs.Exceptions;
using RegSvc = Sehatak.Infrastructure.Services.SuperAdminService.CenterService.CenterRegistirationRequestService;

namespace Sehatak.IntegrationTests.CenterRegistration;

/// <summary>
/// No pre-made center here: the tests create requests and approving one creates a REAL center
/// (shared rows + a migrated tenant database), exactly like production.
/// </summary>
public abstract class RegistrationTestBase(SqlServerFixture fx)
{
    private static int _seq;
    protected SqlServerFixture Fx { get; } = fx;
    protected IEmailService Email { get; } = Substitute.For<IEmailService>();
    protected const string Pwd = "Str0ng!Pass";

    protected RegSvc NewService() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Email);

    protected static string UniqueName(string prefix = "Clinic") => $"{prefix} {Guid.NewGuid().ToString("N")[..10]}";
    protected static string NewEmail() => $"owner{Interlocked.Increment(ref _seq)}.{Guid.NewGuid():N}@test.local";

    protected static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    protected static CenterRegistrationRequestDto Req(int planId, string? name = null, string? email = null) => new()
    {
        CenterName = name ?? UniqueName(), CenterAddress = "Hebron", CenterPhone = "022222222",
        AdminFirstName = "Omar", AdminLastName = "Owner", AdminEmail = email ?? NewEmail(),
        AdminPhone = "0599999999", PasswordHash = Pwd, PlanId = planId
    };

    // ---------- seeding ----------

    protected async Task<int> AddPlanAsync(decimal price = 200m, int days = 30, bool active = true, params int[] featureIds)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var plan = new SubscriptionPlan { Name = $"RegPlan {Guid.NewGuid():N}"[..20], Price = price, DurationDays = days, IsActive = active };
        shared.SubscriptionPlans.Add(plan);
        await shared.SaveChangesAsync();
        foreach (var f in featureIds) shared.PlanFeatures.Add(new PlanFeature { PlanId = plan.Id, FeatureId = f });
        await shared.SaveChangesAsync();
        return plan.Id;
    }

    protected async Task<int> AddFeatureAsync()
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var f = new PlatformFeature { NameOfFeature = $"Feature {Guid.NewGuid():N}"[..20] };
        shared.PlatformFeatures.Add(f);
        await shared.SaveChangesAsync();
        return f.Id;
    }

    protected async Task<int> AddSuperAdminAsync()
    {
        var n = Interlocked.Increment(ref _seq);
        await using var shared = Fx.Harness.NewSharedDb();
        var sa = new SuperAdmin
        {
            Name = $"Super{n}", Email = $"super{n}.{Guid.NewGuid():N}@test.local", phone = $"057{n:D7}",
            role = userRole.SuperAdmin, PasswordHash = "hash-hash-hash", IsActive = true, CreateAt = DateTime.UtcNow
        };
        shared.SuperAdmins.Add(sa);
        await shared.SaveChangesAsync();
        return sa.Id;
    }

    /// <summary>Creates a Pending request through the service; returns its id.</summary>
    protected async Task<(int RequestId, CenterRegistrationRequestDto Dto)> NewRequestAsync(int planId, string? name = null, string? email = null,
        Action<CenterRegistrationRequestDto>? tweak = null)
    {
        var dto = Req(planId, name, email);
        tweak?.Invoke(dto);
        var r = await NewService().CenterRequestAsync(dto);
        return (r.Id, dto);
    }

    protected async Task<int> AddRegistrationPaymentAsync(int requestId, int? confirmedBy = null, decimal amount = 200m)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        var p = new SubscriptionPayment
        {
            RequestId = requestId, Amount = amount, PaymentMethod = "bank", PaidAt = DateTime.UtcNow, RecordedBySuperAdminId = confirmedBy
        };
        shared.subscriptionPayments.Add(p);
        await shared.SaveChangesAsync();
        return p.Id;
    }

    /// <summary>Request + confirmed payment = ready to approve.</summary>
    protected async Task<(int RequestId, int PaymentId, int SuperAdmin, CenterRegistrationRequestDto Dto)> ApprovableAsync(
        int planId, string? name = null, Action<CenterRegistrationRequestDto>? tweak = null)
    {
        var (id, dto) = await NewRequestAsync(planId, name, tweak: tweak);
        var sa = await AddSuperAdminAsync();
        var payment = await AddRegistrationPaymentAsync(id, confirmedBy: sa);
        return (id, payment, sa, dto);
    }

    // ---------- reading ----------

    protected async Task<CenterRegistrationRequest> RequestRowAsync(int id)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.centerRegistrationRequests.AsNoTracking().SingleAsync(r => r.Id == id);
    }

    protected async Task<MedicalCenter> CenterRowAsync(int id)
    {
        await using var shared = Fx.Harness.NewSharedDb();
        return await shared.MedicalCenters.AsNoTracking().SingleAsync(c => c.Id == id);
    }
}
