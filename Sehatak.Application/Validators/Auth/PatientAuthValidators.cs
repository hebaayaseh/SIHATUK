using FluentValidation;
using Sehatak.Application.DTOs.Auth;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Auth
{
    public class RegisterRequestDtoValidator : AbstractValidator<RegisterRequestDto>
    {
        public RegisterRequestDtoValidator()
        {
            RuleFor(x => x.firstName).PersonName();
            RuleFor(x => x.lastName).PersonName();
            RuleFor(x => x.email).ValidEmail();
            RuleFor(x => x.phoneNumber).ValidPhone();
            RuleFor(x => x.password).ValidPassword();
            RuleFor(x => x.address).RequiredText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.city).RequiredText(ValidationRules.MaxCityLength);
            RuleFor(x => x.ProfileImage).OptionalImage();

            // Gender/BloodType start at 1, so an omitted field would persist as 0.
            RuleFor(x => x.gender).ValidEnum();
            RuleFor(x => x.bloodType).ValidEnum();
            RuleFor(x => x.DateOfBith).DateOfBirth();
        }
    }

    public class VerifyOtpRequestDtoValidator : AbstractValidator<VerifyOtpRequestDto>
    {
        public VerifyOtpRequestDtoValidator()
        {
            RuleFor(x => x.UserId).ValidId();
            RuleFor(x => x.code).ValidOtpCode();
        }
    }

    public class PatientRequestDtoValidator : AbstractValidator<PatientRequestDto>
    {
        public PatientRequestDtoValidator()
        {
            RuleFor(x => x.email).ValidEmail();
            // Login only checks presence: strength rules belong on registration,
            // otherwise older accounts can no longer sign in.
            RuleFor(x => x.password)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(ValidationRules.MaxPasswordLength).WithMessage(ValidationKeys.TooLong);
        }
    }

    public class RefreshTokenRequestDtoValidator : AbstractValidator<RefreshTokenRequestDto>
    {
        public RefreshTokenRequestDtoValidator()
        {
            RuleFor(x => x.RefreshToken)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(512).WithMessage(ValidationKeys.TooLong);
        }
    }

    public class LogoutRequestDtoValidator : AbstractValidator<LogoutRequestDto>
    {
        public LogoutRequestDtoValidator()
        {
            RuleFor(x => x.RefreshToken)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(512).WithMessage(ValidationKeys.TooLong);
        }
    }
}
