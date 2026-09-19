using FluentValidation;
using Sehatak.Application.DTOs.SuperAdminDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.SuperAdmin
{
    public class RegisterSuperAdminRequestDtoValidator : AbstractValidator<RegisterSuperAdminRequestDto>
    {
        public RegisterSuperAdminRequestDtoValidator()
        {
            RuleFor(x => x.name).PersonName();
            RuleFor(x => x.email).ValidEmail();
            RuleFor(x => x.phoneNumber).ValidPhone();
            RuleFor(x => x.password).ValidPassword();
            RuleFor(x => x.SuperAdminKey)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(256).WithMessage(ValidationKeys.TooLong);
            RuleFor(x => x.ProfileImageUrl).OptionalImage();
            // CreateAt is server-owned: never trust the value coming from the client.
        }
    }

    public class SuperAdminLoginRequestDtoValidator : AbstractValidator<SuperAdminLoginRequestDto>
    {
        public SuperAdminLoginRequestDtoValidator()
        {
            RuleFor(x => x.email).ValidEmail();
            RuleFor(x => x.password)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(ValidationRules.MaxPasswordLength).WithMessage(ValidationKeys.TooLong);
        }
    }
}
