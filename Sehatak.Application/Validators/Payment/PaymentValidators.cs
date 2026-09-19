using FluentValidation;
using Sehatak.Application.DTOs.ConfirmPaymentDto;
using Sehatak.Application.DTOs.PaymentDto;
using Sehatak.Application.DTOs.RecordPaymentRequestDto;
using Sehatak.Application.Validators.Common;
using Sehatak.Domain.Enums.PaymentEnums;

namespace Sehatak.Application.Validators.Payment
{
    public class PaymentRequestDtoValidator : AbstractValidator<PaymentRequestDto>
    {
        public PaymentRequestDtoValidator()
        {
            RuleFor(x => x.Method).ValidEnum();
            RuleFor(x => x.Type).ValidEnum();
            RuleFor(x => x.Status).ValidEnum();
            RuleFor(x => x.ReferenceNumber).OptionalText(100);
            RuleFor(x => x.Notes).OptionalText(ValidationRules.MaxNoteLength);
            RuleFor(x => x.ReceiptImageUrl).OptionalDocument();

            // A transfer without a reference or a receipt cannot be reconciled later.
            RuleFor(x => x)
                .Must(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber) || x.ReceiptImageUrl is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName(nameof(PaymentRequestDto.ReferenceNumber))
                .When(x => x.Method != PaymentMethod.cash);

            // PaidAt is server-owned; a client-supplied future date corrupts reports.
            RuleFor(x => x.PaidAt)
                .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5)).WithMessage(ValidationKeys.DateInFuture);
        }
    }

    public class CollectPaymentRequestDtoValidator : AbstractValidator<CollectPaymentRequestDto>
    {
        public CollectPaymentRequestDtoValidator()
        {
            RuleFor(x => x.Method).ValidEnum();
            RuleFor(x => x.Note).OptionalText(ValidationRules.MaxNoteLength);
        }
    }

    public class RecordPaymentRequestDtoValidator : AbstractValidator<recordPaymentRequestDto>
    {
        private static readonly string[] AllowedMethods = { "cash", "card", "online", "banktransfer", "cheque" };

        public RecordPaymentRequestDtoValidator()
        {
            // PaymentMethod is a free-text string here while the tenant Payment entity
            // uses the PaymentMethod enum. Restrict it so reports stay groupable.
            RuleFor(x => x.PaymentMethod)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(50).WithMessage(ValidationKeys.TooLong)
                .Must(m => AllowedMethods.Contains(m.Replace(" ", "").ToLowerInvariant()))
                .WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.ReferenceNumber).OptionalText(100);
            RuleFor(x => x.Notes).OptionalText(ValidationRules.MaxNoteLength);
            RuleFor(x => x.ReceiptImage).OptionalDocument();

            RuleFor(x => x)
                .Must(x => !string.IsNullOrWhiteSpace(x.ReferenceNumber) || x.ReceiptImage is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName(nameof(recordPaymentRequestDto.ReferenceNumber))
                .When(x => !string.Equals(x.PaymentMethod?.Trim(), "cash", StringComparison.OrdinalIgnoreCase));
        }
    }

    public class PaymentRequestExistValidator : AbstractValidator<PaymentRequestExist>
    {
        private static readonly string[] AllowedMethods = { "cash", "card", "online", "banktransfer", "cheque" };

        public PaymentRequestExistValidator()
        {
            RuleFor(x => x.centerId).ValidOptionalId();
            RuleFor(x => x.SubscriptionId).ValidOptionalId();

            RuleFor(x => x.PaymentMethod)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(50).WithMessage(ValidationKeys.TooLong)
                .Must(m => AllowedMethods.Contains(m.Replace(" ", "").ToLowerInvariant()))
                .WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.ReferenceNumber).OptionalText(100);
            RuleFor(x => x.Notes).OptionalText(ValidationRules.MaxNoteLength);
            RuleFor(x => x.ReceiptImage).OptionalDocument();

            // The payment has to attach to something.
            RuleFor(x => x)
                .Must(x => x.centerId.HasValue || x.SubscriptionId.HasValue)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName(nameof(PaymentRequestExist.centerId));
        }
    }
}
