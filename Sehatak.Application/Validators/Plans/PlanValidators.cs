using FluentValidation;
using Sehatak.Application.DTOs.Plans;
using Sehatak.Application.DTOs.PlansDto;
using Sehatak.Application.DTOs.RenewSubscription;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Plans
{
    public class SubscriptionPlanRequestDtoValidator : AbstractValidator<SubscriptionPlanRequestDto>
    {
        public SubscriptionPlanRequestDtoValidator()
        {
            RuleFor(x => x.Name).RequiredText(100);
            RuleFor(x => x.Price).Money();
            // [Required] on a value type is a no-op: 0 days would create a plan
            // that expires the moment it starts.
            RuleFor(x => x.DurationDays)
                .InclusiveBetween(1, 3650).WithMessage(ValidationKeys.InvalidValue);
        }
    }

    public class EditPalnRequestDtoValidator : AbstractValidator<EditPalnRequestDto>
    {
        public EditPalnRequestDtoValidator()
        {
            RuleFor(x => x.name)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(100).WithMessage(ValidationKeys.TooLong)
                .When(x => x.name is not null);

            RuleFor(x => x.price).OptionalMoney();

            RuleFor(x => x.DurationDays)
                .InclusiveBetween(1, 3650).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x)
                .Must(x => x.name is not null || x.price is not null || x.DurationDays is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }

    public class RenewSubscriptionRequestValidator : AbstractValidator<RenewSubscriptionRequest>
    {
        public RenewSubscriptionRequestValidator()
        {
            RuleFor(x => x.newPlanId).ValidId();
        }
    }

    public class CancleSubcsriptionRequestValidator : AbstractValidator<CancleSubcsriptionRequest>
    {
        public CancleSubcsriptionRequestValidator()
        {
            RuleFor(x => x.subscriptionId).ValidId();
            RuleFor(x => x.userId).ValidId();
        }
    }
}
