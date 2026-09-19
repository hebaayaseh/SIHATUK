using FluentValidation;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Appointment
{
    public class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
    {
        public BookAppointmentRequestValidator()
        {
            RuleFor(x => x.timeSlot).RealTime();
            RuleFor(x => x.dateOnly).NotInThePast()
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(6))
                .WithMessage(ValidationKeys.DateTooFar);
            RuleFor(x => x.SubPatientId).ValidOptionalId();
            RuleFor(x => x.FollowUpId).ValidOptionalId();
        }
    }

    public class CancelAppointmentRequestValidator : AbstractValidator<CancelAppointmentRequest>
    {
        public CancelAppointmentRequestValidator()
        {
            RuleFor(x => x.timeSlot).RealTime();
            RuleFor(x => x.date).RealDate();
            RuleFor(x => x.SubPatientId).ValidOptionalId();
        }
    }

    public class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
    {
        public RescheduleAppointmentRequestValidator()
        {
            RuleFor(x => x.appointmentId).ValidId();
            RuleFor(x => x.timeSlot).RealTime();
            RuleFor(x => x.date).NotInThePast()
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(6))
                .WithMessage(ValidationKeys.DateTooFar);
            RuleFor(x => x.SubPatientId).ValidOptionalId();
        }
    }

    public class DeleteDoctorSlotRequestValidator : AbstractValidator<DeleteDoctorSlotRequest>
    {
        public DeleteDoctorSlotRequestValidator()
        {
            RuleFor(x => x.timeSlot).RealTime();
            RuleFor(x => x.date).NotInThePast();
        }
    }

    public class ReceptionistBookRequestDtoValidator : AbstractValidator<ReceptionistBookRequestDto>
    {
        public ReceptionistBookRequestDtoValidator()
        {
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.timeSlot).RealTime();
            RuleFor(x => x.dateOnly).NotInThePast()
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(6))
                .WithMessage(ValidationKeys.DateTooFar);
            RuleFor(x => x.SubPatientId).ValidOptionalId();
            RuleFor(x => x.FollowUpId).ValidOptionalId();
        }
    }

    public class ReceptionistCancelAppointmentRequestValidator
        : AbstractValidator<ReceptionistCancelAppointmentRequest>
    {
        public ReceptionistCancelAppointmentRequestValidator()
        {
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.timeSlot).RealTime();
            RuleFor(x => x.date).RealDate();
            RuleFor(x => x.SubPatientId).ValidOptionalId();
        }
    }

    public class ReceptionistRescheduleAppointmentRequestValidator
        : AbstractValidator<ReceptionistRescheduleAppointmentRequest>
    {
        public ReceptionistRescheduleAppointmentRequestValidator()
        {
            RuleFor(x => x.PatientId).ValidId();
            RuleFor(x => x.appointmentId).ValidId();
            RuleFor(x => x.timeSlot).RealTime();
            RuleFor(x => x.date).NotInThePast()
                .LessThanOrEqualTo(_ => ValidationRules.Today.AddMonths(6))
                .WithMessage(ValidationKeys.DateTooFar);
            RuleFor(x => x.SubPatientId).ValidOptionalId();
        }
    }
}
