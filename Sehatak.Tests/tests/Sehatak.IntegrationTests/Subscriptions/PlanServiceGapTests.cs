using Microsoft.EntityFrameworkCore;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Domain.Enums.SharedEnums;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Subscriptions;

/// <summary>ListOfPlanService cases PlanAndCenterStatusTests does not cover.</summary>
[Collection(SqlServerCollection.Name)]
public class PlanServiceGapTests(SqlServerFixture fx) : SubscriptionTestBase(fx)
{
    [Fact]
    public async Task Editing_an_unknown_plan_is_rejected()
    {
        await ShouldFailWith("Subscription.PlanNotFound", () => NewPlans().EditPlanAsync(987654, new EditPalnRequestDto { price = 1m }));
    }

    [Fact]
    public async Task Editing_with_nothing_to_change_keeps_every_field()
    {
        var plan = await AddPlanAsync(120m, 90);

        var r = await NewPlans().EditPlanAsync(plan, new EditPalnRequestDto());

        r.Id.ShouldBe(plan);
        r.price.ShouldBe(120m);
        r.DurationDays.ShouldBe(90);
    }

    [Fact]
    public async Task Changing_a_plans_price_does_not_rewrite_what_a_center_already_paid()
    {
        var plan = await AddPlanAsync(100m, 30);
        var sub = await AddSubscriptionAsync(plan, SubscriptionStatus.Active, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), amount: 100m);

        await NewPlans().EditPlanAsync(plan, new EditPalnRequestDto { price = 250m, DurationDays = 365 });

        await using var shared = Fx.Harness.NewSharedDb();
        var row = await shared.CenterSubscriptions.AsNoTracking().SingleAsync(s => s.Id == sub);
        row.AmountPaid.ShouldBe(100m);
        row.EndDate.ShouldBe(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)));
    }

    [Fact]
    public async Task A_plans_features_are_paged_and_never_include_another_plans_features()
    {
        var features = new List<int>();
        for (var i = 0; i < 5; i++) features.Add(await AddFeatureAsync($"Feat-{i}-{Guid.NewGuid():N}"));
        var plan = await AddPlanAsync(10m, 30, true, features.ToArray());
        var other = await AddPlanAsync(10m, 30, true, await AddFeatureAsync());

        var p1 = await NewPlans().GetPlanFeaturesAsync(plan, new PagedRequest { PageNumber = 1, PageSize = 2 });
        var p2 = await NewPlans().GetPlanFeaturesAsync(plan, new PagedRequest { PageNumber = 2, PageSize = 2 });
        var p3 = await NewPlans().GetPlanFeaturesAsync(plan, new PagedRequest { PageNumber = 3, PageSize = 2 });

        p1.TotalCount.ShouldBe(5);
        p1.Items.Count.ShouldBe(2);
        p2.Items.Count.ShouldBe(2);
        p3.Items.Count.ShouldBe(1);
        var all = p1.Items.Concat(p2.Items).Concat(p3.Items).ToList();
        all.Select(i => i.featureId).OrderBy(i => i).ShouldBe(features.OrderBy(i => i));
        all.ShouldAllBe(i => i.planId == plan && i.planId != other);
    }

    [Fact]
    public async Task A_plan_without_features_gives_an_empty_page()
    {
        var plan = await AddPlanAsync();

        var r = await NewPlans().GetPlanFeaturesAsync(plan, new PagedRequest());

        r.TotalCount.ShouldBe(0);
        r.Items.ShouldBeEmpty();
    }
}
