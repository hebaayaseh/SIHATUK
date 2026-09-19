using FluentValidation;
using Sehatak.Application.DTOs.SubPatientDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.SubPatient
{
    public class SummarySubPatientRequestDtoValidator : AbstractValidator<SummarySubPatientRequestDto>
    {
        public SummarySubPatientRequestDtoValidator()
        {
            // patients.FirstName / LastName are nullable in the schema, which is why
            // every "PatientName" in the responses can come back as a bare space.
            // Enforcing them here is what stops that at the source.
            RuleFor(x => x.SubPatientFirstName).PersonName();
            RuleFor(x => x.SubPatientLastName).PersonName();
            RuleFor(x => x.BloodType).ValidEnum();
            RuleFor(x => x.Gender).ValidEnum();
            RuleFor(x => x.WhatAppNumber).ValidPhone();
            RuleFor(x => x.DateOfBith).DateOfBirth();
        }
    }

    public class AddSubPatientRequestDtoValidator : AbstractValidator<AddSubPatientRequestDto>
    {
        public AddSubPatientRequestDtoValidator()
        {
            RuleFor(x => x.SubPatients)
                .NotNull().WithMessage(ValidationKeys.ListEmpty)
                .Must(s => s is not null && s.Count > 0).WithMessage(ValidationKeys.ListEmpty)
                .Must(s => s is null || s.Count <= 20).WithMessage(ValidationKeys.ListTooLarge);

            RuleForEach(x => x.SubPatients).SetValidator(new SummarySubPatientRequestDtoValidator());
        }
    }

    public class UpdateSubPatientRequestDtoValidator : AbstractValidator<UpdateSubPatientRequestDto>
    {
        public UpdateSubPatientRequestDtoValidator()
        {
            RuleFor(x => x.SubPatientFirstName).OptionalPersonName();
            RuleFor(x => x.SubPatientLastName).OptionalPersonName();
            RuleFor(x => x.BloodType).ValidOptionalEnum();
            RuleFor(x => x.Gender).ValidOptionalEnum();
            RuleFor(x => x.WhatAppNumber).OptionalPhone();
            RuleFor(x => x.DateOfBith).OptionalDateOfBirth();

            RuleFor(x => x)
                .Must(x => x.SubPatientFirstName is not null || x.SubPatientLastName is not null ||
                           x.BloodType is not null || x.Gender is not null ||
                           x.WhatAppNumber is not null || x.DateOfBith is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }
}
