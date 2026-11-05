using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Subscriptions;

/// <summary>The admin's "my subscription" screen: what is running, what is waiting, and what the admin should do next.</summary>
[Collection(SqlServerCollection.Name)]
public class MySubscriptionTests(SqlServerFixture fx) : SubscriptionTestBase(fx)
{
    [Fact]
    public async Task A_running_subscription_far_from_the_end_needs_no_action()
    {
        var plan = await AddPlanAsync(price: 100m);
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-10), Today.AddDays(20), 100m);

        var r = await NewRenew().GetMySubscriptionAsync(C.Id);

        r.CenterId.ShouldBe(C.Id);
        r.CenterStatus.ShouldBe("Active");
        r.Current!.SubscriptionId.ShouldBe(sub);
        r.Current.DaysLeft.ShouldBe(20);
        r.Current.PlanId.ShouldBe(plan);
        r.Current.Amount.ShouldBe(100m);
        r.PaidUntil.ShouldBe(Today.AddDays(20));
        r.Pending.ShouldBeNull();
        r.NextAction.ShouldBe("None");
    }

    [Theory]
    [InlineData(7)]
    [InlineData(1)]
    public async Task Within_a_week_of_the_end_the_admin_is_asked_to_renew(int daysLeft)
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-20), Today.AddDays(daysLeft));

        (await NewRenew().GetMySubscriptionAsync(C.Id)).NextAction.ShouldBe("RenewActive");
    }

    [Fact]
    public async Task A_pending_renewal_without_a_payment_asks_for_the_payment_proof()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-20), Today.AddDays(5));
        var pending = await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today.AddDays(5), Today.AddDays(35));

        var r = await NewRenew().GetMySubscriptionAsync(C.Id);

        r.Pending!.SubscriptionId.ShouldBe(pending);
        r.PendingHasPayment.ShouldBeFalse();
        r.NextAction.ShouldBe("UploadPayment");
    }

    [Fact]
    public async Task After_the_proof_is_uploaded_the_admin_waits_for_confirmation()
    {
        var plan = await AddPlanAsync();
        var pending = await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today, Today.AddDays(30));
        await AddSubPaymentAsync(pending);
        await SetCenterAsync(CenterStatus.Suspended);

        var r = await NewRenew().GetMySubscriptionAsync(C.Id);

        r.CenterStatus.ShouldBe("Suspended");
        r.PendingHasPayment.ShouldBeTrue();
        r.NextAction.ShouldBe("WaitingConfirmation");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Expired)]
    [InlineData(SubscriptionStatus.Cancelled)]
    public async Task A_suspended_center_with_an_ended_subscription_is_asked_to_renew_it(SubscriptionStatus ended)
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, ended, Today.AddDays(-40), Today.AddDays(-10));
        await SetCenterAsync(CenterStatus.Suspended);

        var r = await NewRenew().GetMySubscriptionAsync(C.Id);

        r.Current.ShouldBeNull();
        r.PaidUntil.ShouldBeNull();
        r.LastEnded!.SubscriptionId.ShouldBe(sub);
        r.LastEnded.DaysLeft.ShouldBe(-10);
        r.NextAction.ShouldBe("RenewExpired");
    }

    [Fact]
    public async Task With_an_early_confirmed_renewal_the_current_is_the_one_ending_first_and_paid_until_is_the_later_end()
    {
        var plan = await AddPlanAsync();
        var running = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-25), Today.AddDays(5));
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(5), Today.AddDays(35));

        var r = await NewRenew().GetMySubscriptionAsync(C.Id);

        r.Current!.SubscriptionId.ShouldBe(running);
        r.PaidUntil.ShouldBe(Today.AddDays(35));
        r.NextAction.ShouldBe("None");          // already covered beyond the next week
    }

    [Fact]
    public async Task A_center_that_never_had_a_subscription_is_told_to_contact_support()
    {
        var r = await NewRenew().GetMySubscriptionAsync(C.Id);

        r.Current.ShouldBeNull();
        r.Pending.ShouldBeNull();
        r.LastEnded.ShouldBeNull();
        r.NextAction.ShouldBe("ContactSupport");
    }

    [Fact]
    public Task An_unknown_center_is_rejected() =>
        ShouldFailWith("Center.NotFound", () => NewRenew().GetMySubscriptionAsync(999_999));

    [Fact]
    public async Task Other_centers_subscriptions_are_not_mixed_in()
    {
        var plan = await AddPlanAsync();
        var other = await Fx.Harness.CreateCenterAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-10), Today.AddDays(20), centerId: other.Id);

        var r = await NewRenew().GetMySubscriptionAsync(C.Id);

        r.Current.ShouldBeNull();
        r.NextAction.ShouldBe("ContactSupport");
    }
}
