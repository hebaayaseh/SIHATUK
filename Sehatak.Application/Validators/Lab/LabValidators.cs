using FluentValidation;
using Sehatak.Application.DTOs.LabDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Lab
{
    public class LabRequestItemsSummaryDtoValidator : AbstractValidator<LabRequestItemsSummaryDto>
    {
        public LabRequestItemsSummaryDtoValidator()
        {
            // Id here is a ServicePrice id.
            RuleFor(x => x.Id).ValidId();
        }
    }

    public class CreateLabRequestDtoValidator : AbstractValidator<CreateLabRequestDto>
    {
        public CreateLabRequestDtoValidator()
        {
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.AppointmentId).ValidId();
            RuleFor(x => x.Note).OptionalText(ValidationRules.MaxNoteLength);

            // A lab request with no tests produces a zero-price row.
            RuleFor(x => x.LabItems)
                .NotNull().WithMessage(ValidationKeys.ListEmpty)
                .Must(i => i is not null && i.Count > 0).WithMessage(ValidationKeys.ListEmpty)
                .Must(i => i is null || i.Count <= 50).WithMessage(ValidationKeys.ListTooLarge)
                .Must(NoDuplicateServiceIds).WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.LabItems).SetValidator(new LabRequestItemsSummaryDtoValidator());
        }

        internal static bool NoDuplicateServiceIds(List<LabRequestItemsSummaryDto>? items) =>
            items is null || items.Select(i => i.Id).Distinct().Count() == items.Count;
    }

    public class ReceptionistCreateLabRequestDtoValidator : AbstractValidator<ReceptionistCreateLabRequestDto>
    {
        public ReceptionistCreateLabRequestDtoValidator()
        {
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.Note).OptionalText(ValidationRules.MaxNoteLength);

            RuleFor(x => x.LabItems)
                .NotNull().WithMessage(ValidationKeys.ListEmpty)
                .Must(i => i is not null && i.Count > 0).WithMessage(ValidationKeys.ListEmpty)
                .Must(i => i is null || i.Count <= 50).WithMessage(ValidationKeys.ListTooLarge)
                .Must(CreateLabRequestDtoValidator.NoDuplicateServiceIds)
                .WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.LabItems).SetValidator(new LabRequestItemsSummaryDtoValidator());
        }
    }

    public class UpdateLabRequestDtoValidator : AbstractValidator<UpdateLabRequestDto>
    {
        public UpdateLabRequestDtoValidator()
        {
            RuleFor(x => x.LabRequestId).ValidId();
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.AppointmentId).ValidId();
            RuleFor(x => x.Note).OptionalText(ValidationRules.MaxNoteLength);

            RuleFor(x => x.AddLabItems)
                .Must(i => i is null || i.Count <= 50).WithMessage(ValidationKeys.ListTooLarge)
                .Must(CreateLabRequestDtoValidator.NoDuplicateServiceIds)
                .WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.AddLabItems).SetValidator(new LabRequestItemsSummaryDtoValidator());

            RuleForEach(x => x.RemoveLabItems)
                .GreaterThan(0).WithMessage(ValidationKeys.InvalidId);

            RuleFor(x => x.RemoveLabItems)
                .Must(r => r is null || r.Distinct().Count() == r.Count)
                .WithMessage(ValidationKeys.DuplicateItems);

            RuleFor(x => x)
                .Must(x => x.Note is not null ||
                           (x.AddLabItems is not null && x.AddLabItems.Count > 0) ||
                           (x.RemoveLabItems is not null && x.RemoveLabItems.Count > 0))
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }

    public class ReceptionistUpdateLabRequestDtoValidator : AbstractValidator<ReceptionistUpdateLabRequestDto>
    {
        public ReceptionistUpdateLabRequestDtoValidator()
        {
            RuleFor(x => x.LabRequestId).ValidId();
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.Note).OptionalText(ValidationRules.MaxNoteLength);

            RuleFor(x => x.AddLabItems)
                .Must(i => i is null || i.Count <= 50).WithMessage(ValidationKeys.ListTooLarge)
                .Must(CreateLabRequestDtoValidator.NoDuplicateServiceIds)
                .WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.AddLabItems).SetValidator(new LabRequestItemsSummaryDtoValidator());

            RuleForEach(x => x.RemoveLabItems)
                .GreaterThan(0).WithMessage(ValidationKeys.InvalidId);

            RuleFor(x => x)
                .Must(x => x.Note is not null ||
                           (x.AddLabItems is not null && x.AddLabItems.Count > 0) ||
                           (x.RemoveLabItems is not null && x.RemoveLabItems.Count > 0))
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }

    public class LabResultItemDtoValidator : AbstractValidator<LabResultItemDto>
    {
        public LabResultItemDtoValidator()
        {
            RuleFor(x => x.LabRequestItemId).ValidId();

            // lab_request_items.ResultValue is decimal(10,3).
            RuleFor(x => x.ResultValue)
                .GreaterThanOrEqualTo(0).WithMessage(ValidationKeys.NegativeAmount)
                .LessThanOrEqualTo(9_999_999m).WithMessage(ValidationKeys.AmountTooLarge)
                .Must(v => decimal.Round(v, 3) == v).WithMessage(ValidationKeys.InvalidValue);

            RuleFor(x => x.ResultFileUrl).OptionalDocument();
        }
    }

    public class UploadLabResultRequestDtoValidator : AbstractValidator<UploadLabResultRequestDto>
    {
        public UploadLabResultRequestDtoValidator()
        {
            RuleFor(x => x.LabRequestId).ValidId();

            RuleFor(x => x.Results)
                .NotNull().WithMessage(ValidationKeys.ListEmpty)
                .Must(r => r is not null && r.Count > 0).WithMessage(ValidationKeys.ListEmpty)
                .Must(r => r is null || r.Count <= 50).WithMessage(ValidationKeys.ListTooLarge)
                .Must(r => r is null ||
                           r.Select(i => i.LabRequestItemId).Distinct().Count() == r.Count)
                .WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.Results).SetValidator(new LabResultItemDtoValidator());
        }
    }
}
