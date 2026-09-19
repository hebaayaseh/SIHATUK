using FluentValidation;
using Sehatak.Application.DTOs.StaffSignup;
using Sehatak.Application.Validators.Common;
using Sehatak.Domain.Enums;

namespace Sehatak.Application.Validators.Staff
{
    public class AddStaffRequestDtoValidator : AbstractValidator<AddStaffRequestDto>
    {
        // Doctors go through DoctorRequestDto (they need a department + specialization),
        // and nobody creates a SuperAdmin or a Patient through this endpoint.
        private static readonly userRole[] AllowedRoles =
        {
            userRole.Admin,
            userRole.Receptionist,
            userRole.LabTechnician,
            userRole.Nurse,
            userRole.GeneralDoctor
        };

        public AddStaffRequestDtoValidator()
        {
            RuleFor(x => x.userRole)
                .ValidEnum()
                .Must(r => AllowedRoles.Contains(r)).WithMessage(ValidationKeys.InvalidEnum);

            RuleFor(x => x.FirstName).PersonName();
            RuleFor(x => x.LastName).PersonName();
            RuleFor(x => x.email).ValidEmail();
            RuleFor(x => x.phoneNumber).OptionalPhone();
            RuleFor(x => x.address).RequiredText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.city).RequiredText(ValidationRules.MaxCityLength);
            RuleFor(x => x.ProfileImage).OptionalImage();
        }
    }

    public class DoctorRequestDtoValidator : AbstractValidator<DoctorRequestDto>
    {
        public DoctorRequestDtoValidator()
        {
            RuleFor(x => x.doctorFirstName).PersonName();
            RuleFor(x => x.doctorLastName).PersonName();
            RuleFor(x => x.email).ValidEmail();
            RuleFor(x => x.phoneNumber).OptionalPhone();
            RuleFor(x => x.address).RequiredText(ValidationRules.MaxAddressLength);
            RuleFor(x => x.city).RequiredText(ValidationRules.MaxCityLength);
            RuleFor(x => x.ProfileImage).OptionalImage();

            // doctors.Specialization and doctors.Bio are NOT NULL in the tenant schema.
            RuleFor(x => x.Specialization).RequiredText(256);
            RuleFor(x => x.Bio).RequiredText(1000);
            RuleFor(x => x.departmentId).ValidId();
            // createdAt is server-owned; ignore whatever the client sends.
        }
    }
}
