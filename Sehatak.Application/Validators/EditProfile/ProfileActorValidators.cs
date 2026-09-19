using FluentValidation;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Application.DTOs.EditProfile.EditSuperAdmin;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.EditProfile
{
    public class EditPatientInformationRequestValidator : AbstractValidator<EditPatientInformationRequest>
    {
        public EditPatientInformationRequestValidator()
        {
            RuleFor(x => x.firstNmae).OptionalPersonName();
            RuleFor(x => x.lastNmae).OptionalPersonName();
            RuleFor(x => x.address).OptionalText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.city).OptionalText(ValidationRules.MaxCityLength);
            RuleFor(x => x.phoneNumber).OptionalPhone();
            RuleFor(x => x.profileImage).OptionalImage();

            // Sending a new image and asking to remove it at the same time is contradictory.
            RuleFor(x => x)
                .Must(x => !(x.RemoveProfileImage && x.profileImage is not null))
                .WithMessage(ValidationKeys.InvalidValue)
                .OverridePropertyName(nameof(EditPatientInformationRequest.RemoveProfileImage));

            // A PATCH with nothing set is a no-op that still writes UpdatedAt.
            RuleFor(x => x)
                .Must(HasAnyChange).WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }

        private static bool HasAnyChange(EditPatientInformationRequest x) =>
            x.firstNmae is not null || x.lastNmae is not null || x.address is not null ||
            x.city is not null || x.phoneNumber is not null || x.profileImage is not null ||
            x.RemoveProfileImage;
    }

    public class EditSttafInformationRequestValidator : AbstractValidator<EditSttafInformationRequest>
    {
        public EditSttafInformationRequestValidator()
        {
            RuleFor(x => x.firstNmae).OptionalPersonName();
            RuleFor(x => x.lastNmae).OptionalPersonName();
            RuleFor(x => x.address).OptionalText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.city).OptionalText(ValidationRules.MaxCityLength);
            RuleFor(x => x.phoneNumber).OptionalPhone();
            RuleFor(x => x.profileImage).OptionalImage();
            RuleFor(x => x.Specialization).OptionalText(256);
            RuleFor(x => x.Bio).OptionalText(1000);

            RuleFor(x => x)
                .Must(x => !(x.RemoveProfileImage && x.profileImage is not null))
                .WithMessage(ValidationKeys.InvalidValue)
                .OverridePropertyName(nameof(EditSttafInformationRequest.RemoveProfileImage));

            RuleFor(x => x)
                .Must(HasAnyChange).WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }

        private static bool HasAnyChange(EditSttafInformationRequest x) =>
            x.firstNmae is not null || x.lastNmae is not null || x.address is not null ||
            x.city is not null || x.phoneNumber is not null || x.profileImage is not null ||
            x.Specialization is not null || x.Bio is not null || x.OnlineEnabled is not null ||
            x.RemoveProfileImage;
    }

    public class EditCenterInformationRequestValidator : AbstractValidator<EditCenterInformationRequest>
    {
        public EditCenterInformationRequestValidator()
        {
            RuleFor(x => x.PrepaymentAmount).OptionalMoney();
            RuleFor(x => x.PartialRefundPercent).OptionalPercent();
            RuleFor(x => x.RefundPolicyHours)
                .InclusiveBetween(0, 24 * 30).WithMessage(ValidationKeys.InvalidValue);
            RuleFor(x => x.AdminWhatsappNumber).OptionalPhone();
            RuleFor(x => x.Phone).OptionalPhone();
            RuleFor(x => x.Address).OptionalText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.LogoUrl).OptionalImage();

            // Charging a prepayment of 0 is a misconfiguration, not a free booking.
            RuleFor(x => x.PrepaymentAmount)
                .NotNull().WithMessage(ValidationKeys.Required)
                .GreaterThan(0).WithMessage(ValidationKeys.NegativeAmount)
                .When(x => x.RequiresPrepayment == true);
        }
    }

    public class EditNameRequestValidator : AbstractValidator<EditNameRequest>
    {
        public EditNameRequestValidator()
        {
            RuleFor(x => x.Name).PersonName();
        }
    }

    public class EditProfileImageRequestValidator : AbstractValidator<EditProfileImageRequest>
    {
        public EditProfileImageRequestValidator()
        {
            RuleFor(x => x.ImageFile).RequiredImage();
        }
    }
}
