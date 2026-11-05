using NSubstitute;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Subscriptions;

/// <summary>
/// The daily background job (expire + suspend, 7-day reminder). Its passes are private, so they are invoked by reflection;
/// making them internal (InternalsVisibleTo) would be cleaner.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SubscriptionJobTests(SqlServerFixture fx) : SubscriptionTestBase(fx)
{
    private const string ExpirePass = "CheckAndActivateSubscriptions";
    private const string ReminderPass = "SendRenewalReminders";

    // ---------- expiry ----------

    [Theory]
    [InlineData(0)]     // ends today
    [InlineData(-3)]    // ended earlier (job was down)
    public async Task A_subscription_that_ended_is_expired_and_the_center_is_suspended_with_an_email(int endsInDays)
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-30), Today.AddDays(endsInDays));
        var email = await SetCenterAsync(CenterStatus.Active);

        await RunJobAsync(ExpirePass);

        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Expired);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);
        await Email.Received(1).SendCustomMessageAsync(email, Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_subscription_that_has_not_ended_is_left_alone()
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-10), Today.AddDays(1));
        var email = await SetCenterAsync(CenterStatus.Active);

        await RunJobAsync(ExpirePass);

        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Active);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Active);
        await Email.DidNotReceive().SendCustomMessageAsync(email, Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(SubscriptionStatus.Pending)]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Expired)]
    public async Task Only_Active_subscriptions_are_expired(SubscriptionStatus status)
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, status, Today.AddDays(-30), Today.AddDays(-1));
        await SetCenterAsync(CenterStatus.Active);

        await RunJobAsync(ExpirePass);

        (await SubscriptionAsync(sub)).Status.ShouldBe(status);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Active);
    }

    [Fact]
    public async Task A_center_without_an_admin_email_is_still_suspended_without_crashing()
    {
        var plan = await AddPlanAsync();
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-30), Today);
        await SetCenterAsync(CenterStatus.Active, withEmail: false);

        await RunJobAsync(ExpirePass);

        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Expired);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Suspended);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_center_that_already_renewed_stays_active_when_the_old_subscription_ends()
    {
        // The renewal was confirmed early: old subscription still "Active" until today, new one "Active" from today on.
        var plan = await AddPlanAsync();
        var old = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-30), Today);
        var renewed = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today, Today.AddDays(30));
        await SetCenterAsync(CenterStatus.Active);

        await RunJobAsync(ExpirePass);

        (await SubscriptionAsync(old)).Status.ShouldBe(SubscriptionStatus.Expired);
        (await SubscriptionAsync(renewed)).Status.ShouldBe(SubscriptionStatus.Active);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Active);
    }

    // ---------- reminder ----------

    [Theory]
    [InlineData(7, true)]
    [InlineData(6, false)]
    [InlineData(8, false)]
    public async Task Reminder_goes_out_only_when_exactly_seven_days_are_left(int daysLeft, bool sent)
    {
        var plan = await AddPlanAsync();
        var end = Today.AddDays(daysLeft);
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-20), end);
        var email = await SetCenterAsync(CenterStatus.Active);

        await RunJobAsync(ReminderPass);

        if (sent)
            await Email.Received(1).SendSubscriptionRenewalReminderAsync(email, Arg.Any<string>(), end);
        else
            await Email.DidNotReceive().SendSubscriptionRenewalReminderAsync(email, Arg.Any<string>(), Arg.Any<DateOnly>());
    }

    [Fact]
    public async Task No_reminder_for_a_center_without_email_or_for_a_non_active_subscription()
    {
        var plan = await AddPlanAsync();
        await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today, Today.AddDays(7));
        var email = await SetCenterAsync(CenterStatus.Active);

        await RunJobAsync(ReminderPass);
        await Email.DidNotReceive().SendSubscriptionRenewalReminderAsync(email, Arg.Any<string>(), Arg.Any<DateOnly>());

        await SetCenterAsync(CenterStatus.Active, withEmail: false);
        await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-20), Today.AddDays(7));
        await Should.NotThrowAsync(() => RunJobAsync(ReminderPass));
    }
}
