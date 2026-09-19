using FluentValidation;
using Sehatak.Application.DTOs.Email;
using Sehatak.Application.DTOs.FinancialReport;
using Sehatak.Application.DTOs.PatientCenter;
using Sehatak.Application.DTOs.SearchDoctorDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Misc
{
    public class FinancialReportRequestDtoValidator : AbstractValidator<FinancialReportRequestDto>
    {
        public FinancialReportRequestDtoValidator()
        {
            RuleFor(x => x.Year)
                .InclusiveBetween(2000, DateTime.UtcNow.Year + 1).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.Month)
                .InclusiveBetween(1, 12).WithMessage(ValidationKeys.InvalidValue);
        }
    }

    public class SearchDoctorRequestValidator : AbstractValidator<SearchDoctorRequest>
    {
        public SearchDoctorRequestValidator()
        {
            // PagedRequestValidator cannot be Include()d: FluentValidation's
            // Include only accepts IValidator<T> for the exact type, not a base.
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage(ValidationKeys.InvalidValue)
                .LessThanOrEqualTo(10_000).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.PageSize)
                .InclusiveBetween(1, 50).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.doctorName).OptionalText(100);
            RuleFor(x => x.Specialization).OptionalText(256);
            RuleFor(x => x.departmentId).ValidOptionalId();
        }
    }

    public class GetPatientRequestDtoValidator : AbstractValidator<GetPatientRequestDto>
    {
        public GetPatientRequestDtoValidator()
        {
            RuleFor(x => x.patientId).ValidId();
            RuleFor(x => x.status).ValidEnum();
        }
    }

    public class SendEmailDtoValidator : AbstractValidator<SendEmailDto>
    {
        private static readonly string[] AllowedTargets = { "specific", "active", "expired" };

        public SendEmailDtoValidator()
        {
            RuleFor(x => x.Target)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .Must(t => AllowedTargets.Contains(t.Trim().ToLowerInvariant()))
                .WithMessage(ValidationKeys.InvalidValue);

            // "specific" is meaningless without a center to send to.
            RuleFor(x => x.CenterId)
                .NotNull().WithMessage(ValidationKeys.Required)
                .GreaterThan(0).WithMessage(ValidationKeys.InvalidId)
                .When(x => string.Equals(x.Target?.Trim(), "specific", StringComparison.OrdinalIgnoreCase));

            RuleFor(x => x.Subject).RequiredText(200);
            RuleFor(x => x.Message).RequiredText(10_000);
        }
    }
}
