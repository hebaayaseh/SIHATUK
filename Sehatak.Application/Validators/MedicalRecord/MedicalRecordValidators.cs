using FluentValidation;
using Sehatak.Application.DTOs.MedicalRecordDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.MedicalRecord
{
    public class MedicalRecordItemDtoValidator : AbstractValidator<MedicalRecordItemDto>
    {
        public MedicalRecordItemDtoValidator()
        {
            // Id is a ServicePrice id.
            RuleFor(x => x.Id).ValidId();
            RuleFor(x => x.Quantity)
                .InclusiveBetween(1, 1000).WithMessage(ValidationKeys.InvalidQuantity);
        }
    }

    public class MedicalRecordDetailRequestDtoValidator : AbstractValidator<MedicalRecordDetailRequestDto>
    {
        public MedicalRecordDetailRequestDtoValidator()
        {
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.AppointmentId).ValidOptionalId();
            RuleFor(x => x.ConsultationId).ValidOptionalId();

            // medical_records.Prescription / Notes are NOT NULL, max 500.
            RuleFor(x => x.Prescription).RequiredText(500);
            RuleFor(x => x.Diagnosis).OptionalText(1000);
            RuleFor(x => x.ConsultationCost).OptionalMoney();

            // The service throws MedicalRecord.MustLinkToAppointmentOrConsultation /
            // MedicalRecord.CannotLinkToBoth; catch it before touching the DB.
            RuleFor(x => x)
                .Must(x => x.AppointmentId.HasValue ^ x.ConsultationId.HasValue)
                .WithMessage(ValidationKeys.InvalidValue)
                .OverridePropertyName(nameof(MedicalRecordDetailRequestDto.AppointmentId));

            RuleFor(x => x.Items)
                .Must(i => i is null || i.Count <= 100).WithMessage(ValidationKeys.ListTooLarge)
                .Must(i => i is null || i.Select(v => v.Id).Distinct().Count() == i.Count)
                .WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.Items).SetValidator(new MedicalRecordItemDtoValidator());
        }
    }

    public class UpdateMedicalRecordRequestDtoValidator : AbstractValidator<UpdateMedicalRecordRequestDto>
    {
        public UpdateMedicalRecordRequestDtoValidator()
        {
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.MedicalRecordId).ValidId();
            RuleFor(x => x.AppointmentId).ValidOptionalId();
            RuleFor(x => x.ConsultationId).ValidOptionalId();

            RuleFor(x => x.Prescription)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(500).WithMessage(ValidationKeys.TooLong)
                .When(x => x.Prescription is not null);

            RuleFor(x => x.RecordNotes)
                .NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(500).WithMessage(ValidationKeys.TooLong)
                .When(x => x.RecordNotes is not null);

            RuleFor(x => x.PaymentNotes).OptionalText(ValidationRules.MaxNoteLength);
            RuleFor(x => x.Diagnosis).OptionalText(1000);
            RuleFor(x => x.CustomConsultationPrice).OptionalMoney();

            RuleFor(x => x)
                .Must(x => !(x.AppointmentId.HasValue && x.ConsultationId.HasValue))
                .WithMessage(ValidationKeys.InvalidValue)
                .OverridePropertyName(nameof(UpdateMedicalRecordRequestDto.AppointmentId));

            RuleFor(x => x.Items)
                .Must(i => i is null || i.Count <= 100).WithMessage(ValidationKeys.ListTooLarge)
                .Must(i => i is null || i.Select(v => v.Id).Distinct().Count() == i.Count)
                .WithMessage(ValidationKeys.DuplicateItems);

            RuleForEach(x => x.Items).SetValidator(new MedicalRecordItemDtoValidator());

            RuleForEach(x => x.RemoveItemIds)
                .GreaterThan(0).WithMessage(ValidationKeys.InvalidId);

            RuleFor(x => x)
                .Must(HasAnyChange).WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }

        private static bool HasAnyChange(UpdateMedicalRecordRequestDto x) =>
            x.Prescription is not null || x.PaymentNotes is not null || x.RecordNotes is not null ||
            x.Diagnosis is not null || x.CustomConsultationPrice is not null ||
            (x.Items is not null && x.Items.Count > 0) ||
            (x.RemoveItemIds is not null && x.RemoveItemIds.Count > 0);
    }
}
