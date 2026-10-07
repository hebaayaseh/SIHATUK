

using FluentValidation;
using Sehatak.Application.DTOs.MedicationReminderDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.MedicationReminder
{
    public class MedicationReminderRequestDtoValidator : AbstractValidator<MedicationReminderRequestDto>
    {
        public MedicationReminderRequestDtoValidator()
        {
            RuleFor(x => x.MedicationName).RequiredText(200);
            RuleFor(x => x.DurationInDays).LessThanOrEqualTo(90);
            RuleFor(x => x.ReminderTime).RealTime();
            RuleFor(x => x.SelectedDays).ValidEnum();
            RuleFor(x => x.IsDaily).Null();
        }

    }
    

}
