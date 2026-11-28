using System.Net;
using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Api;

/// <summary>Renewing, cancelling and reactivating a center's subscription as the owner and the super admin do it.</summary>
[Collection(SqlServerCollection.Name)]
public class SubscriptionLifecycleFlowTests(SqlServerFixture fx) : OnboardingFlowBase(fx)
{
    [Fact]
    public async Task The_owner_sees_the_subscription_that_the_approval_created()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);

        var res = await OkJsonAsync(await GetAsync($"/RenewSubscription/my-subscription/{o.CenterId}", admin));
        JsonContains(res, "Active").ShouldBeTrue(res.ToString());
    }

    [Fact]
    public async Task Renewing_an_active_subscription_queues_a_pending_one_that_starts_when_the_current_ends()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);
        var before = (await SubscriptionsAsync(o.CenterId)).Single();

        var renew = await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-active-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId });
        renew.StatusCode.ShouldBe(HttpStatusCode.OK, await renew.Content.ReadAsStringAsync());

        var after = await SubscriptionsAsync(o.CenterId);
        after.Count.ShouldBe(2);
        var queued = after.Single(s => s.Id != before.Id);
        queued.Status.ShouldBe(SubscriptionStatus.Pending);
        queued.StartDate.ShouldBe(before.EndDate);

        // the current one keeps running and the center keeps working
        (await SubscriptionsAsync(o.CenterId)).Single(s => s.Id == before.Id).Status.ShouldBe(SubscriptionStatus.Active);
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Active);

        // a second renewal while one is already queued is refused
        (await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-active-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_owner_records_the_renewal_payment_and_the_super_admin_confirms_it()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);
        await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-active-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId });
        var queued = (await SubscriptionsAsync(o.CenterId)).Single(s => s.Status == SubscriptionStatus.Pending);

        var form = new MultipartFormDataContent
        {
            { new StringContent(o.CenterId.ToString()), "centerId" },
            { new StringContent(queued.Id.ToString()), "SubscriptionId" },
            { new StringContent("cash"), "PaymentMethod" },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/SubscriptionPayment/admin-record-payment/{o.CenterId}") { Content = form };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin);
        var rec = await Http.SendAsync(req);
        rec.StatusCode.ShouldBe(HttpStatusCode.OK, await rec.Content.ReadAsStringAsync());

        await using var shared = Fx.Harness.NewSharedDb();
        var payment = await shared.subscriptionPayments.AsNoTracking().SingleAsync(p => p.SubscriptionId == queued.Id);
        payment.RecordedBySuperAdminId.ShouldBeNull("not confirmed yet");

        // the owner cannot confirm his own payment
        (await SendAsync(HttpMethod.Post, $"/SubscriptionPayment/superAdmin-confirm-payment/{payment.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await SendAsync(HttpMethod.Post, $"/SubscriptionPayment/superAdmin-confirm-payment/{payment.Id}", o.SuperToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var shared2 = Fx.Harness.NewSharedDb();
        (await shared2.subscriptionPayments.AsNoTracking().SingleAsync(p => p.Id == payment.Id)).RecordedBySuperAdminId.ShouldNotBeNull();

        // confirming twice must not double anything
        (await SendAsync(HttpMethod.Post, $"/SubscriptionPayment/superAdmin-confirm-payment/{payment.Id}", o.SuperToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Renew_expired_is_refused_while_the_subscription_is_still_active()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);

        (await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-expird-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_expired_subscription_can_be_renewed_by_the_owner()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);

        await using (var shared = Fx.Harness.NewSharedDb())
        {
            var sub = await shared.CenterSubscriptions.SingleAsync(s => s.CenterId == o.CenterId);
            sub.Status = SubscriptionStatus.Expired;
            sub.StartDate = ClinicClock.Today.AddDays(-40);
            sub.EndDate = ClinicClock.Today.AddDays(-10);
            await shared.SaveChangesAsync();
        }

        var res = await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-expird-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId });
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        var subs = await SubscriptionsAsync(o.CenterId);
        subs.Count.ShouldBe(2);
        subs.Count(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Pending).ShouldBe(1);

        // and a second renewal on top of the new one is refused
        (await SendAsync(HttpMethod.Post, $"/RenewSubscription/renew-expird-subscription/{o.CenterId}", admin, new { newPlanId = o.PlanId })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancelling_suspends_the_center_and_the_super_admin_can_bring_it_back()
    {
        var o = await OnboardAsync();
        var (admin, doctorId) = await SetUpBookableAsync(o);
        var patient = await RegisterPatientAtAsync(o.CenterId);
        var date = NextMonday();
        var sub = (await SubscriptionsAsync(o.CenterId)).Single();
        var adminUser = await AdminUserIdAsync(o);

        (await SearchListsAsync(o.Name)).ShouldBeTrue();

        // the owner cancels
        var cancel = await SendAsync(HttpMethod.Post, $"/RenewSubscription/cancle-subscription/{o.CenterId}", admin, new { subscriptionId = sub.Id, userId = adminUser });
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK, await cancel.Content.ReadAsStringAsync());

        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Suspended);
        (await SubscriptionsAsync(o.CenterId)).Single().Status.ShouldBe(SubscriptionStatus.Cancelled);

        // a suspended center disappears from search and cannot take bookings
        (await SearchListsAsync(o.Name)).ShouldBeFalse();
        (await BookAtAsync(o.CenterId, doctorId, patient.Token, date)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // the super admin reactivates it (the paid period has not ended yet)
        (await SendAsync(HttpMethod.Patch, $"/Center/superAdmin-active-center/{o.CenterId}", admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var back = await SendAsync(HttpMethod.Patch, $"/Center/superAdmin-active-center/{o.CenterId}", o.SuperToken);
        back.StatusCode.ShouldBe(HttpStatusCode.OK, await back.Content.ReadAsStringAsync());

        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Active);
        (await SubscriptionsAsync(o.CenterId)).Single().Status.ShouldBe(SubscriptionStatus.Active);
        (await SearchListsAsync(o.Name)).ShouldBeTrue();

        var again = await OkJsonAsync(await BookAtAsync(o.CenterId, doctorId, patient.Token, date));
        again.GetProperty("success").GetBoolean().ShouldBeTrue(again.ToString());
    }

    [Fact]
    public async Task The_super_admin_can_suspend_a_center_and_it_stops_taking_bookings()
    {
        var o = await OnboardAsync();
        var (_, doctorId) = await SetUpBookableAsync(o);
        var patient = await RegisterPatientAtAsync(o.CenterId);

        var res = await SendAsync(HttpMethod.Patch, $"/Center/superAdmin-suspened-center/{o.CenterId}", o.SuperToken);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());

        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Suspended);
        (await SearchListsAsync(o.Name)).ShouldBeFalse();
        (await BookAtAsync(o.CenterId, doctorId, patient.Token, NextMonday())).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_cancelled_center_whose_paid_period_is_over_cannot_be_reactivated()
    {
        var o = await OnboardAsync();
        var admin = await AdminTokenAsync(o);
        var sub = (await SubscriptionsAsync(o.CenterId)).Single();
        await SendAsync(HttpMethod.Post, $"/RenewSubscription/cancle-subscription/{o.CenterId}", admin, new { subscriptionId = sub.Id, userId = await AdminUserIdAsync(o) });

        await using (var shared = Fx.Harness.NewSharedDb())
        {
            var s = await shared.CenterSubscriptions.SingleAsync(x => x.Id == sub.Id);
            s.EndDate = ClinicClock.Today.AddDays(-1);
            await shared.SaveChangesAsync();
        }

        (await SendAsync(HttpMethod.Patch, $"/Center/superAdmin-active-center/{o.CenterId}", o.SuperToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CenterAsync(o.CenterId)).CenterStatus.ShouldBe(CenterStatus.Suspended);
    }
}
