using FluentValidation;
using Sehatak.Application.DTOs.FollowUpDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.FollowUp
{
    public class DoctorAddFollowUpRequestDtoValidator : AbstractValidator<DoctorAddFollowUpRequestDto>
    {
        public DoctorAddFollowUpRequestDtoValidator()
        {
            RuleFor(x => x.OriginalAppointmentId).ValidId();
            RuleFor(x => x.PatientId).ValidId();

            // BookAppointmentAsync compares dateOnly > AllowFollowUpDate. When the
            // column is null that comparison is always false, so the window is never
            // enforced. Requiring a date here closes that hole.
            RuleFor(x => x.AllowFollowUpDate)
                .NotNull().WithMessage(ValidationKeys.Required)
                .OptionalNotInThePast()
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(12))
                .WithMessage(ValidationKeys.DateTooFar);
        }
    }

    public class ReceptionistAddFollowUpRequestDtoValidator : AbstractValidator<ReceptionistAddFollowUpRequestDto>
    {
        public ReceptionistAddFollowUpRequestDtoValidator()
        {
            RuleFor(x => x.OriginalAppointmentId).ValidId();
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.DoctorId).ValidId();

            RuleFor(x => x.AllowFollowUpDate)
                .NotNull().WithMessage(ValidationKeys.Required)
                .OptionalNotInThePast()
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(12))
                .WithMessage(ValidationKeys.DateTooFar);
        }
    }

    public class UpdateFollowUpRequestDtoValidator : AbstractValidator<UpdateFollowUpRequestDto>
    {
        public UpdateFollowUpRequestDtoValidator()
        {
            RuleFor(x => x.FollowUpId).ValidId();
            RuleFor(x => x.PatientId).ValidId();

            RuleFor(x => x.AllowFollowUpDate)
                .NotNull().WithMessage(ValidationKeys.Required)
                .OptionalNotInThePast()
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(12))
                .WithMessage(ValidationKeys.DateTooFar);
        }
    }
}
