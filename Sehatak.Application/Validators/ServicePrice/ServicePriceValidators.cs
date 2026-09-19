using FluentValidation;
using Sehatak.Application.DTOs.ServicePriceDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.ServicePrice
{
    public class ServicePriceItemDtoValidator : AbstractValidator<ServicePriceItemDto>
    {
        public ServicePriceItemDtoValidator()
        {
            // service_prices.ServiceName is NOT NULL, max 256; Price is decimal(10,2).
            RuleFor(x => x.ServiceName).RequiredText(256);
            RuleFor(x => x.Price).Money();
        }
    }

    public class ServicePriceRequestValidator : AbstractValidator<ServicePriceRequest>
    {
        public ServicePriceRequestValidator()
        {
            RuleFor(x => x.Type).ValidEnum();

            RuleFor(x => x.Items)
                .NotNull().WithMessage(ValidationKeys.ListEmpty)
                .Must(i => i is not null && i.Count > 0).WithMessage(ValidationKeys.ListEmpty)
                .Must(i => i is null || i.Count <= 200).WithMessage(ValidationKeys.ListTooLarge)
                .Must(NoDuplicateNames).WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.Items).SetValidator(new ServicePriceItemDtoValidator());
        }

        private static bool NoDuplicateNames(List<ServicePriceItemDto>? items) =>
            items is null ||
            items.Select(i => i.ServiceName?.Trim().ToLowerInvariant())
                 .Distinct()
                 .Count() == items.Count;
    }

    public class UpdateServicePriceValidator : AbstractValidator<UpdateServicePrice>
    {
        public UpdateServicePriceValidator()
        {
            RuleFor(x => x.ServicePriceId).ValidId();

            // Default is string.Empty, so "" means "not supplied" here rather than
            // "blank the name" — treat both the same and reject a whitespace name.
            RuleFor(x => x.ServiceName)
                .MaximumLength(256).WithMessage(ValidationKeys.TooLong)
                .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage(ValidationKeys.Required)
                .When(x => x.ServiceName is not null && x.ServiceName.Length > 0);

            RuleFor(x => x.Price).OptionalMoney();

            RuleFor(x => x)
                .Must(x => !string.IsNullOrWhiteSpace(x.ServiceName) || x.Price is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }
}
