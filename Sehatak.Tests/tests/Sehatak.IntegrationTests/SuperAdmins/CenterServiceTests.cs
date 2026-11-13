using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
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
/// centerService (super admin): create a center with its plan, create the center's admin, view one center, list centers.
/// Suspend / activate are in PlanAndCenterStatusTests. Each test has its OWN shared database whose center ids start in a private range,
/// so it never meets the centers or tenant databases of other tests.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class CenterServiceTests(SqlServerFixture fx)
{
    private static int _range;
    private static int _seq;
    private readonly IEmailService _mail = Substitute.For<IEmailService>();
    private SharedDbContext _shared = null!;
    private DbContextOptions<SharedDbContext> _sharedOptions = null!;

    private centerService NewSvc(TenantDbContextFactory? factory = null) => new(_shared, factory ?? fx.Harness.TenantFactory, _mail);

    private static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    private async Task NewSharedAsync()
    {
        var baseId = 700_000 + 100 * Interlocked.Increment(ref _range);
        var cs = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fx.Harness.NewSharedDb().Database.GetConnectionString())
        { InitialCatalog = $"center_svc_{Guid.NewGuid():N}" };
        _sharedOptions = new DbContextOptionsBuilder<SharedDbContext>().UseSqlServer(cs.ConnectionString).Options;
        _shared = new SharedDbContext(_sharedOptions);
        await _shared.Database.MigrateAsync();
        await _shared.Database.ExecuteSqlRawAsync($"DBCC CHECKIDENT ('medical_centers', RESEED, {baseId})");
    }

    /// <summary>Like a new HTTP request: a context with no leftovers from a failed call.</summary>
    private void NewRequestContext() => _shared = new SharedDbContext(_sharedOptions);

    private async Task<int> AddSuperAdminRowAsync()
    {
        var n = Interlocked.Increment(ref _seq);
        var sa = new SuperAdmin
        {
            Name = "Owner", Email = $"sa{n}.{Guid.NewGuid():N}@test.local", phone = $"058{n:D7}", role = userRole.SuperAdmin,
            PasswordHash = "hash-hash-hash", IsActive = true, CreateAt = DateTime.UtcNow
        };
        _shared.SuperAdmins.Add(sa);
        await _shared.SaveChangesAsync();
        return sa.Id;
    }

    private async Task<(int PlanId, List<int> FeatureIds)> AddPlanAsync(decimal price = 150m, int days = 30, bool active = true, int features = 2)
    {
        var ids = new List<int>();
        for (var i = 0; i < features; i++)
        {
            var f = new PlatformFeature { NameOfFeature = $"Feature-{Interlocked.Increment(ref _seq)}" };
            _shared.PlatformFeatures.Add(f);
            await _shared.SaveChangesAsync();
            ids.Add(f.Id);
        }
        var plan = new SubscriptionPlan { Name = $"Plan-{Guid.NewGuid():N}"[..20], Price = price, DurationDays = days, IsActive = active };
        _shared.SubscriptionPlans.Add(plan);
        await _shared.SaveChangesAsync();
        foreach (var f in ids) _shared.PlanFeatures.Add(new PlanFeature { PlanId = plan.Id, FeatureId = f });
        await _shared.SaveChangesAsync();
        return (plan.Id, ids);
    }

    private static createCenterRequestDto Req(int planId, string? name = null, int? addedBy = null) => new()
    {
        Name = name ?? $"Al Amal {Guid.NewGuid():N}", Phone = "0591234567", Address = "Hebron", PlanId = planId,
        RequiresPrepayment = true, PrepaymentAmount = 20m, RefundPolicyHours = 12, PartialRefundPercent = 50m,
        AdminWhatsappNumber = "0597654321", AdminEmail = "owner@test.local", AddedBySuperAdminId = addedBy
    };

    private async Task<int> AddCenterRowAsync(CenterStatus status = CenterStatus.Active, DateTime? createdAt = null, string? name = null)
    {
        var c = new MedicalCenter
        {
            Name = name ?? "Row Center", UniqueUrl = Guid.NewGuid().ToString("N"), Phone = "0590000000", Address = "Hebron",
            CenterStatus = status, CreatedAt = createdAt ?? DateTime.UtcNow
        };
        _shared.MedicalCenters.Add(c);
        await _shared.SaveChangesAsync();
        return c.Id;
    }

    private async Task AddSubscriptionRowAsync(int centerId, int planId, SubscriptionStatus status)
    {
        _shared.CenterSubscriptions.Add(new CenterSubscription
        {
            CenterId = centerId, PlanId = planId, Status = status, AmountPaid = 100m,
            StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), EndDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(25))
        });
        await _shared.SaveChangesAsync();
    }

    // ---------- create center ----------

    [Fact]
    public async Task Creating_a_center_makes_it_active_with_a_subscription_the_plans_features_and_its_own_database()
    {
        await NewSharedAsync();
        var (plan, features) = await AddPlanAsync(price: 150m, days: 30);
        var owner = await AddSuperAdminRowAsync();
        var req = Req(plan, "Al Amal Clinic", owner);

        var r = await NewSvc().CreateCenterAsync(req);

        r.Name.ShouldBe("Al Amal Clinic");
        r.UniqueUrl.ShouldBe("al-amal-clinic.sehatak.com");
        r.Status.ShouldBe("Active");
        r.EnabledFeatures.Count.ShouldBe(2);

        var c = await _shared.MedicalCenters.AsNoTracking().SingleAsync(x => x.Id == r.Id);
        c.CenterStatus.ShouldBe(CenterStatus.Active);
        c.Phone.ShouldBe("0591234567");
        c.Address.ShouldBe("Hebron");
        c.RequiresPrepayment.ShouldBeTrue();
        c.PrepaymentAmount.ShouldBe(20m);
        c.RefundPolicyHours.ShouldBe(12);
        c.PartialRefundPercent.ShouldBe(50m);
        c.AdminWhatsappNumber.ShouldBe("0597654321");
        c.AdminEmail.ShouldBe("owner@test.local");
        c.AddedBySuperAdminId.ShouldBe(owner);

        var sub = await _shared.CenterSubscriptions.AsNoTracking().SingleAsync(s => s.CenterId == r.Id);
        sub.Status.ShouldBe(SubscriptionStatus.Active);
        sub.PlanId.ShouldBe(plan);
        sub.AmountPaid.ShouldBe(150m);
        (sub.EndDate.DayNumber - sub.StartDate.DayNumber).ShouldBeInRange(29, 31);

        var links = await _shared.CenterFeatures.AsNoTracking().Where(f => f.CenterId == r.Id).ToListAsync();
        links.Select(l => l.FeatureId).OrderBy(i => i).ShouldBe(features.OrderBy(i => i));
        links.ShouldAllBe(l => l.IsEnabled);

        await using var tenant = fx.Harness.Tenant(r.Id);
        (await tenant.Users.CountAsync()).ShouldBe(0);
        (await tenant.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_plan_without_features_gives_a_center_without_features()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync(features: 0);

        var r = await NewSvc().CreateCenterAsync(Req(plan));

        r.EnabledFeatures.ShouldBeEmpty();
        (await _shared.CenterFeatures.CountAsync(f => f.CenterId == r.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task A_name_that_gives_the_same_address_is_refused_and_nothing_new_is_created()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync();
        await NewSvc().CreateCenterAsync(Req(plan, "Same Name"));

        await ShouldFailWith("Center.UniqueUrlExists", () => NewSvc().CreateCenterAsync(Req(plan, "  SAME NAME ")));

        (await _shared.MedicalCenters.CountAsync()).ShouldBe(1);
        (await _shared.CenterSubscriptions.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_plan_is_refused_and_nothing_is_created()
    {
        await NewSharedAsync();

        await ShouldFailWith("Subscription.PlanNotFound", () => NewSvc().CreateCenterAsync(Req(987654)));

        (await _shared.MedicalCenters.CountAsync()).ShouldBe(0);
    }

    /// <summary>A retired plan is hidden from the plan list, but creating a center with its id still works.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_retired_plan_cannot_be_chosen_for_a_new_center()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync(active: false);

        await ShouldFailWith("Subscription.PlanNotFound", () => NewSvc().CreateCenterAsync(Req(plan)));

        (await _shared.MedicalCenters.CountAsync()).ShouldBe(0);
    }

    /// <summary>Two spaces in the name give two dashes in the address ("al--amal").</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Extra_spaces_in_the_name_give_one_dash_in_the_address()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync();

        var r = await NewSvc().CreateCenterAsync(Req(plan, "Al   Amal"));

        r.UniqueUrl.ShouldBe("al-amal.sehatak.com");
    }

    /// <summary>The center row is saved first (Pending), the database is created afterwards. If that step fails, the Pending row stays,
    /// holds the address forever and the retry is refused with "UniqueUrlExists".</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task When_the_database_cannot_be_created_no_half_made_center_is_left_behind()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync();
        var brokenFactory = new TenantDbContextFactory(new ConfigurationBuilder().Build(), fx.Harness.Push);   // no TenantDbTemplate
        var req = Req(plan, "Half Made");

        await Should.ThrowAsync<Exception>(() => NewSvc(brokenFactory).CreateCenterAsync(req));
        NewRequestContext();

        (await _shared.MedicalCenters.AnyAsync(c => c.Name == "Half Made")).ShouldBeFalse();
        (await _shared.CenterSubscriptions.CountAsync()).ShouldBe(0);
        // and the super admin can simply try again
        (await NewSvc().CreateCenterAsync(req)).Status.ShouldBe("Active");
    }

    [Fact]
    public async Task A_png_logo_is_saved_and_its_url_stored()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync();
        Directory.CreateDirectory(Path.Combine("wwwroot", "uploads", "receipts"));
        var req = Req(plan);
        req.Logo = new FormFile(new MemoryStream(new byte[20]), 0, 20, "Logo", "logo.png");

        var r = await NewSvc().CreateCenterAsync(req);

        var url = (await _shared.MedicalCenters.AsNoTracking().SingleAsync(c => c.Id == r.Id)).LogoUrl;
        try
        {
            url.ShouldNotBeNull();
            url.ShouldEndWith(".png");
            File.Exists(Path.Combine("wwwroot", url.TrimStart('/'))).ShouldBeTrue();
        }
        finally { if (url != null && File.Exists(Path.Combine("wwwroot", url.TrimStart('/')))) File.Delete(Path.Combine("wwwroot", url.TrimStart('/'))); }
    }

    /// <summary>Same hole as in super-admin registration: any file type is saved under wwwroot.</summary>
    [Theory]
    [InlineData("page.html")]
    [InlineData("logo.svg")]
    [InlineData("run.aspx")]
    [Trait("Category", "KnownBug")]
    public async Task A_logo_that_is_not_an_image_is_refused(string fileName)
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync();
        Directory.CreateDirectory(Path.Combine("wwwroot", "uploads", "receipts"));
        var req = Req(plan);
        req.Logo = new FormFile(new MemoryStream(new byte[20]), 0, 20, "Logo", fileName);

        try { await Should.ThrowAsync<Exception>(() => NewSvc().CreateCenterAsync(req)); }
        finally
        {
            foreach (var c in await _shared.MedicalCenters.AsNoTracking().Where(c => c.LogoUrl != null).ToListAsync())
            {
                var p = Path.Combine("wwwroot", c.LogoUrl!.TrimStart('/'));
                if (File.Exists(p)) File.Delete(p);
            }
        }
        (await _shared.MedicalCenters.CountAsync()).ShouldBe(0);
    }

    // ---------- create admin ----------

    private async Task<(int CenterId, string Name)> CenterWithDatabaseAsync(CenterStatus status = CenterStatus.Active)
    {
        var id = await AddCenterRowAsync(status, name: "Admin Test Center");
        (await fx.Harness.TenantFactory.MigrateSingleTenantAsync(id)).Success.ShouldBeTrue();
        return (id, "Admin Test Center");
    }

    private static CreateAdminRequestDto AdminReq(string? email = null, string? phone = null) => new()
    {
        FirstName = "Dina", LastName = "Owner", Email = email ?? $"admin.{Guid.NewGuid():N}@test.local",
        PhoneNumber = phone ?? $"059{Interlocked.Increment(ref _seq):D7}", Address = "Hebron", City = "Hebron"
    };

    [Fact]
    public async Task Creating_the_admin_saves_an_active_admin_and_mails_a_temporary_password_that_matches_the_hash()
    {
        await NewSharedAsync();
        var (center, centerName) = await CenterWithDatabaseAsync();
        string? sentPassword = null;
        _mail.When(m => m.SendTempPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
             .Do(ci => sentPassword = ci.ArgAt<string>(2));
        var req = AdminReq();

        var r = await NewSvc().CreateAdminAsync(center, req);

        r.Email.ShouldBe(req.Email);
        r.FullName.ShouldBe("Dina Owner");
        r.Message.ShouldNotBeNullOrWhiteSpace();
        await using var db = fx.Harness.Tenant(center);
        var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == r.UserId);
        u.role.ShouldBe(userRole.Admin);
        u.isActive.ShouldBeTrue();
        u.email.ShouldBe(req.Email);
        u.phoneNumber.ShouldBe(req.PhoneNumber);
        sentPassword.ShouldNotBeNull();
        sentPassword.Length.ShouldBe(10);
        sentPassword.ShouldAllBe(ch => "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789".Contains(ch));
        BCrypt.Net.BCrypt.Verify(sentPassword, u.passwordHash).ShouldBeTrue();
        u.passwordHash.ShouldNotBe(sentPassword);
        await _mail.Received(1).SendTempPasswordAsync(req.Email, "Dina Owner", sentPassword, centerName);
    }

    [Fact]
    public async Task Two_admins_get_two_different_temporary_passwords()
    {
        await NewSharedAsync();
        var (center, _) = await CenterWithDatabaseAsync();
        var sent = new List<string>();
        _mail.When(m => m.SendTempPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()))
             .Do(ci => sent.Add(ci.ArgAt<string>(2)));

        await NewSvc().CreateAdminAsync(center, AdminReq());
        await NewSvc().CreateAdminAsync(center, AdminReq());

        sent.Count.ShouldBe(2);
        sent[0].ShouldNotBe(sent[1]);
    }

    [Fact]
    public async Task An_email_that_already_exists_in_that_center_is_refused_and_nothing_is_mailed()
    {
        await NewSharedAsync();
        var (center, _) = await CenterWithDatabaseAsync();
        var req = AdminReq();
        await NewSvc().CreateAdminAsync(center, req);
        _mail.ClearReceivedCalls();

        await ShouldFailWith("Auth.EmailExists", () => NewSvc().CreateAdminAsync(center, AdminReq(email: req.Email)));

        await _mail.DidNotReceive().SendTempPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        await using var db = fx.Harness.Tenant(center);
        (await db.Users.CountAsync(u => u.email == req.Email)).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_email_can_be_an_admin_in_two_different_centers()
    {
        await NewSharedAsync();
        var (a, _) = await CenterWithDatabaseAsync();
        var (b, _) = await CenterWithDatabaseAsync();
        var email = $"both.{Guid.NewGuid():N}@test.local";

        await NewSvc().CreateAdminAsync(a, AdminReq(email: email));
        await NewSvc().CreateAdminAsync(b, AdminReq(email: email));

        await using var db = fx.Harness.Tenant(b);
        (await db.Users.CountAsync(u => u.email == email)).ShouldBe(1);
    }

    [Fact]
    public async Task An_unknown_center_is_refused()
    {
        await NewSharedAsync();

        await ShouldFailWith("Center.NotFound", () => NewSvc().CreateAdminAsync(987654, AdminReq()));

        await _mail.DidNotReceive().SendTempPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    /// <summary>users.phoneNumber has a unique index and nothing checks it first: raw DbUpdateException (HTTP 500).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_phone_number_that_is_taken_gives_a_clean_error()
    {
        await NewSharedAsync();
        var (center, _) = await CenterWithDatabaseAsync();
        var first = AdminReq();
        await NewSvc().CreateAdminAsync(center, first);

        await ShouldFailWith("Auth.PhoneExists", () => NewSvc().CreateAdminAsync(center, AdminReq(phone: first.PhoneNumber)));
    }

    /// <summary>The account is saved before the mail is sent. If the mail fails, the admin exists with a password nobody has, and trying again says "email exists".</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task When_the_mail_cannot_be_sent_no_account_is_left_that_nobody_can_open()
    {
        await NewSharedAsync();
        var (center, _) = await CenterWithDatabaseAsync();
        _mail.SendTempPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
             .Returns(Task.FromException(new InvalidOperationException("smtp down")));
        var req = AdminReq();

        await Should.ThrowAsync<Exception>(() => NewSvc().CreateAdminAsync(center, req));

        await using var db = fx.Harness.Tenant(center);
        (await db.Users.AnyAsync(u => u.email == req.Email)).ShouldBeFalse();
    }

    // ---------- view one center ----------

    [Fact]
    public async Task Viewing_a_center_shows_its_details_plan_name_and_features()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync(features: 2);
        var owner = await AddSuperAdminRowAsync();
        var created = await NewSvc().CreateCenterAsync(Req(plan, "View Me", owner));

        var r = await NewSvc().GetSpasificCenterById(created.Id);

        r.Id.ShouldBe(created.Id);
        r.Name.ShouldBe("View Me");
        r.UniqueUrl.ShouldBe("view-me.sehatak.com");
        r.Phone.ShouldBe("0591234567");
        r.Address.ShouldBe("Hebron");
        r.AdminWhatsappNumber.ShouldBe("0597654321");
        r.AddedBySuperAdminId.ShouldBe(owner);
        r.PlanName.ShouldNotBeNullOrWhiteSpace();
        r.FeatureNames.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Viewing_an_unknown_center_is_refused()
    {
        await NewSharedAsync();

        await ShouldFailWith("Center.NotFound", () => NewSvc().GetSpasificCenterById(987654));
    }

    /// <summary>A suspended center has no Active subscription, so the super admin gets "SubscriptionExpired" instead of the details they need to decide whether to re-activate it.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_suspended_center_can_still_be_viewed()
    {
        await NewSharedAsync();
        var (plan, _) = await AddPlanAsync();
        var id = await AddCenterRowAsync(CenterStatus.Suspended, name: "Suspended One");
        await AddSubscriptionRowAsync(id, plan, SubscriptionStatus.Cancelled);

        var r = await NewSvc().GetSpasificCenterById(id);

        r.Name.ShouldBe("Suspended One");
    }

    /// <summary>The features shown are the PLAN's features. A feature the super admin switched off for this center is still listed, and one added only to this center is missing.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_features_shown_are_the_ones_enabled_for_that_center()
    {
        await NewSharedAsync();
        var (plan, planFeatures) = await AddPlanAsync(features: 2);
        var created = await NewSvc().CreateCenterAsync(Req(plan));
        var extra = new PlatformFeature { NameOfFeature = $"Extra-{Guid.NewGuid():N}" };
        _shared.PlatformFeatures.Add(extra);
        await _shared.SaveChangesAsync();
        _shared.CenterFeatures.Add(new CenterFeature { CenterId = created.Id, FeatureId = extra.Id, IsEnabled = true });
        (await _shared.CenterFeatures.SingleAsync(f => f.CenterId == created.Id && f.FeatureId == planFeatures[0])).IsEnabled = false;
        await _shared.SaveChangesAsync();
        var offName = (await _shared.PlatformFeatures.SingleAsync(f => f.Id == planFeatures[0])).NameOfFeature;
        var onName = (await _shared.PlatformFeatures.SingleAsync(f => f.Id == planFeatures[1])).NameOfFeature;

        var r = await NewSvc().GetSpasificCenterById(created.Id);

        r.FeatureNames.OrderBy(x => x).ShouldBe(new[] { onName, extra.NameOfFeature }.OrderBy(x => x));
        r.FeatureNames.ShouldNotContain(offName);
    }

    // ---------- list ----------

    [Fact]
    public async Task The_list_is_newest_first_and_includes_centers_of_every_status()
    {
        await NewSharedAsync();
        var now = DateTime.UtcNow;
        var oldest = await AddCenterRowAsync(CenterStatus.Active, now.AddDays(-3));
        var middle = await AddCenterRowAsync(CenterStatus.Suspended, now.AddDays(-2));
        var newest = await AddCenterRowAsync(CenterStatus.Pending, now.AddDays(-1));

        var r = await NewSvc().GetListOfCenters(new PagedRequest { PageNumber = 1, PageSize = 10 });

        r.TotalCount.ShouldBe(3);
        r.Items.Select(i => i.Id).ShouldBe(new[] { newest, middle, oldest });
    }

    [Fact]
    public async Task The_list_is_paged_without_repeating_or_skipping_a_center()
    {
        await NewSharedAsync();
        var now = DateTime.UtcNow;
        var ids = new List<int>();
        for (var i = 0; i < 5; i++) ids.Add(await AddCenterRowAsync(createdAt: now.AddMinutes(-i)));

        var p1 = await NewSvc().GetListOfCenters(new PagedRequest { PageNumber = 1, PageSize = 2 });
        var p2 = await NewSvc().GetListOfCenters(new PagedRequest { PageNumber = 2, PageSize = 2 });
        var p3 = await NewSvc().GetListOfCenters(new PagedRequest { PageNumber = 3, PageSize = 2 });

        p1.TotalCount.ShouldBe(5);
        p1.Items.Concat(p2.Items).Concat(p3.Items).Select(i => i.Id).ShouldBe(ids);
        p3.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task With_no_centers_the_list_is_empty()
    {
        await NewSharedAsync();

        var r = await NewSvc().GetListOfCenters(new PagedRequest());

        r.TotalCount.ShouldBe(0);
        r.Items.ShouldBeEmpty();
    }

    /// <summary>The list rows have no status field, so a super admin cannot tell active, pending and suspended centers apart.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task The_list_tells_the_status_of_each_center()
    {
        await NewSharedAsync();
        await AddCenterRowAsync(CenterStatus.Suspended);

        var r = await NewSvc().GetListOfCenters(new PagedRequest());

        r.Items.Single().GetType().GetProperty("Status").ShouldNotBeNull();
    }
}
