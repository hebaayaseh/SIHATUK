using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.DTOs.Email;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using BulkSvc = Sehatak.Infrastructure.Services.AdminBulkEmailService;

namespace Sehatak.IntegrationTests.SuperAdmins;

/// <summary>
/// AdminBulkEmailService: the super admin mails the admin of one center, of all active centers, or of centers with an expired subscription.
/// The shared DB holds centers of other tests, so "all" targets are checked with unique addresses that belong to this test.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AdminBulkEmailTests(SqlServerFixture fx) : SuperAdminTestBase(fx)
{
    private readonly IEmailService _mail = Substitute.For<IEmailService>();
    private List<string>? _sent;
    private string? _subject;
    private string? _message;

    private BulkSvc NewSvc()
    {
        _mail.When(m => m.SendBulkAsync(Arg.Any<List<string>>(), Arg.Any<string>(), Arg.Any<string>()))
             .Do(ci => { _sent = ci.ArgAt<List<string>>(0).ToList(); _subject = ci.ArgAt<string>(1); _message = ci.ArgAt<string>(2); });
        return new BulkSvc(Shared(), _mail);
    }

    private static SendEmailDto Dto(string target, int? centerId = null) =>
        new() { Target = target, CenterId = centerId, Subject = "Maintenance", Message = "We will be down tonight." };

    private async Task<int> NewCenterAsync(string? adminEmail, CenterStatus status = CenterStatus.Active)
    {
        await using var db = Shared();
        var c = new MedicalCenter
        {
            Name = "Mail Test Center", UniqueUrl = Guid.NewGuid().ToString("N"), Phone = "0590000000",
            Address = "Hebron", CenterStatus = status, AdminEmail = adminEmail
        };
        db.MedicalCenters.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    private async Task<int> NewPlanAsync()
    {
        await using var db = Shared();
        var p = new SubscriptionPlan { Name = $"Plan-{Guid.NewGuid():N}"[..20], Price = 100m, DurationDays = 30, IsActive = true };
        db.SubscriptionPlans.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    private async Task AddSubscriptionAsync(int centerId, SubscriptionStatus status)
    {
        var plan = await NewPlanAsync();
        await using var db = Shared();
        db.CenterSubscriptions.Add(new CenterSubscription
        {
            CenterId = centerId, PlanId = plan, Status = status, AmountPaid = 100m,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30))
        });
        await db.SaveChangesAsync();
    }

    private static string Mail() => $"admin.{Guid.NewGuid():N}@test.local";

    // ---------- specific ----------

    [Fact]
    public async Task Specific_mails_only_the_admin_of_that_center_with_the_subject_and_message()
    {
        var mail = Mail();
        var center = await NewCenterAsync(mail);
        await NewCenterAsync(Mail());

        var n = await NewSvc().SendAsync(Dto("specific", center));

        n.ShouldBe(1);
        _sent.ShouldBe(new[] { mail });
        _subject.ShouldBe("Maintenance");
        _message.ShouldBe("We will be down tonight.");
    }

    [Theory]
    [InlineData(CenterStatus.Pending)]
    [InlineData(CenterStatus.Suspended)]
    public async Task Specific_also_reaches_a_center_that_is_not_active(CenterStatus status)
    {
        var mail = Mail();
        var center = await NewCenterAsync(mail, status);

        (await NewSvc().SendAsync(Dto("specific", center))).ShouldBe(1);

        _sent.ShouldBe(new[] { mail });
    }

    [Fact]
    public async Task Specific_needs_a_center_id_and_an_existing_center()
    {
        await ShouldFailWith("Validation.CenterIdRequired", () => NewSvc().SendAsync(Dto("specific", null)));
        await ShouldFailWith("Center.NotFound", () => NewSvc().SendAsync(Dto("specific", 987654)));
        await _mail.DidNotReceive().SendBulkAsync(Arg.Any<List<string>>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task A_center_without_an_admin_email_gets_nothing_and_nothing_is_sent()
    {
        var center = await NewCenterAsync(null);

        (await NewSvc().SendAsync(Dto("specific", center))).ShouldBe(0);

        await _mail.DidNotReceive().SendBulkAsync(Arg.Any<List<string>>(), Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>The check is "not null", so an empty or blank address is sent to the mail server as a recipient.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [Trait("Category", "KnownBug")]
    public async Task A_blank_admin_email_is_treated_as_no_email(string blank)
    {
        var center = await NewCenterAsync(blank);

        (await NewSvc().SendAsync(Dto("specific", center))).ShouldBe(0);

        await _mail.DidNotReceive().SendBulkAsync(Arg.Any<List<string>>(), Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------- target ----------

    [Theory]
    [InlineData("")]
    [InlineData("everyone")]
    public async Task An_unknown_target_is_refused(string target)
    {
        await ShouldFailWith("Validation.InvalidTarget", () => NewSvc().SendAsync(Dto(target)));
    }

    /// <summary>The validator accepts the target ignoring case and spaces ("Active", " specific "), the service compares exactly, so a request that passed validation fails here.</summary>
    [Theory]
    [InlineData("Active")]
    [InlineData(" active ")]
    [InlineData("EXPIRED")]
    [Trait("Category", "KnownBug")]
    public async Task A_target_the_validator_accepts_is_understood_by_the_service(string target)
    {
        await NewSvc().SendAsync(Dto(target));
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Specific_with_different_case_still_finds_the_center()
    {
        var mail = Mail();
        var center = await NewCenterAsync(mail);

        (await NewSvc().SendAsync(Dto(" Specific ", center))).ShouldBe(1);
    }

    // ---------- active ----------

    [Fact]
    public async Task Active_mails_active_centers_only_and_the_count_matches_the_list()
    {
        var active = Mail();
        var suspended = Mail();
        var pending = Mail();
        await NewCenterAsync(active);
        await NewCenterAsync(suspended, CenterStatus.Suspended);
        await NewCenterAsync(pending, CenterStatus.Pending);
        await NewCenterAsync(null);

        var n = await NewSvc().SendAsync(Dto("active"));

        _sent.ShouldNotBeNull();
        n.ShouldBe(_sent.Count);
        _sent.ShouldContain(active);
        _sent.ShouldNotContain(suspended);
        _sent.ShouldNotContain(pending);
    }

    /// <summary>One owner with two centers gets the same announcement twice (the "expired" target uses Distinct, "active" does not).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Active_mails_the_same_address_once()
    {
        var shared = Mail();
        await NewCenterAsync(shared);
        await NewCenterAsync(shared);

        var n = await NewSvc().SendAsync(Dto("active"));

        _sent!.Count(e => e == shared).ShouldBe(1);
        n.ShouldBe(_sent!.Count);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Active_skips_centers_with_a_blank_admin_email()
    {
        await NewCenterAsync("   ");

        await NewSvc().SendAsync(Dto("active"));

        _sent.ShouldNotBeNull();
        _sent.ShouldAllBe(e => !string.IsNullOrWhiteSpace(e));
    }

    // ---------- expired ----------

    [Fact]
    public async Task Expired_mails_centers_with_an_expired_subscription_once_each()
    {
        var expired = Mail();
        var twice = Mail();
        var activeOnly = Mail();
        var cancelled = Mail();
        var noEmail = await NewCenterAsync(null);
        var c1 = await NewCenterAsync(expired);
        var c2 = await NewCenterAsync(twice);
        var c3 = await NewCenterAsync(activeOnly);
        var c4 = await NewCenterAsync(cancelled);
        await AddSubscriptionAsync(c1, SubscriptionStatus.Expired);
        await AddSubscriptionAsync(c2, SubscriptionStatus.Expired);
        await AddSubscriptionAsync(c2, SubscriptionStatus.Expired);
        await AddSubscriptionAsync(c3, SubscriptionStatus.Active);
        await AddSubscriptionAsync(c4, SubscriptionStatus.Cancelled);
        await AddSubscriptionAsync(noEmail, SubscriptionStatus.Expired);

        var n = await NewSvc().SendAsync(Dto("expired"));

        n.ShouldBe(_sent!.Count);
        _sent.ShouldContain(expired);
        _sent.Count(e => e == twice).ShouldBe(1);
        _sent.ShouldNotContain(activeOnly);
        _sent.ShouldNotContain(cancelled);
    }

    /// <summary>Any old "Expired" row counts. A center that expired last year, renewed and is active now still gets "your subscription expired" mails.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Expired_skips_a_center_that_has_renewed_and_is_active_now()
    {
        var renewed = Mail();
        var center = await NewCenterAsync(renewed);
        await AddSubscriptionAsync(center, SubscriptionStatus.Expired);
        await AddSubscriptionAsync(center, SubscriptionStatus.Active);

        await NewSvc().SendAsync(Dto("expired"));

        _sent?.ShouldNotContain(renewed);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Expired_skips_a_center_with_a_blank_admin_email()
    {
        var center = await NewCenterAsync("");
        await AddSubscriptionAsync(center, SubscriptionStatus.Expired);

        await NewSvc().SendAsync(Dto("expired"));

        _sent?.ShouldAllBe(e => !string.IsNullOrWhiteSpace(e));
    }
}
