using System.Net;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>
/// What happens when the time runs out: the daily expiry pass, a suspended owner who still logs in to renew,
/// and the payment that brings the center back. Dates are moved in the DB, the rest is real HTTP.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class SubscriptionExpiryFlowTests(SqlServerFixture fx) : OnboardingFlowBase(fx)
{
    [Fact]
    public async Task An_expired_center_is_suspended_and_the_owner_renews_pays_and_gets_it_back()
    {
        var o = await OnboardAsync();
        var (admin, doctorId) = await SetUpBookableAsync(o);
        var patient = await RegisterPatientAtAsync(o.CenterId);
        var date = NextMonday();
        var sub = (await SubscriptionsAsync(o.CenterId)).Single();

        // the paid period runs out and the daily job runs
        await SetSubscriptionDatesAsync(sub.Id, ClinicClock.Today.AddDays(-30), ClinicClock.Today);
        await RunExpiryJobAsync();

        (await SubscriptionsAsync(o.CenterId)).Single().Status.ShouldBe(SubscriptionStatus.Expired);
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Suspended);
        (await SearchListsAsync(o.Name)).ShouldBeFalse();
        (await BookAtAsync(o.CenterId, doctorId, patient.Token, date)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // the owner can still log in, see the center and the subscription, because that is how he renews
        var ownerToken = await OwnerLoginAsync(o);
        (await GetAsync($"/Center/admin-get-my-center/{o.CenterId}", ownerToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync($"/RenewSubscription/my-subscription/{o.CenterId}", ownerToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // renew, pay, the super admin confirms
        var renew = await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-expird-subscription/{o.CenterId}", ownerToken, new { newPlanId = o.PlanId });
        renew.StatusCode.ShouldBe(HttpStatusCode.OK, await renew.Content.ReadAsStringAsync());
        var queued = (await SubscriptionsAsync(o.CenterId)).Single(s => s.Id != sub.Id);
        queued.Status.ShouldBe(SubscriptionStatus.Pending);

        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Suspended, "paying has not been confirmed yet");

        var paymentId = await OwnerPaysAsync(o, ownerToken, queued.Id);
        (await SendAsync(HttpMethod.Post, $"/SubscriptionPayment/superAdmin-confirm-payment/{paymentId}", o.SuperToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // back to normal
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Active);
        (await SubscriptionsAsync(o.CenterId)).Single(s => s.Id == queued.Id).Status.ShouldBe(SubscriptionStatus.Active);
        (await SearchListsAsync(o.Name)).ShouldBeTrue();
        var booked = await OkJsonAsync(await BookAtAsync(o.CenterId, doctorId, patient.Token, date));
        booked.GetProperty("success").GetBoolean().ShouldBeTrue(booked.ToString());
    }

    [Fact]
    public async Task The_owner_of_a_suspended_center_can_still_log_in_and_see_his_center()
    {
        var o = await OnboardAsync();
        await SendAsync(HttpMethod.Patch, $"/Center/superAdmin-suspened-center/{o.CenterId}", o.SuperToken);
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Suspended);

        var token = await OwnerLoginAsync(o);
        (await GetAsync($"/Center/admin-get-my-center/{o.CenterId}", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync($"/RenewSubscription/my-subscription/{o.CenterId}", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_renewal_that_was_paid_and_confirmed_early_keeps_the_center_running_when_the_old_period_ends()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);
        var old = (await SubscriptionsAsync(o.CenterId)).Single();

        await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-active-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId });
        var queued = (await SubscriptionsAsync(o.CenterId)).Single(s => s.Id != old.Id);
        var paymentId = await OwnerPaysAsync(o, admin, queued.Id);
        (await SendAsync(HttpMethod.Post, $"/SubscriptionPayment/superAdmin-confirm-payment/{paymentId}", o.SuperToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // time passes: the old period ends today, the new one starts today
        await SetSubscriptionDatesAsync(old.Id, ClinicClock.Today.AddDays(-30), ClinicClock.Today);
        await SetSubscriptionDatesAsync(queued.Id, ClinicClock.Today, ClinicClock.Today.AddDays(30));
        await RunExpiryJobAsync();

        var subs = await SubscriptionsAsync(o.CenterId);
        subs.Single(s => s.Id == old.Id).Status.ShouldBe(SubscriptionStatus.Expired);
        subs.Single(s => s.Id == queued.Id).Status.ShouldBe(SubscriptionStatus.Active);
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Active);
        (await SearchListsAsync(o.Name)).ShouldBeTrue();
    }

    [Fact]
    public async Task A_renewal_that_was_never_paid_does_not_keep_the_center_running()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);
        var old = (await SubscriptionsAsync(o.CenterId)).Single();

        await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-active-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId });
        var queued = (await SubscriptionsAsync(o.CenterId)).Single(s => s.Id != old.Id);

        await SetSubscriptionDatesAsync(old.Id, ClinicClock.Today.AddDays(-30), ClinicClock.Today);
        await SetSubscriptionDatesAsync(queued.Id, ClinicClock.Today, ClinicClock.Today.AddDays(30));
        await RunExpiryJobAsync();

        (await SubscriptionsAsync(o.CenterId)).Single(s => s.Id == old.Id).Status.ShouldBe(SubscriptionStatus.Expired);
        (await SubscriptionsAsync(o.CenterId)).Single(s => s.Id == queued.Id).Status.ShouldBe(SubscriptionStatus.Pending);
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Suspended);

        // paying afterwards brings it back
        var paymentId = await OwnerPaysAsync(o, admin, queued.Id);
        (await SendAsync(HttpMethod.Post, $"/SubscriptionPayment/superAdmin-confirm-payment/{paymentId}", o.SuperToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Active);
    }

    [Fact]
    public async Task The_expiry_job_leaves_a_running_center_alone()
    {
        var o = await OnboardAsync();
        await RunExpiryJobAsync();

        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Active);
        (await SubscriptionsAsync(o.CenterId)).Single().Status.ShouldBe(SubscriptionStatus.Active);
        (await SearchListsAsync(o.Name)).ShouldBeTrue();
    }
}
