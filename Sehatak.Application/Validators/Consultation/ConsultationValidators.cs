using FluentValidation;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Consultation
{
    public class ConfirmPaymentRequestValidator : AbstractValidator<ConfirmPaymentRequest>
    {
        public ConfirmPaymentRequestValidator()
        {
            
            // Stored and handed to the patient, so it must be a real http(s) link.
            RuleFor(x => x.VideoLink).HttpUrl();
        }
    }
    public class DoctorScheduleConsultationRequestValidator : AbstractValidator<DateTime>
    {
        public DoctorScheduleConsultationRequestValidator()
        {
            RuleFor(x => x)
                .Must(d => d != default).WithMessage(ValidationKeys.Required)
                .GreaterThan(_ => DateTime.UtcNow).WithMessage(ValidationKeys.DateInPast);
        }
    }
    public class RejectReasonRequestValidator : AbstractValidator<RejectReasonRequest>
    {
        public RejectReasonRequestValidator()
        {
            RuleFor(x => x.Reason).RequiredText(ValidationRules.MaxReasonLength);
        }
    }
}
