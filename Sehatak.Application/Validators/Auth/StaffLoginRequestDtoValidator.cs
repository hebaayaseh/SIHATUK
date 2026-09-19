using FluentValidation;
using Sehatak.Application.DTOs.StaffLogIn;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Auth
{
    public class StaffLoginRequestDtoValidator : AbstractValidator<StaffLoginRequestDto>
    {
        public StaffLoginRequestDtoValidator()
        {
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.Password)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(ValidationRules.MaxPasswordLength).WithMessage(ValidationKeys.TooLong);
        }
    }
}
