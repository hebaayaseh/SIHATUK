using FluentValidation;
using Sehatak.Application.DTOs.ShiftDto;
using Sehatak.Application.DTOs.StaffAttendance;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Shift
{
    public class ShiftScheduleRequestValidator : AbstractValidator<ShiftScheduleRequest>
    {
        public ShiftScheduleRequestValidator()
        {
            RuleFor(x => x.ShiftName).ValidEnum();
            RuleFor(x => x.StartTime).RealTime();
            RuleFor(x => x.EndTime)
                .RealTime()
                .NotEqual(x => x.StartTime).WithMessage(ValidationKeys.EndBeforeStart);
            // EndTime < StartTime is allowed on purpose: a night shift wraps past midnight.
        }
    }

    public class UpdateShiftSchedualRequestDtoValidator : AbstractValidator<UpdateShiftSchedualRequestDto>
    {
        public UpdateShiftSchedualRequestDtoValidator()
        {
            RuleFor(x => x.ShiftId).ValidId();
            RuleFor(x => x.ShiftName).ValidOptionalEnum();

            // Times are updated as a pair; one alone leaves an inconsistent window.
            RuleFor(x => x.EndTime)
                .NotNull().WithMessage(ValidationKeys.Required)
                .When(x => x.StartTime.HasValue);

            RuleFor(x => x.StartTime)
                .NotNull().WithMessage(ValidationKeys.Required)
                .When(x => x.EndTime.HasValue);

            RuleFor(x => x.EndTime)
                .NotEqual(x => x.StartTime).WithMessage(ValidationKeys.EndBeforeStart)
                .When(x => x.StartTime.HasValue && x.EndTime.HasValue);

            RuleFor(x => x)
                .Must(x => x.ShiftName.HasValue || x.StartTime.HasValue || x.EndTime.HasValue)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }

    public class AssignShiftToStaffRequestDtoValidator : AbstractValidator<AssignShiftToStaffRequestDto>
    {
        public AssignShiftToStaffRequestDtoValidator()
        {
            RuleFor(x => x.UserId).ValidId();
            RuleFor(x => x.ShiftName).ValidEnum();
            RuleFor(x => x.ShiftDate)
                .RealDate()
                .GreaterThanOrEqualTo(_ => ValidationRules.Today.AddDays(-1))
                .WithMessage(ValidationKeys.DateInPast)
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(12))
                .WithMessage(ValidationKeys.DateTooFar);
        }
    }

    public class StaffAttendanceCheckInRequestDtoValidator : AbstractValidator<StaffAttendanceCheckInRequestDto>
    {
        public StaffAttendanceCheckInRequestDtoValidator()
        {
            RuleFor(x => x.AttendanceDate)
                .RealDate()
                .LessThanOrEqualTo(_ => ValidationRules.Today).WithMessage(ValidationKeys.DateInFuture);

            RuleFor(x => x.CheckTime)
                .RealDateTime()
                .LessThanOrEqualTo(_ => DateTime.UtcNow.AddMinutes(5))
                .WithMessage(ValidationKeys.DateInFuture);

            // The clock-in timestamp has to belong to the day being recorded.
            RuleFor(x => x)
                .Must(x => DateOnly.FromDateTime(x.CheckTime) == x.AttendanceDate)
                .WithMessage(ValidationKeys.InvalidDate)
                .OverridePropertyName(nameof(StaffAttendanceCheckInRequestDto.CheckTime))
                .When(x => x.AttendanceDate != default && x.CheckTime != default);
        }
    }

    public class StaffAbsentRequestDtoValidator : AbstractValidator<StaffAbsentRequestDto>
    {
        public StaffAbsentRequestDtoValidator()
        {
            RuleFor(x => x.userId).ValidId();
            RuleFor(x => x.AttendanceDate).RealDate();
        }
    }

    public class StaffOnLeaveRequestDtoValidator : AbstractValidator<StaffOnLeaveRequestDto>
    {
        public StaffOnLeaveRequestDtoValidator()
        {
            RuleFor(x => x.AttendanceDate).RealDate();
        }
    }
}
