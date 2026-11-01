using FluentValidation;
using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Emergency
{
    public class EmergencyRequestDtoValidator : AbstractValidator<EmergencyRequestDto>
    {
        public EmergencyRequestDtoValidator()
        {
            // emergency_cases.PatientName is NOT NULL, max 150.
            RuleFor(x => x.PatientName)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MinimumLength(2).WithMessage(ValidationKeys.TooShort)
                .MaximumLength(150).WithMessage(ValidationKeys.TooLong);

            RuleFor(x => x.DoctorUserId).ValidId();
            RuleFor(x => x.AmountPaid).Money();

            
        }
    }

    public class UpdateEmergencyRequestDtoValidator : AbstractValidator<UpdateEmergencyRequestDto>
    {
        public UpdateEmergencyRequestDtoValidator()
        {
            RuleFor(x => x.EmergencyId).ValidId();

            RuleFor(x => x.PatientName)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MinimumLength(2).WithMessage(ValidationKeys.TooShort)
                .MaximumLength(150).WithMessage(ValidationKeys.TooLong)
                .When(x => x.PatientName is not null);

            RuleFor(x => x.AmountPaid).OptionalMoney();

            RuleFor(x => x)
                .Must(x => x.PatientName is not null || x.IsInsurance is not null || x.AmountPaid is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }
}
