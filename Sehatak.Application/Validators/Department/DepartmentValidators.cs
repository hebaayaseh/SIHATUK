using FluentValidation;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Department
{
    public class DepartmentRequestDtoValidator : AbstractValidator<DepartmentRequestDto>
    {
        public DepartmentRequestDtoValidator()
        {
            // departments.Name is NOT NULL, max 100.
            RuleFor(x => x.departmentName).RequiredText(100);
            RuleFor(x => x.departmentDescription).OptionalText(ValidationRules.MaxNoteLength);
            RuleFor(x => x.logo).OptionalImage();
        }
    }

    public class DepartmentUpdateRequestDtoValidator : AbstractValidator<DepartmentUpdateRequestDto>
    {
        public DepartmentUpdateRequestDtoValidator()
        {
            RuleFor(x => x.departmentId).ValidId();

            // Optional, but an empty string would blank a NOT NULL column.
            RuleFor(x => x.departmentName)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(100).WithMessage(ValidationKeys.TooLong)
                .When(x => x.departmentName is not null);

            RuleFor(x => x.departmentdiscription).OptionalText(ValidationRules.MaxNoteLength);
            RuleFor(x => x.logo).OptionalImage();

            RuleFor(x => x)
                .Must(x => x.departmentName is not null ||
                           x.departmentdiscription is not null ||
                           x.logo is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }

    public class DepartmentRemoveRequestDtoValidator : AbstractValidator<DepartmentRemoveRequestDto>
    {
        public DepartmentRemoveRequestDtoValidator()
        {
            RuleFor(x => x.departmentId).ValidId();
        }
    }

    public class RemoveStaffRequestDtoValidator : AbstractValidator<RemoveStaffRequestDto>
    {
        public RemoveStaffRequestDtoValidator()
        {
            RuleFor(x => x.userId).ValidId();
        }
    }
}
