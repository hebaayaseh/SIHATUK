using Sehatak.Application.DTOs.RenewSubscription;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Subscriptions;

[Collection(SqlServerCollection.Name)]
public class RenewSubscriptionTests(SqlServerFixture fx) : SubscriptionTestBase(fx)
{
    private static RenewSubscriptionRequest Req(int planId) => new() { newPlanId = planId };

    // ---------- renew while the current subscription is still Active ----------

    [Fact]
    public async Task Renewing_an_active_subscription_creates_a_Pending_one_starting_when_the_current_ends()
    {
        var oldPlan = await AddPlanAsync();
        var newPlan = await AddPlanAsync(price: 250m, days: 90);
        var end = Today.AddDays(10);
        await AddSubscriptionAsync(oldPlan, SubscriptionStatus.Active, Today.AddDays(-20), end);

        var r = await NewRenew().RenewActiveSubscriptionAsync(C.Id, Req(newPlan));

        r.Status.ShouldBe(SubscriptionStatus.Pending);
        r.StartDate.ShouldBe(end);
        r.EndDate.ShouldBe(end.AddDays(90));
        r.Amount.ShouldBe(250m);
        r.CenterId.ShouldBe(C.Id);

        var saved = await SubscriptionAsync(r.SubscriptionId);
        saved.PlanId.ShouldBe(newPlan);
        saved.AmountPaid.ShouldBe(250m);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Active);
    }

    [Fact]
    public async Task Renewing_without_an_active_subscription_is_rejected()
    {
        var plan = await AddPlanAsync();
        await ShouldFailWith("Subscription.PlanNotFound", () => NewRenew().RenewActiveSubscriptionAsync(C.Id, Req(plan)));
    }

    [Fact]
    public async Task A_second_pending_renewal_is_rejected()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-20), Today.AddDays(10));
        await NewRenew().RenewActiveSubscriptionAsync(C.Id, Req(plan));

        await ShouldFailWith("Subscription.Renewed", () => NewRenew().RenewActiveSubscriptionAsync(C.Id, Req(plan)));
        (await SubscriptionsOfCenterAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Unknown_plan_or_center_is_rejected()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-20), Today.AddDays(10));

        await ShouldFailWith("Subscription.PlanNotFound", () => NewRenew().RenewActiveSubscriptionAsync(C.Id, Req(999_999)));
        await ShouldFailWith("Center.NotFound", () => NewRenew().RenewActiveSubscriptionAsync(999_999, Req(plan)));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Renewing_with_a_deactivated_plan_is_rejected()
    {
        var plan = await AddPlanAsync();
        var retired = await AddPlanAsync(active: false);
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-20), Today.AddDays(10));

        await ShouldFailWith("Subscription.PlanNotFound", () => NewRenew().RenewActiveSubscriptionAsync(C.Id, Req(retired)));
    }

    // ---------- renew after the subscription expired (the suspended-center admin's path) ----------

    [Fact]
    public async Task Renewing_an_expired_subscription_creates_a_Pending_one_starting_today()
    {
        var plan = await AddPlanAsync(price: 300m, days: 30);
        await AddSubscriptionAsync(plan, SubscriptionStatus.Expired, Today.AddDays(-40), Today.AddDays(-10));
        await SetCenterAsync(CenterStatus.Suspended);

        var r = await NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(plan));

        r.Status.ShouldBe(SubscriptionStatus.Pending);
        r.StartDate.ShouldBe(Today);
        r.Amount.ShouldBe(300m);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);     // stays suspended until the payment is confirmed
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Expired_renewal_ends_exactly_plan_days_after_the_clinic_start_date()
    {
        // EndDate is computed from UtcNow while StartDate uses the clinic date: they differ between 00:00 and 03:00 clinic time.
        var plan = await AddPlanAsync(days: 30);
        await AddSubscriptionAsync(plan, SubscriptionStatus.Expired, Today.AddDays(-40), Today.AddDays(-10));

        var r = await NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(plan));

        r.EndDate.ShouldBe(r.StartDate.AddDays(30));
    }

    [Fact]
    public async Task Expired_renewal_needs_an_expired_subscription_and_an_active_plan()
    {
        var plan = await AddPlanAsync();
        var retired = await AddPlanAsync(active: false);

        await ShouldFailWith("Subscription.PlanNotFound", () => NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(plan)));

        await AddSubscriptionAsync(plan, SubscriptionStatus.Expired, Today.AddDays(-40), Today.AddDays(-10));
        await ShouldFailWith("Subscription.PlanNotFound", () => NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(retired)));
        await ShouldFailWith("Center.NotFound", () => NewRenew().RenewExpiredSubscriptionAsync(999_999, Req(plan)));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_center_with_a_running_subscription_cannot_use_the_expired_renewal_just_because_of_old_history()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Expired, Today.AddDays(-400), Today.AddDays(-370));
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-10), Today.AddDays(20));

        await ShouldFailWith("Subscription.Renewed", () => NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(plan)));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Expired_renewal_twice_does_not_pile_up_pending_subscriptions()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Expired, Today.AddDays(-40), Today.AddDays(-10));
        await NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(plan));

        await ShouldFailWith("Subscription.Renewed", () => NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(plan)));
    }

    // ---------- cancel ----------

    [Fact]
    public async Task Admin_cancels_the_subscription_and_the_center_is_suspended()
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-5), Today.AddDays(25));

        (await NewRenew().CancleSubscriptionAsync(C.Id, new CancleSubcsriptionRequest { userId = C.AdminUserId, subscriptionId = sub })).ShouldBeTrue();

        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Cancelled);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);
    }

    [Fact]
    public async Task Only_the_center_admin_can_cancel_and_only_an_active_subscription_of_this_center()
    {
        var plan = await AddPlanAsync();
        var rec = await C.AddReceptionistAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-5), Today.AddDays(25));
        var pending = await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today.AddDays(25), Today.AddDays(55));

        await ShouldFailWith("Auth.Forbidden", () => NewRenew().CancleSubscriptionAsync(C.Id, new CancleSubcsriptionRequest { userId = rec, subscriptionId = sub }));
        await ShouldFailWith("Subscription.PlanNotFound", () => NewRenew().CancleSubscriptionAsync(C.Id, new CancleSubcsriptionRequest { userId = C.AdminUserId, subscriptionId = pending }));
        await ShouldFailWith("Subscription.PlanNotFound", () => NewRenew().CancleSubscriptionAsync(C.Id, new CancleSubcsriptionRequest { userId = C.AdminUserId, subscriptionId = 999_999 }));
        await ShouldFailWith("Center.NotFound", () => NewRenew().CancleSubscriptionAsync(999_999, new CancleSubcsriptionRequest { userId = C.AdminUserId, subscriptionId = sub }));
        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task After_cancelling_the_admin_is_not_stuck_and_can_start_a_new_subscription()
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-5), Today.AddDays(25));
        await NewRenew().CancleSubscriptionAsync(C.Id, new CancleSubcsriptionRequest { userId = C.AdminUserId, subscriptionId = sub });

        var r = await NewRenew().RenewExpiredSubscriptionAsync(C.Id, Req(plan));

        r.Status.ShouldBe(SubscriptionStatus.Pending);
    }
}
