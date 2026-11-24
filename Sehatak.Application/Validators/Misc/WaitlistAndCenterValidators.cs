using FluentValidation;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.DTOs.CentersDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Misc
{
    public class ReceptionistEnterFromWaitlistRequestValidator : AbstractValidator<ReceptionistEnterFromWaitlistRequest>
    {
        public ReceptionistEnterFromWaitlistRequestValidator()
        {
            RuleFor(x => x.WaitListId).ValidId();
            RuleFor(x => x.TimeSlot)
                .Must(t => t is null || t.Value != default)
                .WithMessage(ValidationKeys.Required);
        }
    }

    public class SearchCentersRequestValidator : AbstractValidator<SearchCentersRequest>
    {
        public SearchCentersRequestValidator()
        {
            // PagedRequestValidator cannot be Include()d (see SearchDoctorRequestValidator).
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage(ValidationKeys.InvalidValue)
                .LessThanOrEqualTo(10_000).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 50).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.Search).OptionalText(100);
        }
    }
}
