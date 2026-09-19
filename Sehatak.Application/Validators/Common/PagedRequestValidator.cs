using FluentValidation;
using Sehatak.Application.Common;

namespace Sehatak.Application.Validators.Common
{
    public class PagedRequestValidator : AbstractValidator<PagedRequest>
    {
        public PagedRequestValidator()
        {
            // PageSize clamps itself in the setter; PageNumber does not.
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage(ValidationKeys.InvalidValue)
                .LessThanOrEqualTo(10_000).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 50).WithMessage(ValidationKeys.InvalidValue);
        }
    }
}
