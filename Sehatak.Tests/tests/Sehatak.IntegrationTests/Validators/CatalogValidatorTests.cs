using Sehatak.Application.DTOs.AssignFeaturesWithPlan;
using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.DTOs.FeatureDto;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Application.DTOs.RenewSubscription;
using Sehatak.Application.DTOs.ServicePriceDto;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.Feature;
using Sehatak.Application.Validators.Plans;
using Sehatak.Application.Validators.ServicePrice;
using Sehatak.Domain.Enums;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class PlanValidatorTests
{
    private readonly SubscriptionPlanRequestDtoValidator _create = new();
    private static SubscriptionPlanRequestDto Plan() => new() { Name = "Gold", Price = 100m, DurationDays = 30 };

    [Fact] public void A_normal_plan_is_valid() => _create.ShouldBeValid(Plan());
    [Fact] public void A_free_plan_is_valid() => _create.ShouldBeValid(Plan().With("Price", 0m));

    [Fact]
    public void The_name_is_required_and_limited_to_100()
    {
        _create.ShouldFail(Plan().With("Name", ""), "Name", ValidationKeys.Required);
        _create.ShouldFail(Plan().With("Name", Long(101)), "Name", ValidationKeys.TooLong);
        _create.ShouldBeValid(Plan().With("Name", Long(100)));
    }

    [Fact]
    public void The_price_is_a_valid_amount()
    {
        _create.ShouldFail(Plan().With("Price", -1m), "Price", ValidationKeys.NegativeAmount);
        _create.ShouldFail(Plan().With("Price", 10.999m), "Price", ValidationKeys.InvalidValue);
        _create.ShouldFail(Plan().With("Price", 100_000_000m), "Price", ValidationKeys.AmountTooLarge);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3650, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(3651, false)]
    public void The_duration_is_1_to_3650_days(int days, bool ok)
    {
        if (ok) _create.ShouldBeValid(Plan().With("DurationDays", days));
        else _create.ShouldFail(Plan().With("DurationDays", days), "DurationDays", ValidationKeys.InvalidValue);
    }

    // ---------- edit ----------

    private readonly EditPalnRequestDtoValidator _edit = new();

    [Fact]
    public void An_edit_of_one_field_is_valid()
    {
        _edit.ShouldBeValid(new EditPalnRequestDto { name = "New" });
        _edit.ShouldBeValid(new EditPalnRequestDto { price = 0m });
        _edit.ShouldBeValid(new EditPalnRequestDto { DurationDays = 10 });
    }

    [Fact]
    public void An_empty_edit_is_refused() =>
        _edit.ShouldFail(new EditPalnRequestDto(), "request", ValidationKeys.AtLeastOneField);

    [Fact]
    public void A_sent_name_cannot_be_blank_or_too_long()
    {
        _edit.ShouldFail(new EditPalnRequestDto { name = "  " }, "name", ValidationKeys.Required);
        _edit.ShouldFail(new EditPalnRequestDto { name = Long(101) }, "name", ValidationKeys.TooLong);
    }

    [Fact]
    public void A_sent_price_and_duration_are_checked()
    {
        _edit.ShouldFail(new EditPalnRequestDto { price = -1m }, "price", ValidationKeys.NegativeAmount);
        _edit.ShouldFail(new EditPalnRequestDto { price = 1.234m }, "price", ValidationKeys.InvalidValue);
        _edit.ShouldFail(new EditPalnRequestDto { DurationDays = 0 }, "DurationDays", ValidationKeys.InvalidValue);
        _edit.ShouldFail(new EditPalnRequestDto { DurationDays = 3651 }, "DurationDays", ValidationKeys.InvalidValue);
    }

    // ---------- renew / cancel ----------

    [Fact]
    public void Renew_and_cancel_need_positive_ids()
    {
        new RenewSubscriptionRequestValidator().ShouldBeValid(new RenewSubscriptionRequest { newPlanId = 1 });
        new RenewSubscriptionRequestValidator().ShouldFail(new RenewSubscriptionRequest { newPlanId = 0 }, "newPlanId", ValidationKeys.InvalidId);

        var c = new CancleSubcsriptionRequestValidator();
        c.ShouldBeValid(new CancleSubcsriptionRequest { subscriptionId = 1, userId = 1 });
        c.ShouldFail(new CancleSubcsriptionRequest { subscriptionId = 0, userId = 1 }, "subscriptionId", ValidationKeys.InvalidId);
        c.ShouldFail(new CancleSubcsriptionRequest { subscriptionId = 1, userId = -1 }, "userId", ValidationKeys.InvalidId);
    }
}

public class FeatureValidatorTests
{
    [Fact]
    public void A_feature_has_a_required_name_and_an_optional_limited_description()
    {
        var v = new CreateFeatureRequestDtoValidator();

        v.ShouldBeValid(new CreateFeatureRequestDto { Name = "Lab" });
        v.ShouldFail(new CreateFeatureRequestDto { Name = "" }, "Name", ValidationKeys.Required);
        v.ShouldFail(new CreateFeatureRequestDto { Name = Long(101) }, "Name", ValidationKeys.TooLong);
        v.ShouldBeValid(new CreateFeatureRequestDto { Name = "Lab", Description = Long(300) });
        v.ShouldFail(new CreateFeatureRequestDto { Name = "Lab", Description = Long(301) }, "Description", ValidationKeys.TooLong);
    }

    [Fact]
    public void Feature_requests_need_positive_ids()
    {
        new AssignFeatureToPlanRequestDtoValidator().ShouldFail(new AssignFeatureToPlanRequestDto { featureId = 0 }, "featureId", ValidationKeys.InvalidId);
        new ActiveFetureRequestValidator().ShouldFail(new ActiveFetureRequest { FetureId = 0 }, "FetureId", ValidationKeys.InvalidId);
        new RemoveFeatureFromCenterRequestValidator().ShouldFail(new RemoveFeatureFromCenterRequest { featureId = -1 }, "featureId", ValidationKeys.InvalidId);

        new AssignFeatureToPlanRequestDtoValidator().ShouldBeValid(new AssignFeatureToPlanRequestDto { featureId = 1 });
        new ActiveFetureRequestValidator().ShouldBeValid(new ActiveFetureRequest { FetureId = 1 });
        new RemoveFeatureFromCenterRequestValidator().ShouldBeValid(new RemoveFeatureFromCenterRequest { featureId = 1 });
    }

    [Fact]
    public void Adding_a_feature_to_a_center_needs_an_id_and_a_name()
    {
        var v = new AddFeatureToCenterRequestValidator();

        v.ShouldBeValid(new AddFeatureToCenterRequest { featureId = 1, featureName = "Lab" });
        v.ShouldFail(new AddFeatureToCenterRequest { featureId = 0, featureName = "Lab" }, "featureId", ValidationKeys.InvalidId);
        v.ShouldFail(new AddFeatureToCenterRequest { featureId = 1, featureName = "" }, "featureName", ValidationKeys.Required);
        v.ShouldFail(new AddFeatureToCenterRequest { featureId = 1, featureName = Long(101) }, "featureName", ValidationKeys.TooLong);
    }
}

public class ServicePriceValidatorTests
{
    private static ServicePriceItemDto Item(string name = "X-ray", decimal price = 50m) => new() { ServiceName = name, Price = price };
    private static ServicePriceRequest Req(params ServicePriceItemDto[] items) => new() { Type = ServiceType.LabTest, Items = items.ToList() };

    private readonly ServicePriceRequestValidator _create = new();

    [Fact] public void A_list_of_services_is_valid() => _create.ShouldBeValid(Req(Item("A"), Item("B", 0m)));

    [Fact]
    public void The_type_must_be_a_real_service_type()
    {
        _create.ShouldFail(Req(Item()).With("Type", (ServiceType)0), "Type", ValidationKeys.InvalidEnum);
        _create.ShouldFail(Req(Item()).With("Type", (ServiceType)99), "Type", ValidationKeys.InvalidEnum);
    }

    [Fact]
    public void The_list_cannot_be_empty_or_over_200()
    {
        _create.ShouldFail(Req(), "Items", ValidationKeys.ListEmpty);
        _create.ShouldFail(Req().With("Items", null), "Items", ValidationKeys.ListEmpty);
        _create.ShouldBeValid(Req(Enumerable.Range(1, 200).Select(i => Item("S" + i)).ToArray()));
        _create.ShouldFail(Req(Enumerable.Range(1, 201).Select(i => Item("S" + i)).ToArray()), "Items", ValidationKeys.ListTooLarge);
    }

    [Theory]
    [InlineData("X-ray", "x-ray")]
    [InlineData("X-ray", "  X-RAY  ")]
    public void The_same_name_twice_is_refused_ignoring_case_and_spaces(string a, string b) =>
        _create.ShouldFail(Req(Item(a), Item(b)), "Items", ValidationKeys.DuplicateItems);

    [Fact]
    public void Each_item_is_checked_with_its_position_in_the_error()
    {
        _create.ShouldFail(Req(Item("A"), Item("")), "Items[1].ServiceName", ValidationKeys.Required);
        _create.ShouldFail(Req(Item(Long(257))), "Items[0].ServiceName", ValidationKeys.TooLong);
        _create.ShouldBeValid(Req(Item(Long(256))));
        _create.ShouldFail(Req(Item("A", -1m)), "Items[0].Price", ValidationKeys.NegativeAmount);
        _create.ShouldFail(Req(Item("A", 1.005m)), "Items[0].Price", ValidationKeys.InvalidValue);
    }

    // ---------- update ----------

    private readonly UpdateServicePriceValidator _update = new();

    [Fact]
    public void An_update_with_a_name_or_a_price_is_valid()
    {
        _update.ShouldBeValid(new UpdateServicePrice { ServicePriceId = 1, ServiceName = "New", Price = null });
        _update.ShouldBeValid(new UpdateServicePrice { ServicePriceId = 1, ServiceName = null, Price = 0m });
        _update.ShouldBeValid(new UpdateServicePrice { ServicePriceId = 1, ServiceName = "", Price = 5m });
    }

    [Fact]
    public void An_update_with_neither_is_refused()
    {
        _update.ShouldFail(new UpdateServicePrice { ServicePriceId = 1, ServiceName = "", Price = null }, "request", ValidationKeys.AtLeastOneField);
        _update.ShouldFail(new UpdateServicePrice { ServicePriceId = 1, ServiceName = null, Price = null }, "request", ValidationKeys.AtLeastOneField);
    }

    [Fact]
    public void An_update_checks_id_name_and_price()
    {
        _update.ShouldFail(new UpdateServicePrice { ServicePriceId = 0, Price = 1m }, "ServicePriceId", ValidationKeys.InvalidId);
        _update.ShouldFail(new UpdateServicePrice { ServicePriceId = 1, ServiceName = "   " }, "ServiceName", ValidationKeys.Required);
        _update.ShouldFail(new UpdateServicePrice { ServicePriceId = 1, ServiceName = Long(257) }, "ServiceName", ValidationKeys.TooLong);
        _update.ShouldFail(new UpdateServicePrice { ServicePriceId = 1, Price = -1m }, "Price", ValidationKeys.NegativeAmount);
    }
}
