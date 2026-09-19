using FluentValidation;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Consultation
{
    public class ConfirmPaymentRequestValidator : AbstractValidator<ConfirmPaymentRequest>
    {
        public ConfirmPaymentRequestValidator()
        {
            RuleFor(x => x.ScheduledAt)
                .RealDateTime()
                .GreaterThan(_ => DateTime.UtcNow).WithMessage(ValidationKeys.DateInPast)
                .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMonths(6)).WithMessage(ValidationKeys.DateTooFar);

            // Stored and handed to the patient, so it must be a real http(s) link.
            RuleFor(x => x.VideoLink).HttpUrl();
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
