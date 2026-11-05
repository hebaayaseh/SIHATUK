using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AssignFeaturesWithPlan;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Services.SuperAdminService.CenterService;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Subscriptions;

[Collection(SqlServerCollection.Name)]
public class PlanAndCenterStatusTests(SqlServerFixture fx) : SubscriptionTestBase(fx)
{
    private async Task<List<ListOfPlanResponseDto>> AllPlansAsync()
    {
        var all = new List<ListOfPlanResponseDto>();
        for (var page = 1; ; page++)
        {
            var r = await NewPlans().ListOfPlanAsync(new PagedRequest { PageNumber = page, PageSize = 50 });
            all.AddRange(r.Items);
            if (r.Items.Count == 0 || all.Count >= r.TotalCount) return all;
        }
    }

    private centerService NewCenters() => new(Fx.Harness.NewSharedDb(), Fx.Harness.TenantFactory, Email);

    // ---------- plans ----------

    [Fact]
    public async Task Adding_a_plan_makes_it_active_and_visible_in_the_list()
    {
        var name = $"Gold-{Guid.NewGuid():N}";
        var r = await NewPlans().AddSubscriptionPlan(new SubscriptionPlanRequestDto { Name = name, Price = 99.5m, DurationDays = 365 });

        r.Id.ShouldBeGreaterThan(0);
        r.Price.ShouldBe(99.5m);
        (await AllPlansAsync()).ShouldContain(p => p.Id == r.Id && p.Name == name && p.DurationDays == 365);
    }

    [Fact]
    public async Task The_list_hides_retired_plans_and_shows_feature_names()
    {
        var f = await AddFeatureAsync("Chat-" + Guid.NewGuid().ToString("N"));
        var visible = await AddPlanAsync(100m, 30, true, f);
        var retired = await AddPlanAsync(active: false);

        var items = await AllPlansAsync();

        items.Single(p => p.Id == visible).PlanFeatureNames.Count.ShouldBe(1);
        items.ShouldNotContain(p => p.Id == retired);
    }

    [Fact]
    public async Task Editing_changes_only_the_fields_sent()
    {
        var plan = await AddPlanAsync(price: 100m, days: 30);

        var r = await NewPlans().EditPlanAsync(plan, new EditPalnRequestDto { price = 150m });

        r.price.ShouldBe(150m);
        r.DurationDays.ShouldBe(30);
        r.name.ShouldStartWith("Plan");
    }

    [Fact]
    public Task Editing_an_unknown_plan_is_rejected() =>
        ShouldFailWith("Subscription.PlanNotFound", () => NewPlans().EditPlanAsync(999_999, new EditPalnRequestDto { price = 1m }));

    [Fact]
    public async Task Assigning_a_feature_links_it_and_the_plan_features_list_is_sorted_by_name()
    {
        var plan = await AddPlanAsync();
        var b = await AddFeatureAsync("B-" + Guid.NewGuid().ToString("N"));
        var a = await AddFeatureAsync("A-" + Guid.NewGuid().ToString("N"));

        await NewPlans().AssignFeatureAsync(plan, new AssignFeatureToPlanRequestDto { featureId = b });
        var r = await NewPlans().AssignFeatureAsync(plan, new AssignFeatureToPlanRequestDto { featureId = a });

        r.planId.ShouldBe(plan);
        (await NewPlans().GetPlanFeaturesAsync(plan, new PagedRequest())).Items.Select(i => i.featureId).ShouldBe(new[] { a, b });
    }

    [Fact]
    public async Task Assigning_to_an_unknown_plan_or_with_an_unknown_feature_is_rejected()
    {
        var plan = await AddPlanAsync();
        await ShouldFailWith("Subscription.PlanNotFound", () => NewPlans().AssignFeatureAsync(999_999, new AssignFeatureToPlanRequestDto { featureId = 1 }));
        await ShouldFailWith("Feature.NotFound", () => NewPlans().AssignFeatureAsync(plan, new AssignFeatureToPlanRequestDto { featureId = 999_999 }));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Assigning_the_same_feature_twice_says_it_is_already_assigned_not_not_found()
    {
        var plan = await AddPlanAsync();
        var f = await AddFeatureAsync();
        await NewPlans().AssignFeatureAsync(plan, new AssignFeatureToPlanRequestDto { featureId = f });

        await ShouldFailWith("Plan.FeatureAlreadyAssigned", () => NewPlans().AssignFeatureAsync(plan, new AssignFeatureToPlanRequestDto { featureId = f }));
    }

    // ---------- super admin suspends / re-activates a center ----------

    [Fact]
    public async Task Suspending_cancels_the_active_subscription_and_suspends_the_center()
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-5), Today.AddDays(25));

        (await NewCenters().SuspendedCenter(C.Id)).ShouldBeTrue();

        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);
        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Cancelled);
    }

    [Fact]
    public async Task Suspending_needs_an_active_center_with_an_active_subscription()
    {
        await ShouldFailWith("Subscription.PlanNotFound", () => NewCenters().SuspendedCenter(C.Id));
        await SetCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Center.NotFound", () => NewCenters().SuspendedCenter(C.Id));
    }

    [Fact]
    public async Task Re_activating_restores_the_cancelled_subscription_if_it_has_not_ended()
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Cancelled, Today.AddDays(-5), Today.AddDays(25));
        await SetCenterAsync(CenterStatus.Suspended);

        (await NewCenters().ActiveCenter(C.Id)).ShouldBeTrue();

        (await CenterStatusAsync()).ShouldBe(CenterStatus.Active);
        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Re_activating_with_an_ended_subscription_is_rejected()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Cancelled, Today.AddDays(-40), Today.AddDays(-5));
        await SetCenterAsync(CenterStatus.Suspended);

        await ShouldFailWith("Center.SubscriptionExpired", () => NewCenters().ActiveCenter(C.Id));
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Re_activating_picks_the_latest_cancelled_subscription_not_an_old_one()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Cancelled, Today.AddDays(-400), Today.AddDays(-370));   // old history, inserted first
        var latest = await AddSubscriptionAsync(plan, SubscriptionStatus.Cancelled, Today.AddDays(-5), Today.AddDays(25));
        await SetCenterAsync(CenterStatus.Suspended);

        await NewCenters().ActiveCenter(C.Id);

        (await SubscriptionAsync(latest)).Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Re_activating_only_applies_to_suspended_centers_with_a_cancelled_subscription()
    {
        await ShouldFailWith("Center.NotFound", () => NewCenters().ActiveCenter(C.Id));              // center is Active
        await SetCenterAsync(CenterStatus.Suspended);
        await ShouldFailWith("Subscription.PlanNotFound", () => NewCenters().ActiveCenter(C.Id));    // nothing cancelled
    }

    // ---------- the whole journey of a suspended center's admin ----------

    [Fact]
    public async Task Expired_center_admin_renews_pays_and_after_confirmation_the_center_is_back()
    {
        var plan = await AddPlanAsync(price: 300m, days: 30);
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-30), Today);
        await SetCenterAsync(CenterStatus.Active);

        await RunJobAsync("CheckAndActivateSubscriptions");                         // night job: expires + suspends
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);

        var renewal = await NewRenew().RenewExpiredSubscriptionAsync(C.Id, new Sehatak.Application.DTOs.RenewSubscription.RenewSubscriptionRequest { newPlanId = plan });
        var payment = await NewPayments().RecordPaymentAsync(
            new Sehatak.Application.DTOs.RecordPaymentRequestDto.PaymentRequestExist { SubscriptionId = renewal.SubscriptionId, PaymentMethod = "bank" }, C.Id);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);               // still suspended until a super admin confirms

        await NewPayments().ConfirmPaymentAsync(payment.Id, await AddSuperAdminAsync());

        (await CenterStatusAsync()).ShouldBe(CenterStatus.Active);
        (await SubscriptionAsync(renewal.SubscriptionId)).Status.ShouldBe(SubscriptionStatus.Active);
    }
}
