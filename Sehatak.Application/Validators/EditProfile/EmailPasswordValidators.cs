using FluentValidation;
using Sehatak.Application.DTOs.EditProfile.EditEmailOrPasswored;
using Sehatak.Application.DTOs.EditProfileDto.EditEmailOrPasswored;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.EditProfile
{
    public class EditEmailRequestValidator : AbstractValidator<EditEmailRequest>
    {
        public EditEmailRequestValidator()
        {
            RuleFor(x => x.Email).ValidEmail();
        }
    }

    public class ConfirmEditEmailRequestValidator : AbstractValidator<ConfirmEditEmailRequest>
    {
        public ConfirmEditEmailRequestValidator()
        {
            RuleFor(x => x.Code).ValidOtpCode();
        }
    }

    public class EditPasswordRequestValidator : AbstractValidator<EditPasswordRequest>
    {
        public EditPasswordRequestValidator()
        {
            // Named PasswordHash on the DTO, but it carries the plaintext password.
            RuleFor(x => x.PasswordHash).ValidPassword();
            RuleFor(x => x.ConfirmPassword)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .Equal(x => x.PasswordHash).WithMessage(ValidationKeys.PasswordMismatch);
        }
    }

    public class ConfirmEditPasswordRequestValidator : AbstractValidator<ConfirmEditPasswordRequest>
    {
        public ConfirmEditPasswordRequestValidator()
        {
            RuleFor(x => x.Code).ValidOtpCode();
        }
    }

    public class ForgetPasswordRequestValidator : AbstractValidator<ForgetPasswordRequest>
    {
        public ForgetPasswordRequestValidator()
        {
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.newPassword).ValidPassword();
            RuleFor(x => x.confirmPassword)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .Equal(x => x.newPassword).WithMessage(ValidationKeys.PasswordMismatch);
        }
    }

    public class ConfirmForgetPasswordRequestValidator : AbstractValidator<ConfirmForgetPasswordRequest>
    {
        public ConfirmForgetPasswordRequestValidator()
        {
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.Code).ValidOtpCode();
        }
    }
}
