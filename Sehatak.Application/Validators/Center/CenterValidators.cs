using FluentValidation;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Center
{
    public class CreateCenterRequestDtoValidator : AbstractValidator<createCenterRequestDto>
    {
        public CreateCenterRequestDtoValidator()
        {
            RuleFor(x => x.Name).RequiredText(256);
            RuleFor(x => x.Phone).ValidPhone();
            RuleFor(x => x.Address).RequiredText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.PlanId).ValidId();
            RuleFor(x => x.Logo).OptionalImage();

            RuleFor(x => x.PrepaymentAmount).Money();
            RuleFor(x => x.PrepaymentAmount)
                .GreaterThan(0).WithMessage(ValidationKeys.NegativeAmount)
                .When(x => x.RequiresPrepayment);

            RuleFor(x => x.RefundPolicyHours)
                .InclusiveBetween(0, 24 * 30).WithMessage(ValidationKeys.InvalidValue);
            RuleFor(x => x.PartialRefundPercent).Percent();

            RuleFor(x => x.AddedBySuperAdminId).ValidOptionalId();
            RuleFor(x => x.AdminWhatsappNumber).OptionalPhone();
            RuleFor(x => x.AdminEmail).OptionalEmail();
            // CreatedAt is server-owned.
        }
    }

    public class CenterRegistrationRequestDtoValidator : AbstractValidator<CenterRegistrationRequestDto>
    {
        public CenterRegistrationRequestDtoValidator()
        {
            RuleFor(x => x.CenterName).RequiredText(256);
            RuleFor(x => x.CenterAddress).RequiredText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.CenterPhone).ValidPhone();

            RuleFor(x => x.AdminFirstName).PersonName();
            RuleFor(x => x.AdminLastName).PersonName();
            RuleFor(x => x.AdminEmail).ValidEmail();
            RuleFor(x => x.AdminPhone).ValidPhone();

            // PasswordHash carries the admin's chosen plaintext password.
            RuleFor(x => x.PasswordHash).ValidPassword();

            RuleFor(x => x.PlanId).ValidId();
            RuleFor(x => x.LogoUrl).OptionalImage();

            RuleFor(x => x.PrepaymentAmount).Money();
            RuleFor(x => x.PrepaymentAmount)
                .GreaterThan(0).WithMessage(ValidationKeys.NegativeAmount)
                .When(x => x.RequiresPrepayment);

            RuleFor(x => x.RefundPolicyHours)
                .InclusiveBetween(0, 24 * 30).WithMessage(ValidationKeys.InvalidValue);
            RuleFor(x => x.PartialRefundPercent).Percent();
            // RequestedAt is server-owned.
        }
    }

    public class CreateAdminRequestDtoValidator : AbstractValidator<CreateAdminRequestDto>
    {
        public CreateAdminRequestDtoValidator()
        {
            RuleFor(x => x.FirstName).PersonName();
            RuleFor(x => x.LastName).PersonName();
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.PhoneNumber).ValidPhone();
            RuleFor(x => x.Address).RequiredText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.City).RequiredText(ValidationRules.MaxCityLength);
        }
    }

    public class RejectCenterRequestDtoValidator : AbstractValidator<RejectCenterRequestDto>
    {
        public RejectCenterRequestDtoValidator()
        {
            RuleFor(x => x.rejectionReason).RequiredText(ValidationRules.MaxReasonLength);
        }
    }
}
