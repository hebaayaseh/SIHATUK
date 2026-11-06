using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.CenterRegistration;

[Collection(SqlServerCollection.Name)]
public class RegistrationPaymentTests(SqlServerFixture fx) : RegistrationTestBase(fx)
{
    private static recordPaymentRequestDto Pay(string method = "bank", IFormFile? file = null) =>
        new() { PaymentMethod = method, ReferenceNumber = "REF-9", Notes = "n", ReceiptImage = file };

    private static IFormFile Fake(string fileName, long length)
    {
        var f = Substitute.For<IFormFile>();
        f.FileName.Returns(fileName);
        f.Length.Returns(length);
        return f;
    }

    // ---------- applicant uploads the proof ----------

    [Fact]
    public async Task Recording_creates_an_unconfirmed_payment_for_the_plan_price_tied_to_the_request()
    {
        var plan = await AddPlanAsync(price: 350m);
        var (id, _) = await NewRequestAsync(plan);

        (await NewService().RecordRegistrationPaymentAsync(id, Pay("bank"))).ShouldBeTrue();

        await using var shared = Fx.Harness.NewSharedDb();
        var p = await shared.subscriptionPayments.AsNoTracking().SingleAsync(x => x.RequestId == id);
        p.Amount.ShouldBe(350m);
        p.PaymentMethod.ShouldBe("bank");
        p.CenterId.ShouldBeNull();
        p.SubscriptionId.ShouldBeNull();
        p.RecordedBySuperAdminId.ShouldBeNull();
    }

    [Fact]
    public async Task One_payment_per_request_unknown_or_reviewed_requests_are_rejected()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);
        await NewService().RecordRegistrationPaymentAsync(id, Pay());

        await ShouldFailWith("Payment.Exists", () => NewService().RecordRegistrationPaymentAsync(id, Pay()));
        await ShouldFailWith("CenterRegistration.NotFound", () => NewService().RecordRegistrationPaymentAsync(999_999, Pay()));

        var (rejected, _) = await NewRequestAsync(plan);
        await NewService().RejectAsync(rejected, await AddSuperAdminAsync(), "no");
        await ShouldFailWith("CenterRegistration.AlreadyReviewed", () => NewService().RecordRegistrationPaymentAsync(rejected, Pay()));
    }

    [Fact]
    public async Task Receipt_must_be_jpg_png_or_pdf_and_at_most_5MB()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);

        await ShouldFailWith("Validation.InvalidFileType", () => NewService().RecordRegistrationPaymentAsync(id, Pay(file: Fake("x.exe", 10))));
        await ShouldFailWith("Validation.FileTooLarge", () => NewService().RecordRegistrationPaymentAsync(id, Pay(file: Fake("x.pdf", 5 * 1024 * 1024 + 1))));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Two_uploads_for_the_same_request_at_once_give_one_payment()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);

        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await NewService().RecordRegistrationPaymentAsync(id, Pay()); } catch { /* the loser may fail */ }
        }));

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.subscriptionPayments.CountAsync(p => p.RequestId == id)).ShouldBe(1);
    }

    // ---------- super admin confirms ----------

    [Fact]
    public async Task Confirming_marks_the_payment_with_the_super_admin()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);
        var payment = await AddRegistrationPaymentAsync(id);
        var sa = await AddSuperAdminAsync();

        (await NewService().ConfirmRegistrationPaymentAsync(payment, sa)).ShouldBeTrue();

        await using var shared = Fx.Harness.NewSharedDb();
        (await shared.subscriptionPayments.AsNoTracking().SingleAsync(p => p.Id == payment)).RecordedBySuperAdminId.ShouldBe(sa);
    }

    [Fact]
    public async Task Confirm_rejects_unknown_confirmed_and_renewal_payments_and_reviewed_requests()
    {
        var plan = await AddPlanAsync();
        var sa = await AddSuperAdminAsync();
        await ShouldFailWith("Payment.NotFound", () => NewService().ConfirmRegistrationPaymentAsync(999_999, sa));

        var (id, _) = await NewRequestAsync(plan);
        var payment = await AddRegistrationPaymentAsync(id, confirmedBy: sa);
        await ShouldFailWith("Payment.AlreadyConfirmed", () => NewService().ConfirmRegistrationPaymentAsync(payment, sa));

        var (rejected, _) = await NewRequestAsync(plan);
        var late = await AddRegistrationPaymentAsync(rejected);
        await NewService().RejectAsync(rejected, sa, "no");
        await ShouldFailWith("CenterRegistration.AlreadyReviewed", () => NewService().ConfirmRegistrationPaymentAsync(late, sa));

        // a subscription (renewal) payment has no RequestId: not a registration payment
        int renewal;
        await using (var shared = Fx.Harness.NewSharedDb())
        {
            var p = new Sehatak.Domain.Entities.SharedEntities.SubscriptionPayment { Amount = 1m, PaymentMethod = "bank", PaidAt = DateTime.UtcNow };
            shared.subscriptionPayments.Add(p);
            await shared.SaveChangesAsync();
            renewal = p.Id;
        }
        await ShouldFailWith("Payment.NotFound", () => NewService().ConfirmRegistrationPaymentAsync(renewal, sa));
    }

    // ---------- lists ----------

    [Fact]
    public async Task Pending_registration_payments_list_shows_unconfirmed_ones_with_the_center_name()
    {
        var plan = await AddPlanAsync();
        var name = UniqueName();
        var (id, _) = await NewRequestAsync(plan, name);
        var waiting = await AddRegistrationPaymentAsync(id);
        var (id2, _) = await NewRequestAsync(plan);
        var done = await AddRegistrationPaymentAsync(id2, confirmedBy: await AddSuperAdminAsync());

        var items = (await NewService().GetPendingRegistrationPaymentsAsync(new PagedRequest { PageSize = 100 })).Items;
        items.Single(i => i.Id == waiting).RequestId.ShouldBe(id);
        items.Single(i => i.Id == waiting).CenterName.ShouldBe(name);
        items.ShouldNotContain(i => i.Id == done);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_rejected_request_does_not_stay_in_the_pending_payments_list()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);
        var payment = await AddRegistrationPaymentAsync(id);
        await NewService().RejectAsync(id, await AddSuperAdminAsync(), "no");

        var items = (await NewService().GetPendingRegistrationPaymentsAsync(new PagedRequest { PageSize = 100 })).Items;

        items.ShouldNotContain(i => i.Id == payment);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Requests_list_gives_each_request_its_id_so_the_super_admin_can_approve_it()
    {
        var plan = await AddPlanAsync();
        var (id, _) = await NewRequestAsync(plan);

        var list = (await NewService().GetCentersRegisterationAsync(new PagedRequest { PageSize = 100 })).Items;
        var single = await NewService().GetCenterRegistrationAsync(id);

        list.ShouldContain(i => i.Id == id);
        single!.Id.ShouldBe(id);
    }

    [Fact]
    public async Task Requests_list_has_only_pending_requests_newest_first()
    {
        var plan = await AddPlanAsync();
        var a = UniqueName("A"); var b = UniqueName("B"); var c = UniqueName("C");
        await NewRequestAsync(plan, a);
        var (rejected, _) = await NewRequestAsync(plan, b);
        await NewRequestAsync(plan, c);
        await NewService().RejectAsync(rejected, await AddSuperAdminAsync(), "no");

        var names = (await NewService().GetCentersRegisterationAsync(new PagedRequest { PageSize = 100 })).Items.Select(i => i.CenterName).ToList();

        names.ShouldContain(a);
        names.ShouldContain(c);
        names.ShouldNotContain(b);
        names.IndexOf(c).ShouldBeLessThan(names.IndexOf(a));
    }

    [Fact]
    public async Task Single_request_unknown_id_is_not_found()
    {
        await ShouldFailWith("Center.NotFound", () => NewService().GetCenterRegistrationAsync(999_999));
    }
}
