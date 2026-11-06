using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.CenterRegistration;

/// <summary>
/// Approving builds a real center: shared rows + a migrated tenant database + the owner's admin account.
/// Each approval creates a database, so these tests are a bit slower than the others.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ApproveAndRejectTests(SqlServerFixture fx) : RegistrationTestBase(fx)
{
    // ---------- approve: the happy path ----------

    [Fact]
    public async Task Approving_creates_the_center_subscription_features_and_the_owners_admin_account()
    {
        var f1 = await AddFeatureAsync(); var f2 = await AddFeatureAsync();
        var plan = await AddPlanAsync(250m, 90, true, f1, f2);
        var name = UniqueName("Alshifa");
        var (requestId, paymentId, sa, dto) = await ApprovableAsync(plan, name);

        (await NewService().ApproveCenterRequest(requestId, sa)).ShouldBeTrue();

        var request = await RequestRowAsync(requestId);
        request.Status.ShouldBe(CenterRegistrationStatus.Approved);
        request.ReviewedBySuperAdminId.ShouldBe(sa);
        request.ReviewedAt.ShouldNotBeNull();
        var centerId = request.CreatedCenterId!.Value;

        var center = await CenterRowAsync(centerId);
        center.Name.ShouldBe(name);
        center.CenterStatus.ShouldBe(CenterStatus.Active);
        center.AdminEmail.ShouldBe(dto.AdminEmail);
        center.Address.ShouldBe(dto.CenterAddress);
        center.Phone.ShouldBe(dto.CenterPhone);
        center.UniqueUrl.ShouldEndWith(".sehatak.com");

        await using var shared = Fx.Harness.NewSharedDb();
        var sub = await shared.CenterSubscriptions.AsNoTracking().SingleAsync(s => s.CenterId == centerId);
        sub.Status.ShouldBe(SubscriptionStatus.Active);
        sub.PlanId.ShouldBe(plan);
        sub.AmountPaid.ShouldBe(250m);
        sub.StartDate.ShouldBe(ClinicClock.Today);

        var payment = await shared.subscriptionPayments.AsNoTracking().SingleAsync(p => p.Id == paymentId);
        payment.CenterId.ShouldBe(centerId);
        payment.SubscriptionId.ShouldBe(sub.Id);

        (await shared.CenterFeatures.AsNoTracking().Where(c => c.CenterId == centerId && c.IsEnabled).Select(c => c.FeatureId).OrderBy(x => x).ToListAsync())
            .ShouldBe(new[] { f1, f2 }.OrderBy(x => x));

        await using var tenant = Fx.Harness.Tenant(centerId);
        var admin = await tenant.Users.AsNoTracking().SingleAsync(u => u.email == dto.AdminEmail);
        admin.role.ShouldBe(userRole.Admin);
        admin.isActive.ShouldBeTrue();
        admin.firstName.ShouldBe("Omar");
        BCrypt.Net.BCrypt.Verify(Pwd, admin.passwordHash).ShouldBeTrue();      // the owner can log in with the password he chose

        await Email.Received().SendCustomMessageAsync(dto.AdminEmail, Arg.Is<string>(s => s.Contains("Approve")), Arg.Any<string>());
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_subscription_lasts_exactly_the_plan_days_from_its_clinic_start_date()
    {
        // EndDate is computed from UtcNow while StartDate uses the clinic date: they differ between 00:00 and 03:00 clinic time.
        var plan = await AddPlanAsync(days: 30);
        var (requestId, _, sa, _) = await ApprovableAsync(plan);
        await NewService().ApproveCenterRequest(requestId, sa);

        var centerId = (await RequestRowAsync(requestId)).CreatedCenterId!.Value;
        await using var shared = Fx.Harness.NewSharedDb();
        var sub = await shared.CenterSubscriptions.AsNoTracking().SingleAsync(s => s.CenterId == centerId);
        sub.EndDate.ShouldBe(sub.StartDate.AddDays(30));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Prepayment_and_refund_settings_reach_the_new_center()
    {
        var plan = await AddPlanAsync();
        var (requestId, _, sa, _) = await ApprovableAsync(plan, tweak: d =>
        {
            d.RequiresPrepayment = true; d.PrepaymentAmount = 50m; d.RefundPolicyHours = 12; d.PartialRefundPercent = 30m;
        });
        await NewService().ApproveCenterRequest(requestId, sa);

        var center = await CenterRowAsync((await RequestRowAsync(requestId)).CreatedCenterId!.Value);
        center.RequiresPrepayment.ShouldBeTrue();
        center.PrepaymentAmount.ShouldBe(50m);
        center.RefundPolicyHours.ShouldBe(12);
        center.PartialRefundPercent.ShouldBe(30m);
    }

    // ---------- approve: guards ----------

    [Fact]
    public async Task Only_a_real_super_admin_can_approve_and_the_request_must_exist()
    {
        var plan = await AddPlanAsync();
        var (requestId, _, sa, _) = await ApprovableAsync(plan);

        await ShouldFailWith("Auth.Unauthorized", () => NewService().ApproveCenterRequest(requestId, 999_999));
        await ShouldFailWith("Center.NotFound", () => NewService().ApproveCenterRequest(999_999, sa));
        (await RequestRowAsync(requestId)).Status.ShouldBe(CenterRegistrationStatus.Pending);
    }

    [Fact]
    public async Task Approving_needs_a_confirmed_payment()
    {
        var plan = await AddPlanAsync();
        var sa = await AddSuperAdminAsync();
        var (noPayment, _) = await NewRequestAsync(plan);
        var (unconfirmed, _) = await NewRequestAsync(plan);
        await AddRegistrationPaymentAsync(unconfirmed);

        await ShouldFailWith("CenterRegistration.PaymentNotConfirmed", () => NewService().ApproveCenterRequest(noPayment, sa));
        await ShouldFailWith("CenterRegistration.PaymentNotConfirmed", () => NewService().ApproveCenterRequest(unconfirmed, sa));
    }

    [Fact]
    public async Task A_request_can_be_approved_only_once()
    {
        var plan = await AddPlanAsync();
        var (requestId, _, sa, _) = await ApprovableAsync(plan);
        await NewService().ApproveCenterRequest(requestId, sa);

        await ShouldFailWith("CenterRegistration.AlreadyReviewed", () => NewService().ApproveCenterRequest(requestId, sa));
    }

    [Fact]
    public async Task Two_centers_whose_names_give_the_same_url_cannot_both_be_approved()
    {
        var plan = await AddPlanAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (first, _, sa, _) = await ApprovableAsync(plan, $"Noor {tag}");
        var (second, _, _, _) = await ApprovableAsync(plan, $"noor-{tag}");          // different text, same slug
        await NewService().ApproveCenterRequest(first, sa);

        await ShouldFailWith("Center.UrlExists", () => NewService().ApproveCenterRequest(second, sa));
        (await RequestRowAsync(second)).Status.ShouldBe(CenterRegistrationStatus.Pending);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Centers_with_Arabic_names_get_a_usable_unique_url_each()
    {
        var plan = await AddPlanAsync();
        var (first, _, sa, _) = await ApprovableAsync(plan, $"مركز الشفاء {Guid.NewGuid():N}"[..20]);
        var (second, _, _, _) = await ApprovableAsync(plan, $"مركز النور {Guid.NewGuid():N}"[..18]);

        await NewService().ApproveCenterRequest(first, sa);
        await NewService().ApproveCenterRequest(second, sa);

        var url1 = (await CenterRowAsync((await RequestRowAsync(first)).CreatedCenterId!.Value)).UniqueUrl;
        var url2 = (await CenterRowAsync((await RequestRowAsync(second)).CreatedCenterId!.Value)).UniqueUrl;
        url1.ShouldNotBe(url2);
        url1.ShouldMatch(@"^[a-z0-9]+(-[a-z0-9]+)*\.sehatak\.com$");
        url2.ShouldMatch(@"^[a-z0-9]+(-[a-z0-9]+)*\.sehatak\.com$");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_name_with_dots_and_apostrophes_gives_a_valid_web_address()
    {
        var plan = await AddPlanAsync();
        var (id, _, sa, _) = await ApprovableAsync(plan, $"Dr. Ali's Clinic {Guid.NewGuid():N}"[..28]);
        await NewService().ApproveCenterRequest(id, sa);

        var url = (await CenterRowAsync((await RequestRowAsync(id)).CreatedCenterId!.Value)).UniqueUrl;
        url.ShouldMatch(@"^[a-z0-9]+(-[a-z0-9]+)*\.sehatak\.com$");
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_super_admins_approving_at_once_create_one_center_and_one_business_error()
    {
        var plan = await AddPlanAsync();
        var (id, _, sa1, dto) = await ApprovableAsync(plan);
        var sa2 = await AddSuperAdminAsync();

        var results = await Task.WhenAll(new[] { sa1, sa2 }.Select(async sa =>
        {
            try { await NewService().ApproveCenterRequest(id, sa); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        results.Count(e => e == null).ShouldBe(1);
        results.Single(e => e != null).ShouldBeOfType<BusinessException>();
        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.MedicalCenters.CountAsync(c => c.Name == dto.CenterName)).ShouldBe(1);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_failing_welcome_email_does_not_turn_a_completed_approval_into_an_error()
    {
        var plan = await AddPlanAsync();
        var (id, _, sa, _) = await ApprovableAsync(plan);
        Email.SendCustomMessageAsync(default!, default!, default!).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("smtp down")));

        await Should.NotThrowAsync(() => NewService().ApproveCenterRequest(id, sa));
        (await RequestRowAsync(id)).Status.ShouldBe(CenterRegistrationStatus.Approved);
    }

    // ---------- reject ----------

    [Fact]
    public async Task Rejecting_records_the_reason_and_the_reviewer()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);
        var sa = await AddSuperAdminAsync();

        (await NewService().RejectAsync(id, sa, "Documents missing")).ShouldBeTrue();

        var row = await RequestRowAsync(id);
        row.Status.ShouldBe(CenterRegistrationStatus.Rejected);
        row.RejectionReason.ShouldBe("Documents missing");
        row.ReviewedBySuperAdminId.ShouldBe(sa);
        row.ReviewedAt.ShouldNotBeNull();
        row.CreatedCenterId.ShouldBeNull();
    }

    [Fact]
    public async Task Reject_needs_a_real_super_admin_an_existing_pending_request()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);
        var sa = await AddSuperAdminAsync();

        await ShouldFailWith("Auth.Unauthorized", () => NewService().RejectAsync(id, 999_999, "x"));
        await ShouldFailWith("CenterRegistration.NotFound", () => NewService().RejectAsync(999_999, sa, "x"));

        await NewService().RejectAsync(id, sa, "x");
        await ShouldFailWith("CenterRegistration.AlreadyReviewed", () => NewService().RejectAsync(id, sa, "again"));
    }

    [Fact]
    public async Task An_approved_request_cannot_be_rejected_afterwards()
    {
        var plan = await AddPlanAsync();
        var (id, _, sa, _) = await ApprovableAsync(plan);
        await NewService().ApproveCenterRequest(id, sa);

        await ShouldFailWith("CenterRegistration.AlreadyReviewed", () => NewService().RejectAsync(id, sa, "too late"));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_applicant_is_told_when_the_request_is_rejected()
    {
        var plan = await AddPlanAsync();
        var (id, dto) = await NewRequestAsync(plan);
        Email.ClearReceivedCalls();

        await NewService().RejectAsync(id, await AddSuperAdminAsync(), "Documents missing");

        await Email.Received(1).SendCustomMessageAsync(dto.AdminEmail, Arg.Any<string>(), Arg.Is<string>(m => m.Contains("Documents missing")));
    }
}
