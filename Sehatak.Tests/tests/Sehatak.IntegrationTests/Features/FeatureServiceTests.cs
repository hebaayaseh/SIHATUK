using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Exceptions;
using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.DTOs.FeatureDto;
using Sehatak.Domain.Entities.SharedEntities;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.Infrastructure.Data;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;
using FeatureSvc = Sehatak.Infrastructure.Services.SuperAdminService.Features.FeatureService.featureService;

namespace Sehatak.IntegrationTests.Features;

/// <summary>
/// featureService (super admin): the platform feature catalog and switching a feature on/off for one center.
/// Everything lives in the shared DB, so tests only look at rows they created themselves.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class FeatureServiceTests(SqlServerFixture fx)
{
    private static int _seq;

    private SharedDbContext Shared() => fx.Harness.NewSharedDb();
    private FeatureSvc NewSvc() => new(Shared());

    private static async Task ShouldFailWith(string code, Func<Task> act)
    {
        var ex = await Should.ThrowAsync<BusinessException>(act);
        ex.Message.ShouldBe(code);
    }

    private async Task<int> NewCenterAsync(CenterStatus status = CenterStatus.Active)
    {
        await using var db = Shared();
        var c = new MedicalCenter
        {
            Name = "Feature Test Center", UniqueUrl = Guid.NewGuid().ToString("N"),
            Phone = "0590000000", Address = "Hebron", CenterStatus = status
        };
        db.MedicalCenters.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    private async Task<int> NewFeatureAsync(string? name = null)
    {
        await using var db = Shared();
        var f = new PlatformFeature { NameOfFeature = name ?? $"Feature-{Interlocked.Increment(ref _seq)}-{Guid.NewGuid():N}"[..40] };
        db.PlatformFeatures.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private async Task LinkAsync(int centerId, int featureId, bool enabled)
    {
        await using var db = Shared();
        db.CenterFeatures.Add(new CenterFeature { CenterId = centerId, FeatureId = featureId, IsEnabled = enabled });
        await db.SaveChangesAsync();
    }

    private async Task<CenterFeature?> LinkOfAsync(int centerId, int featureId)
    {
        await using var db = Shared();
        return await db.CenterFeatures.AsNoTracking().SingleOrDefaultAsync(x => x.CenterId == centerId && x.FeatureId == featureId);
    }

    private static AddFeatureToCenterRequest AddReq(int featureId) => new() { featureId = featureId, featureName = "x" };

    // ---------- catalog: add ----------

    [Fact]
    public async Task Adding_a_feature_saves_it_and_returns_it()
    {
        var name = $"LabAI-{Guid.NewGuid():N}";

        var r = await NewSvc().AddFeatureAsync(new CreateFeatureRequestDto { Name = name, Description = "AI for lab results" });

        r.Id.ShouldBeGreaterThan(0);
        r.Name.ShouldBe(name);
        r.Description.ShouldBe("AI for lab results");
        await using var db = Shared();
        var row = await db.PlatformFeatures.AsNoTracking().SingleAsync(f => f.Id == r.Id);
        row.NameOfFeature.ShouldBe(name);
        row.Description.ShouldBe("AI for lab results");
    }

    [Fact]
    public async Task A_feature_may_have_no_description()
    {
        var r = await NewSvc().AddFeatureAsync(new CreateFeatureRequestDto { Name = $"NoDesc-{Guid.NewGuid():N}", Description = null });

        r.Description.ShouldBeNull();
    }

    /// <summary>The DTO allows 500 characters, the column holds 300: a 301-500 character description passes validation and then fails in SQL (HTTP 500).</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_description_the_dto_accepts_is_saved()
    {
        var text = new string('d', 300);

        var r = await NewSvc().AddFeatureAsync(new CreateFeatureRequestDto { Name = $"LongDesc-{Guid.NewGuid():N}", Description = text });

        await using var db = Shared();
        (await db.PlatformFeatures.AsNoTracking().SingleAsync(f => f.Id == r.Id)).Description.ShouldBe(text);
    }

    // ---------- catalog: list ----------

    [Fact]
    public async Task The_list_contains_the_new_features_and_pages_never_repeat_an_item()
    {
        var ids = new List<int>();
        for (var i = 0; i < 3; i++) ids.Add(await NewFeatureAsync());

        var all = new List<int>();
        var first = await NewSvc().GetAllFeatureAsync(new PagedRequest { PageNumber = 1, PageSize = 2 });
        first.Items.Count.ShouldBeLessThanOrEqualTo(2);
        var pages = (int)Math.Ceiling(first.TotalCount / 2.0);
        all.AddRange(first.Items.Select(i => i.Id));
        for (var p = 2; p <= pages; p++)
            all.AddRange((await NewSvc().GetAllFeatureAsync(new PagedRequest { PageNumber = p, PageSize = 2 })).Items.Select(i => i.Id));

        all.Count.ShouldBe(first.TotalCount);
        all.Distinct().Count().ShouldBe(all.Count);
        ids.ShouldAllBe(id => all.Contains(id));
    }

    // ---------- center: add ----------

    [Fact]
    public async Task Giving_a_center_a_feature_creates_an_enabled_link()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();

        (await NewSvc().AddFeatureToCenterAsync(center, AddReq(feature))).ShouldBeTrue();

        (await LinkOfAsync(center, feature))!.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Adding_a_feature_to_one_center_does_not_touch_another_center()
    {
        var a = await NewCenterAsync();
        var b = await NewCenterAsync();
        var feature = await NewFeatureAsync();

        await NewSvc().AddFeatureToCenterAsync(a, AddReq(feature));

        (await LinkOfAsync(b, feature)).ShouldBeNull();
    }

    [Fact]
    public async Task Adding_needs_an_existing_center_and_an_existing_feature()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();

        await ShouldFailWith("Center.NotFound", () => NewSvc().AddFeatureToCenterAsync(987654, AddReq(feature)));
        await ShouldFailWith("Feature.NotFound", () => NewSvc().AddFeatureToCenterAsync(center, AddReq(987654)));
        (await LinkOfAsync(center, feature)).ShouldBeNull();
    }

    /// <summary>Today the same code "General.NotFound" is used for a feature that does not exist and for one that is already added, so the admin sees "not found" for something that exists.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Adding_a_feature_the_center_already_has_says_so()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        await NewSvc().AddFeatureToCenterAsync(center, AddReq(feature));

        await ShouldFailWith("Feature.AlreadyAdded", () => NewSvc().AddFeatureToCenterAsync(center, AddReq(feature)));
    }

    /// <summary>After "remove" the link stays with IsEnabled = false, so adding the feature again is refused as if it were still there.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task A_feature_that_was_switched_off_can_be_added_again()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        await LinkAsync(center, feature, enabled: false);

        await NewSvc().AddFeatureToCenterAsync(center, AddReq(feature));

        (await LinkOfAsync(center, feature))!.IsEnabled.ShouldBeTrue();
    }

    /// <summary>Double click: both calls pass the "already added" check, the second insert hits the (CenterId, FeatureId) primary key and the admin gets a raw DbUpdateException.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public async Task Adding_the_same_feature_twice_at_the_same_moment_never_gives_a_raw_database_error()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();

        var errors = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await NewSvc().AddFeatureToCenterAsync(center, AddReq(feature)); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));

        errors.Where(e => e != null).ShouldAllBe(e => e is BusinessException);
        await using var db = Shared();
        (await db.CenterFeatures.CountAsync(x => x.CenterId == center && x.FeatureId == feature)).ShouldBe(1);
    }

    // ---------- center: switch on ----------

    [Fact]
    public async Task Switching_on_a_disabled_feature_enables_it()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        await LinkAsync(center, feature, enabled: false);

        (await NewSvc().ActiveFeaturAsync(center, new ActiveFetureRequest { FetureId = feature })).ShouldBeTrue();

        (await LinkOfAsync(center, feature))!.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Switching_on_needs_a_center_a_feature_and_a_disabled_link()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        var enabled = await NewFeatureAsync();
        await LinkAsync(center, enabled, enabled: true);

        await ShouldFailWith("Center.NotFound", () => NewSvc().ActiveFeaturAsync(987654, new ActiveFetureRequest { FetureId = feature }));
        await ShouldFailWith("Feature.NotFound", () => NewSvc().ActiveFeaturAsync(center, new ActiveFetureRequest { FetureId = 987654 }));
        await ShouldFailWith("Feature.NotFound", () => NewSvc().ActiveFeaturAsync(center, new ActiveFetureRequest { FetureId = feature }));
        await ShouldFailWith("Feature.NotFound", () => NewSvc().ActiveFeaturAsync(center, new ActiveFetureRequest { FetureId = enabled }));
    }

    [Fact]
    public async Task Switching_on_one_center_does_not_switch_on_the_same_feature_of_another()
    {
        var a = await NewCenterAsync();
        var b = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        await LinkAsync(a, feature, enabled: false);
        await LinkAsync(b, feature, enabled: false);

        await NewSvc().ActiveFeaturAsync(a, new ActiveFetureRequest { FetureId = feature });

        (await LinkOfAsync(a, feature))!.IsEnabled.ShouldBeTrue();
        (await LinkOfAsync(b, feature))!.IsEnabled.ShouldBeFalse();
    }

    // ---------- center: remove ----------

    [Fact]
    public async Task Removing_switches_the_feature_off_but_keeps_the_link()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        await LinkAsync(center, feature, enabled: true);

        (await NewSvc().RemoveFeatureFromCenterAsync(center, new RemoveFeatureFromCenterRequest { featureId = feature })).ShouldBeTrue();

        var link = await LinkOfAsync(center, feature);
        link.ShouldNotBeNull();
        link.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Removing_twice_is_harmless_and_the_feature_can_be_switched_on_again()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        await LinkAsync(center, feature, enabled: true);

        await NewSvc().RemoveFeatureFromCenterAsync(center, new RemoveFeatureFromCenterRequest { featureId = feature });
        await NewSvc().RemoveFeatureFromCenterAsync(center, new RemoveFeatureFromCenterRequest { featureId = feature });
        (await LinkOfAsync(center, feature))!.IsEnabled.ShouldBeFalse();

        await NewSvc().ActiveFeaturAsync(center, new ActiveFetureRequest { FetureId = feature });
        (await LinkOfAsync(center, feature))!.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Removing_needs_a_center_a_feature_and_a_link()
    {
        var center = await NewCenterAsync();
        var feature = await NewFeatureAsync();

        await ShouldFailWith("Center.NotFound", () => NewSvc().RemoveFeatureFromCenterAsync(987654, new RemoveFeatureFromCenterRequest { featureId = feature }));
        await ShouldFailWith("Feature.NotFound", () => NewSvc().RemoveFeatureFromCenterAsync(center, new RemoveFeatureFromCenterRequest { featureId = 987654 }));
        await ShouldFailWith("Feature.NotFound", () => NewSvc().RemoveFeatureFromCenterAsync(center, new RemoveFeatureFromCenterRequest { featureId = feature }));
    }

    [Fact]
    public async Task Removing_from_one_center_leaves_the_other_centers_feature_on()
    {
        var a = await NewCenterAsync();
        var b = await NewCenterAsync();
        var feature = await NewFeatureAsync();
        await LinkAsync(a, feature, enabled: true);
        await LinkAsync(b, feature, enabled: true);

        await NewSvc().RemoveFeatureFromCenterAsync(a, new RemoveFeatureFromCenterRequest { featureId = feature });

        (await LinkOfAsync(b, feature))!.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_suspended_center_can_still_be_managed_by_the_super_admin()
    {
        var center = await NewCenterAsync(CenterStatus.Suspended);
        var feature = await NewFeatureAsync();

        await NewSvc().AddFeatureToCenterAsync(center, AddReq(feature));
        await NewSvc().RemoveFeatureFromCenterAsync(center, new RemoveFeatureFromCenterRequest { featureId = feature });

        (await LinkOfAsync(center, feature))!.IsEnabled.ShouldBeFalse();
    }
}
