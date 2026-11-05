using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Subscriptions;

[Collection(SqlServerCollection.Name)]
public class SubscriptionPaymentTests(SqlServerFixture fx) : SubscriptionTestBase(fx)
{
    private static PaymentRequestExist Req(int subscriptionId, string method = "bank", IFormFile? file = null) =>
        new() { SubscriptionId = subscriptionId, PaymentMethod = method, ReferenceNumber = "REF-1", Notes = "n", ReceiptImage = file };

    private static IFormFile Fake(string fileName, long length)
    {
        var f = Substitute.For<IFormFile>();
        f.FileName.Returns(fileName);
        f.Length.Returns(length);
        return f;
    }

    private async Task<(int Plan, int Sub)> PendingSubscriptionAsync(decimal price = 120m, params int[] features)
    {
        var plan = await AddPlanAsync(price, 30, true, features);
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today, Today.AddDays(30), price);
        return (plan, sub);
    }

    // ---------- record (center admin uploads the proof) ----------

    [Fact]
    public async Task Recording_creates_an_unconfirmed_payment_for_the_subscription_price()
    {
        var (_, sub) = await PendingSubscriptionAsync(120m);

        var r = await NewPayments().RecordPaymentAsync(Req(sub, "bank"), C.Id);

        r.IsConfirmed.ShouldBeFalse();
        r.Amount.ShouldBe(120m);
        r.SubscriptionId.ShouldBe(sub);
        r.CenterId.ShouldBe(C.Id);
        r.PaymentMethod.ShouldBe("bank");
        r.PaidAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromMinutes(2));

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.subscriptionPayments.AsNoTracking().SingleAsync(p => p.Id == r.Id)).RecordedBySuperAdminId.ShouldBeNull();
        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Pending);      // not active until a super admin confirms
    }

    [Fact]
    public async Task A_second_payment_for_the_same_subscription_is_rejected()
    {
        var (_, sub) = await PendingSubscriptionAsync();
        await NewPayments().RecordPaymentAsync(Req(sub), C.Id);

        await ShouldFailWith("Payment.Exists", () => NewPayments().RecordPaymentAsync(Req(sub), C.Id));
    }

    [Fact]
    public async Task Only_pending_subscriptions_of_this_center_accept_a_payment()
    {
        var plan = await AddPlanAsync();
        var active = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, Today.AddDays(-5), Today.AddDays(25));
        var other = await Fx.Harness.CreateCenterAsync();
        var foreign = await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today, Today.AddDays(30), centerId: other.Id);

        await ShouldFailWith("Subscription.NotFound", () => NewPayments().RecordPaymentAsync(Req(active), C.Id));
        await ShouldFailWith("Subscription.NotFound", () => NewPayments().RecordPaymentAsync(Req(foreign), C.Id));
        await ShouldFailWith("Subscription.NotFound", () => NewPayments().RecordPaymentAsync(Req(999_999), C.Id));
        await ShouldFailWith("Center.NotFound", () => NewPayments().RecordPaymentAsync(Req(active), 999_999));
    }

    [Fact]
    public async Task Receipt_must_be_jpg_png_or_pdf_and_at_most_5MB()
    {
        var (_, sub) = await PendingSubscriptionAsync();

        await ShouldFailWith("Validation.InvalidFileType", () => NewPayments().RecordPaymentAsync(Req(sub, file: Fake("virus.exe", 100)), C.Id));
        await ShouldFailWith("Validation.FileTooLarge", () => NewPayments().RecordPaymentAsync(Req(sub, file: Fake("big.pdf", 5 * 1024 * 1024 + 1)), C.Id));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_amount_is_the_price_quoted_when_the_renewal_was_requested_not_the_current_plan_price()
    {
        var (plan, sub) = await PendingSubscriptionAsync(120m);
        await NewPlans().EditPlanAsync(plan, new EditPalnRequestDto { price = 200m });     // price changes after the quote

        var r = await NewPayments().RecordPaymentAsync(Req(sub), C.Id);

        r.Amount.ShouldBe(120m);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_uploads_for_the_same_subscription_at_once_give_one_payment()
    {
        var (_, sub) = await PendingSubscriptionAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await NewPayments().RecordPaymentAsync(Req(sub), C.Id); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.subscriptionPayments.CountAsync(p => p.SubscriptionId == sub)).ShouldBe(1);
        results.Count(e => e == null).ShouldBe(1);
    }

    // ---------- confirm (super admin) ----------

    [Fact]
    public async Task Confirming_activates_the_subscription_reactivates_the_center_and_swaps_the_features()
    {
        var f1 = await AddFeatureAsync(); var f2 = await AddFeatureAsync(); var f3 = await AddFeatureAsync();
        var (_, sub) = await PendingSubscriptionAsync(120m, f2, f3);
        var payment = await AddSubPaymentAsync(sub, 120m);
        var superAdmin = await AddSuperAdminAsync();
        var email = await SetCenterAsync(CenterStatus.Suspended);
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            shared.CenterFeatures.Add(new Sehatak.Domain.Entities.SharedEntities.CenterFeature { CenterId = C.Id, FeatureId = f1, IsEnabled = true });
            await shared.SaveChangesAsync();
        }

        (await NewPayments().ConfirmPaymentAsync(payment, superAdmin)).ShouldBeTrue();

        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Active);
        (await CenterStatusAsync()).ShouldBe(CenterStatus.Active);
        (await EnabledFeaturesAsync()).ShouldBe(new[] { f2, f3 }.OrderBy(x => x));
        await using var check = Fx.Harness.NewSharedDb();
        (await check.subscriptionPayments.AsNoTracking().SingleAsync(p => p.Id == payment)).RecordedBySuperAdminId.ShouldBe(superAdmin);
        await Email.Received(1).SendPaymentConfirmedAsync(email, Arg.Any<string>(), 120m);
    }

    [Fact]
    public async Task Confirming_twice_is_rejected()
    {
        var (_, sub) = await PendingSubscriptionAsync();
        var payment = await AddSubPaymentAsync(sub);
        var superAdmin = await AddSuperAdminAsync();
        await NewPayments().ConfirmPaymentAsync(payment, superAdmin);

        await ShouldFailWith("Payment.AlreadyConfirmed", () => NewPayments().ConfirmPaymentAsync(payment, superAdmin));
    }

    [Fact]
    public async Task Confirm_rejects_unknown_payment_or_a_subscription_that_is_no_longer_pending()
    {
        var superAdmin = await AddSuperAdminAsync();
        await ShouldFailWith("Payment.NotFound", () => NewPayments().ConfirmPaymentAsync(999_999, superAdmin));

        var plan = await AddPlanAsync();
        var cancelled = await AddSubscriptionAsync(plan, SubscriptionStatus.Cancelled, Today.AddDays(-5), Today.AddDays(25));
        var payment = await AddSubPaymentAsync(cancelled);
        await ShouldFailWith("Subscription.AlreadyProcessed", () => NewPayments().ConfirmPaymentAsync(payment, superAdmin));
    }

    [Fact]
    public async Task Confirm_rejects_a_payment_that_has_no_subscription()
    {
        var superAdmin = await AddSuperAdminAsync();
        int paymentId;
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            var p = new Sehatak.Domain.Entities.SharedEntities.SubscriptionPayment { CenterId = C.Id, Amount = 10m, PaymentMethod = "bank", PaidAt = DateTime.UtcNow };
            shared.subscriptionPayments.Add(p);
            await shared.SaveChangesAsync();
            paymentId = p.Id;
        }

        await ShouldFailWith("Subscription.NotFound", () => NewPayments().ConfirmPaymentAsync(paymentId, superAdmin));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_failing_confirmation_email_does_not_turn_a_successful_confirmation_into_an_error()
    {
        var (_, sub) = await PendingSubscriptionAsync();
        var payment = await AddSubPaymentAsync(sub);
        var superAdmin = await AddSuperAdminAsync();
        await SetCenterAsync(CenterStatus.Suspended);
        Email.SendPaymentConfirmedAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("smtp down")));

        await Should.NotThrowAsync(() => NewPayments().ConfirmPaymentAsync(payment, superAdmin));
        (await SubscriptionAsync(sub)).Status.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_super_admins_confirming_at_once_give_one_confirmation_and_one_business_error()
    {
        var f = await AddFeatureAsync();
        var (_, sub) = await PendingSubscriptionAsync(100m, f);
        var payment = await AddSubPaymentAsync(sub);
        var a = await AddSuperAdminAsync(); var b = await AddSuperAdminAsync();

        var results = await Task.WhenAll(new[] { a, b }.Select(async id =>
        {
            try { await NewPayments().ConfirmPaymentAsync(payment, id); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        results.Count(e => e == null).ShouldBe(1);
        results.Single(e => e != null).ShouldBeOfType<BusinessException>();
        (await EnabledFeaturesAsync()).Count.ShouldBe(1);
    }

    // ---------- lists ----------

    [Fact]
    public async Task Center_payments_are_newest_first_and_show_confirmation_state()
    {
        var plan = await AddPlanAsync();
        var s1 = await AddSubscriptionAsync(plan, SubscriptionStatus.Expired, Today.AddDays(-60), Today.AddDays(-30));
        var s2 = await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today, Today.AddDays(30));
        var superAdmin = await AddSuperAdminAsync();
        var confirmed = await AddSubPaymentAsync(s1, confirmedBy: superAdmin);
        await Task.Delay(20);
        var waiting = await AddSubPaymentAsync(s2);

        var page = await NewPayments().GetCenterPaymentsAsync(C.Id, new PagedRequest());

        page.Items.Select(i => i.Id).ShouldBe(new[] { waiting, confirmed });
        page.Items[0].IsConfirmed.ShouldBeFalse();
        page.Items[0].RecordedBy.ShouldBe("Pending");
        page.Items[1].IsConfirmed.ShouldBeTrue();
        await ShouldFailWith("Center.NotFound", () => NewPayments().GetCenterPaymentsAsync(999_999, new PagedRequest()));
    }

    [Fact]
    public async Task Pending_list_has_only_unconfirmed_payments_that_do_not_belong_to_a_registration_request()
    {
        var plan = await AddPlanAsync();
        var s1 = await AddSubscriptionAsync(plan, SubscriptionStatus.Pending, Today, Today.AddDays(30));
        var s2 = await AddSubscriptionAsync(plan, SubscriptionStatus.Expired, Today.AddDays(-60), Today.AddDays(-30));
        var superAdmin = await AddSuperAdminAsync();
        var waiting = await AddSubPaymentAsync(s1);
        var done = await AddSubPaymentAsync(s2, confirmedBy: superAdmin);

        var ids = (await NewPayments().GetPendingPaymentsAsync(new PagedRequest { PageSize = 50 })).Items.Select(i => i.Id).ToList();

        ids.ShouldContain(waiting);
        ids.ShouldNotContain(done);
    }
}
