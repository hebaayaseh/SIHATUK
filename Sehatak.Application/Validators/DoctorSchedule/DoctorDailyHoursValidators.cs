using FluentValidation;
using Sehatak.Application.DTOs.AddDoctorDailyHour;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.DoctorSchedule
{
    public class AddDoctorDailyHoursRequestValidator : AbstractValidator<AddDoctorDailyHoursRequest>
    {
        public AddDoctorDailyHoursRequestValidator()
        {
            // DayOfWeek is a BCL enum where Sunday == 0, so ValidEnum() (which rejects 0)
            // must not be used here.
            RuleFor(x => x.DayOfWeek)
                .IsInEnum().WithMessage(ValidationKeys.InvalidEnum);

            RuleFor(x => x.StartTime).RealTime();

            RuleFor(x => x.EndTime)
                .RealTime()
                .GreaterThan(x => x.StartTime).WithMessage(ValidationKeys.EndBeforeStart);

            // 0 makes GenerateTheoreticalSlot loop forever; keep this rule.
            RuleFor(x => x.SlotDurationMinutes)
                .InclusiveBetween(5, 480).WithMessage(ValidationKeys.InvalidSlotDuration);

            // The working window must fit at least one slot.
            RuleFor(x => x)
                .Must(x => x.EndTime > x.StartTime &&
                           (x.EndTime - x.StartTime).TotalMinutes >= x.SlotDurationMinutes)
                .WithMessage(ValidationKeys.ShiftTooShort)
                .OverridePropertyName(nameof(AddDoctorDailyHoursRequest.SlotDurationMinutes))
                .When(x => x.SlotDurationMinutes > 0);
        }
    }

    public class UpdateDoctorDailyHousrRequestValidator : AbstractValidator<UpdateDoctorDailyHousrRequest>
    {
        public UpdateDoctorDailyHousrRequestValidator()
        {
            RuleFor(x => x.schedualId).ValidId();

            RuleFor(x => x.DayOfWeek)
                .IsInEnum().WithMessage(ValidationKeys.InvalidEnum);

            RuleFor(x => x.StartTime).RealTime();

            RuleFor(x => x.EndTime)
                .RealTime()
                .GreaterThan(x => x.StartTime).WithMessage(ValidationKeys.EndBeforeStart);

            RuleFor(x => x.SlotDurationMinutes)
                .InclusiveBetween(5, 480).WithMessage(ValidationKeys.InvalidSlotDuration);

            RuleFor(x => x)
                .Must(x => x.EndTime > x.StartTime &&
                           (x.EndTime - x.StartTime).TotalMinutes >= x.SlotDurationMinutes)
                .WithMessage(ValidationKeys.ShiftTooShort)
                .OverridePropertyName(nameof(UpdateDoctorDailyHousrRequest.SlotDurationMinutes))
                .When(x => x.SlotDurationMinutes > 0);
        }
    }
}
