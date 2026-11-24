using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.CentersDto;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.Interfaces.IEmail;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.Infrastructure.Services.SuperAdminService.CenterService;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.SuperAdmins;

/// <summary>
/// The patient's landing page (all active centers, searchable by name / address / phone) and the admin's own center page.
/// Each test has its OWN shared database, so "all centers" means exactly the ones the test added.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CenterSearchTests(SqlServerFixture fx)
{
    private static int _range;
    private readonly IEmailService _mail = Substitute.For<IEmailService>();
    private SharedDbContext _shared = null!;

    private centerService NewSvc() => new(_shared, fx.Harness.TenantFactory, _mail);

    private static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    private async Task NewSharedAsync()
    {
        var baseId = 800_000 + 100 * Interlocked.Increment(ref _range);
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fx.Harness.NewSharedDb().Database.GetConnectionString())
        { InitialCatalog = $"center_search_{Guid.NewGuid():N}" };
        var options = new DbContextOptionsBuilder<SharedDbContext>().UseSqlServer(cs.ConnectionString).Options;
        _shared = new SharedDbContext(options);
        await _shared.Database.MigrateAsync();
        await _shared.Database.ExecuteSqlRawAsync($"DBCC CHECKIDENT ('medical_centers', RESEED, {baseId})");
    }

    private async Task<int> AddCenterAsync(string name, string address = "Hebron", string phone = "0590000000",
        CenterStatus status = CenterStatus.Active)
    {
        var c = new MedicalCenter
        {
            Name = name, UniqueUrl = Guid.NewGuid().ToString("N"), Phone = phone, Address = address,
            CenterStatus = status, CreatedAt = DateTime.UtcNow
        };
        _shared.MedicalCenters.Add(c);
        await _shared.SaveChangesAsync();
        return c.Id;
    }

    private static SearchCentersRequest Req(string? search = null, int page = 1, int size = 10) =>
        new() { Search = search, PageNumber = page, PageSize = size };

    private static List<string> Names(PagedResult<PublicCenterDto> r) => r.Items.Select(i => i.Name).ToList();

    // ---------- the landing page ----------

    [Fact]
    public async Task Without_a_search_the_patient_sees_every_active_center_by_name_and_no_other()
    {
        await NewSharedAsync();
        await AddCenterAsync("Zahra Clinic");
        await AddCenterAsync("Al Amal Clinic");
        await AddCenterAsync("Pending Clinic", status: CenterStatus.Pending);
        await AddCenterAsync("Suspended Clinic", status: CenterStatus.Suspended);

        var r = await NewSvc().SearchActiveCentersAsync(Req());

        Names(r).ShouldBe(new List<string> { "Al Amal Clinic", "Zahra Clinic" });
        r.TotalCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_search_is_the_same_as_no_search(string? search)
    {
        await NewSharedAsync();
        await AddCenterAsync("Al Amal Clinic");
        await AddCenterAsync("Zahra Clinic");

        (await NewSvc().SearchActiveCentersAsync(Req(search))).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task The_search_finds_a_center_by_a_part_of_its_name_ignoring_case()
    {
        await NewSharedAsync();
        await AddCenterAsync("Al Amal Clinic");
        await AddCenterAsync("Zahra Clinic");

        Names(await NewSvc().SearchActiveCentersAsync(Req("amal"))).ShouldBe(new List<string> { "Al Amal Clinic" });
    }

    [Fact]
    public async Task The_search_finds_a_center_by_its_address()
    {
        await NewSharedAsync();
        await AddCenterAsync("North Clinic", address: "Ramallah, Al-Masyoun");
        await AddCenterAsync("South Clinic", address: "Hebron, Ein Sara");

        Names(await NewSvc().SearchActiveCentersAsync(Req("masyoun"))).ShouldBe(new List<string> { "North Clinic" });
    }

    [Theory]
    [InlineData("0591234567")]       // exactly as stored
    [InlineData("059123")]           // the start of the number
    [InlineData("059-123-4567")]     // typed with dashes
    [InlineData("059 123 4567")]     // typed with spaces
    public async Task The_search_finds_a_center_by_its_phone_however_the_number_is_typed(string typed)
    {
        await NewSharedAsync();
        await AddCenterAsync("Phone Clinic", phone: "0591234567");
        await AddCenterAsync("Other Clinic", phone: "0569999999");

        Names(await NewSvc().SearchActiveCentersAsync(Req(typed))).ShouldBe(new List<string> { "Phone Clinic" });
    }

    [Fact]
    public async Task A_center_stored_with_a_formatted_phone_is_found_by_plain_digits()
    {
        await NewSharedAsync();
        await AddCenterAsync("Formatted Clinic", phone: "059-123 4567");

        Names(await NewSvc().SearchActiveCentersAsync(Req("0591234"))).ShouldBe(new List<string> { "Formatted Clinic" });
    }

    [Fact]
    public async Task A_search_with_no_match_is_an_empty_page_not_an_error()
    {
        await NewSharedAsync();
        await AddCenterAsync("Al Amal Clinic");

        var r = await NewSvc().SearchActiveCentersAsync(Req("nothing like this"));

        r.Items.ShouldBeEmpty();
        r.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_center_that_is_not_active_is_never_returned_even_when_it_matches()
    {
        await NewSharedAsync();
        await AddCenterAsync("Hidden Suspended", phone: "0591112222", status: CenterStatus.Suspended);
        await AddCenterAsync("Hidden Pending", phone: "0591112222", status: CenterStatus.Pending);

        (await NewSvc().SearchActiveCentersAsync(Req("Hidden"))).TotalCount.ShouldBe(0);
        (await NewSvc().SearchActiveCentersAsync(Req("0591112222"))).TotalCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[a-z]")]
    public async Task Wildcard_characters_are_just_text_and_do_not_match_everything(string search)
    {
        await NewSharedAsync();
        await AddCenterAsync("Al Amal Clinic");
        await AddCenterAsync("Zahra Clinic");

        (await NewSvc().SearchActiveCentersAsync(Req(search))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_search_cannot_break_the_query_with_quotes()
    {
        await NewSharedAsync();
        await AddCenterAsync("Al Amal Clinic");

        (await NewSvc().SearchActiveCentersAsync(Req("'; DROP TABLE medical_centers; --"))).TotalCount.ShouldBe(0);
        (await NewSvc().SearchActiveCentersAsync(Req())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Paging_returns_the_right_pages_and_the_total()
    {
        await NewSharedAsync();
        foreach (var n in new[] { "A", "B", "C", "D", "E" }) await AddCenterAsync($"Clinic {n}");

        var p1 = await NewSvc().SearchActiveCentersAsync(Req(page: 1, size: 2));
        var p2 = await NewSvc().SearchActiveCentersAsync(Req(page: 2, size: 2));
        var p3 = await NewSvc().SearchActiveCentersAsync(Req(page: 3, size: 2));

        Names(p1).ShouldBe(new List<string> { "Clinic A", "Clinic B" });
        Names(p2).ShouldBe(new List<string> { "Clinic C", "Clinic D" });
        Names(p3).ShouldBe(new List<string> { "Clinic E" });
        p1.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task The_landing_page_shows_public_details_only()
    {
        await NewSharedAsync();
        var id = await AddCenterAsync("Al Amal Clinic", address: "Hebron", phone: "0591234567");

        var item = (await NewSvc().SearchActiveCentersAsync(Req())).Items.Single();

        item.Id.ShouldBe(id);
        item.Name.ShouldBe("Al Amal Clinic");
        item.Address.ShouldBe("Hebron");
        item.Phone.ShouldBe("0591234567");
        item.UniqueUrl.ShouldNotBeNullOrWhiteSpace();

        // nothing private can be added to this DTO by accident
        var props = typeof(PublicCenterDto).GetProperties().Select(p => p.Name).ToList();
        props.ShouldNotContain("AdminEmail");
        props.ShouldNotContain("AdminWhatsappNumber");
        props.ShouldNotContain("PrepaymentAmount");
        props.ShouldNotContain("AddedBySuperAdminId");
        props.ShouldNotContain("PlanName");
    }

    // ---------- the admin's own center ----------

    [Fact]
    public async Task The_admin_sees_everything_he_entered_about_his_center_and_the_plan()
    {
        await NewSharedAsync();
        var feature = new PlatformFeature { NameOfFeature = "Lab-" + Guid.NewGuid().ToString("N")[..8] };
        _shared.PlatformFeatures.Add(feature);
        var plan = new SubscriptionPlan { Name = "Gold-" + Guid.NewGuid().ToString("N")[..8], Price = 150m, DurationDays = 30, IsActive = true };
        _shared.SubscriptionPlans.Add(plan);
        await _shared.SaveChangesAsync();
        _shared.PlanFeatures.Add(new PlanFeature { PlanId = plan.Id, FeatureId = feature.Id });

        var center = new MedicalCenter
        {
            Name = "Al Amal Clinic", UniqueUrl = "al-amal", Phone = "0591234567", Address = "Hebron", LogoUrl = "/logos/a.png",
            AdminEmail = "owner@test.local", AdminWhatsappNumber = "0597654321", BookingType = BookingType.Queue,
            RequiresPrepayment = true, PrepaymentAmount = 20m, RefundPolicyHours = 12, PartialRefundPercent = 50m,
            CenterStatus = CenterStatus.Active, CreatedAt = DateTime.UtcNow
        };
        _shared.MedicalCenters.Add(center);
        await _shared.SaveChangesAsync();
        var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5));
        var end = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25));
        _shared.CenterSubscriptions.Add(new CenterSubscription
        {
            CenterId = center.Id, PlanId = plan.Id, Status = SubscriptionStatus.Active, AmountPaid = 150m, StartDate = start, EndDate = end
        });
        await _shared.SaveChangesAsync();

        var r = await NewSvc().GetMyCenterAsync(center.Id);

        r.Id.ShouldBe(center.Id);
        r.Name.ShouldBe("Al Amal Clinic");
        r.UniqueUrl.ShouldBe("al-amal");
        r.Phone.ShouldBe("0591234567");
        r.Address.ShouldBe("Hebron");
        r.LogoUrl.ShouldBe("/logos/a.png");
        r.AdminEmail.ShouldBe("owner@test.local");
        r.AdminWhatsappNumber.ShouldBe("0597654321");
        r.BookingType.ShouldBe(BookingType.Queue);
        r.RequiresPrepayment.ShouldBeTrue();
        r.PrepaymentAmount.ShouldBe(20m);
        r.RefundPolicyHours.ShouldBe(12);
        r.PartialRefundPercent.ShouldBe(50m);
        r.CenterStatus.ShouldBe(CenterStatus.Active);
        r.PlanName.ShouldBe(plan.Name);
        r.SubscriptionStatus.ShouldBe(SubscriptionStatus.Active);
        r.SubscriptionStartDate.ShouldBe(start);
        r.SubscriptionEndDate.ShouldBe(end);
        r.FeatureNames.ShouldBe(new List<string> { feature.NameOfFeature });
    }

    [Fact]
    public async Task A_center_with_an_expired_subscription_still_shows_its_data()
    {
        await NewSharedAsync();
        var plan = new SubscriptionPlan { Name = "Old-" + Guid.NewGuid().ToString("N")[..8], Price = 100m, DurationDays = 30, IsActive = true };
        _shared.SubscriptionPlans.Add(plan);
        var id = await AddCenterAsync("Expired Clinic", status: CenterStatus.Suspended);
        await _shared.SaveChangesAsync();
        _shared.CenterSubscriptions.Add(new CenterSubscription
        {
            CenterId = id, PlanId = plan.Id, Status = SubscriptionStatus.Expired, AmountPaid = 100m,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-40)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10))
        });
        await _shared.SaveChangesAsync();

        var r = await NewSvc().GetMyCenterAsync(id);

        r.Name.ShouldBe("Expired Clinic");
        r.CenterStatus.ShouldBe(CenterStatus.Suspended);
        r.SubscriptionStatus.ShouldBe(SubscriptionStatus.Expired);
        r.PlanName.ShouldBe(plan.Name);
        r.FeatureNames.ShouldBeEmpty("an expired plan gives no features");
    }

    [Fact]
    public async Task A_center_without_any_subscription_still_shows_its_data()
    {
        await NewSharedAsync();
        var id = await AddCenterAsync("New Clinic");

        var r = await NewSvc().GetMyCenterAsync(id);

        r.Name.ShouldBe("New Clinic");
        r.PlanName.ShouldBeNull();
        r.SubscriptionStatus.ShouldBeNull();
        r.SubscriptionEndDate.ShouldBeNull();
        r.FeatureNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_there_are_old_and_active_subscriptions_the_active_one_is_shown()
    {
        await NewSharedAsync();
        var oldPlan = new SubscriptionPlan { Name = "Old-" + Guid.NewGuid().ToString("N")[..8], Price = 100m, DurationDays = 30, IsActive = true };
        var newPlan = new SubscriptionPlan { Name = "New-" + Guid.NewGuid().ToString("N")[..8], Price = 200m, DurationDays = 30, IsActive = true };
        _shared.SubscriptionPlans.AddRange(oldPlan, newPlan);
        var id = await AddCenterAsync("Renewed Clinic");
        await _shared.SaveChangesAsync();
        _shared.CenterSubscriptions.AddRange(
            new CenterSubscription
            {
                CenterId = id, PlanId = oldPlan.Id, Status = SubscriptionStatus.Expired, AmountPaid = 100m,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60))
            },
            new CenterSubscription
            {
                CenterId = id, PlanId = newPlan.Id, Status = SubscriptionStatus.Active, AmountPaid = 200m,
                StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25))
            });
        await _shared.SaveChangesAsync();

        var r = await NewSvc().GetMyCenterAsync(id);

        r.PlanName.ShouldBe(newPlan.Name);
        r.SubscriptionStatus.ShouldBe(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task An_unknown_center_is_not_found()
    {
        await NewSharedAsync();

        await ShouldFailWith("Center.NotFound", () => NewSvc().GetMyCenterAsync(999_999_999));
    }

    [Fact]
    public async Task Each_admin_gets_only_his_own_centers_data()
    {
        await NewSharedAsync();
        var a = await AddCenterAsync("Center A", phone: "0591111111");
        var b = await AddCenterAsync("Center B", phone: "0592222222");

        (await NewSvc().GetMyCenterAsync(a)).Phone.ShouldBe("0591111111");
        (await NewSvc().GetMyCenterAsync(b)).Phone.ShouldBe("0592222222");
    }
}
