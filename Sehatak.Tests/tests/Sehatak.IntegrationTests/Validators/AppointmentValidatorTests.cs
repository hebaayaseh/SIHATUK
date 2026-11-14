using FluentValidation;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.AppointmentDto;
using Sehatak.Application.Validators.Appointment;
using Sehatak.Application.Validators.Common;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class AppointmentValidatorTests
{
    private static readonly TimeOnly Nine = new(9, 0);
    private static DateOnly Today => ClinicClock.Today;

    // ---------- booking window: today .. today + 6 months, shared by every "book/reschedule" validator ----------

    public static IEnumerable<object[]> DateRules() => new[]
    {
        new object[] { "Book", "dateOnly" },
        new object[] { "Reschedule", "date" },
        new object[] { "ReceptionistBook", "dateOnly" },
        new object[] { "ReceptionistReschedule", "date" },
    };

    private static (IValidator v, object model) Build(string kind) => kind switch
    {
        "Book" => (new BookAppointmentRequestValidator(), new BookAppointmentRequest { timeSlot = Nine, dateOnly = Today }),
        "Reschedule" => (new RescheduleAppointmentRequestValidator(), new RescheduleAppointmentRequest { appointmentId = 1, timeSlot = Nine, date = Today }),
        "ReceptionistBook" => (new ReceptionistBookRequestDtoValidator(), new ReceptionistBookRequestDto { PatientId = 1, timeSlot = Nine, dateOnly = Today }),
        "ReceptionistReschedule" => (new ReceptionistRescheduleAppointmentRequestValidator(), new ReceptionistRescheduleAppointmentRequest { PatientId = 1, appointmentId = 1, timeSlot = Nine, date = Today }),
        _ => throw new ArgumentException(kind)
    };

    private static void Set(object model, string prop, object? value) => model.GetType().GetProperty(prop)!.SetValue(model, value);

    private static IReadOnlyList<(string Prop, string Msg)> Run(IValidator v, object model) =>
        v.Validate(new ValidationContext<object>(model)).Errors.Select(e => (e.PropertyName, e.ErrorMessage)).ToList();

    [Theory]
    [MemberData(nameof(DateRules))]
    public void A_booking_is_valid_from_today_up_to_six_months_ahead(string kind, string dateProp)
    {
        foreach (var d in new[] { Today, Today.AddDays(1), Today.AddMonths(6) })
        {
            var (v, m) = Build(kind);
            Set(m, dateProp, d);
            Run(v, m).ShouldBeEmpty($"{kind} {d}");
        }
    }

    [Theory]
    [MemberData(nameof(DateRules))]
    public void A_booking_in_the_past_is_refused(string kind, string dateProp)
    {
        var (v, m) = Build(kind);
        Set(m, dateProp, Today.AddDays(-1));
        Run(v, m).ShouldContain((dateProp, ValidationKeys.DateInPast));
    }

    [Theory]
    [MemberData(nameof(DateRules))]
    public void A_booking_more_than_six_months_ahead_is_refused(string kind, string dateProp)
    {
        var (v, m) = Build(kind);
        Set(m, dateProp, Today.AddMonths(6).AddDays(1));
        Run(v, m).ShouldContain((dateProp, ValidationKeys.DateTooFar));
    }

    [Theory]
    [MemberData(nameof(DateRules))]
    public void An_omitted_date_or_time_is_refused(string kind, string dateProp)
    {
        var (v, m) = Build(kind);
        Set(m, dateProp, default(DateOnly));
        Run(v, m).ShouldContain((dateProp, ValidationKeys.Required));

        var (v2, m2) = Build(kind);
        Set(m2, "timeSlot", default(TimeOnly));
        Run(v2, m2).ShouldContain(("timeSlot", ValidationKeys.Required));
    }

    [Theory]
    [InlineData("Book")]
    [InlineData("ReceptionistBook")]
    public void Sub_patient_and_follow_up_ids_are_optional_but_never_zero_or_negative(string kind)
    {
        foreach (var prop in new[] { "SubPatientId", "FollowUpId" })
        {
            var (v, m) = Build(kind);
            Set(m, prop, null);
            Run(v, m).ShouldBeEmpty();

            Set(m, prop, 5);
            Run(v, m).ShouldBeEmpty();

            foreach (var bad in new[] { 0, -1 })
            {
                Set(m, prop, bad);
                Run(v, m).ShouldContain((prop, ValidationKeys.InvalidId));
            }
        }
    }

    [Theory]
    [InlineData("Reschedule", "appointmentId")]
    [InlineData("ReceptionistReschedule", "appointmentId")]
    [InlineData("ReceptionistBook", "PatientId")]
    [InlineData("ReceptionistReschedule", "PatientId")]
    public void Required_ids_must_be_positive(string kind, string prop)
    {
        foreach (var bad in new[] { 0, -3 })
        {
            var (v, m) = Build(kind);
            Set(m, prop, bad);
            Run(v, m).ShouldContain((prop, ValidationKeys.InvalidId));
        }
    }

    // ---------- cancel: the date may be in the past (cancelling a missed visit), but must be real ----------

    [Fact]
    public void Cancelling_needs_a_real_time_and_date_but_the_date_can_be_in_the_past()
    {
        var v = new CancelAppointmentRequestValidator();

        v.ShouldBeValid(new CancelAppointmentRequest { timeSlot = Nine, date = Today.AddDays(-3) });
        v.ShouldFail(new CancelAppointmentRequest { timeSlot = default, date = Today }, "timeSlot", ValidationKeys.Required);
        v.ShouldFail(new CancelAppointmentRequest { timeSlot = Nine, date = default }, "date", ValidationKeys.Required);
        v.ShouldFail(new CancelAppointmentRequest { timeSlot = Nine, date = Today, SubPatientId = 0 }, "SubPatientId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void The_receptionist_cancel_checks_the_patient_too()
    {
        var v = new ReceptionistCancelAppointmentRequestValidator();

        v.ShouldBeValid(new ReceptionistCancelAppointmentRequest { PatientId = 1, timeSlot = Nine, date = Today, Resone = "x" });
        v.ShouldFail(new ReceptionistCancelAppointmentRequest { PatientId = 0, timeSlot = Nine, date = Today, Resone = "x" }, "PatientId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void Deleting_a_doctor_slot_cannot_target_the_past()
    {
        var v = new DeleteDoctorSlotRequestValidator();

        v.ShouldBeValid(new DeleteDoctorSlotRequest { timeSlot = Nine, date = Today });
        v.ShouldFail(new DeleteDoctorSlotRequest { timeSlot = Nine, date = Today.AddDays(-1) }, "date", ValidationKeys.DateInPast);
        v.ShouldFail(new DeleteDoctorSlotRequest { timeSlot = default, date = Today }, "timeSlot", ValidationKeys.Required);
    }

    // ---------- free text that is stored and written to the audit log with no limit ----------

    [Fact]
    [Trait("Category", "KnownBug")]
    public void A_cancellation_reason_has_a_length_limit()
    {
        new CancelAppointmentRequestValidator()
            .ShouldFail(new CancelAppointmentRequest { timeSlot = Nine, date = Today, Resone = Long(100_000) }, "Resone", ValidationKeys.TooLong);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public void A_receptionist_cancellation_reason_has_a_length_limit()
    {
        new ReceptionistCancelAppointmentRequestValidator()
            .ShouldFail(new ReceptionistCancelAppointmentRequest { PatientId = 1, timeSlot = Nine, date = Today, Resone = Long(100_000) }, "Resone", ValidationKeys.TooLong);
    }

    [Fact]
    [Trait("Category", "KnownBug")]
    public void A_deleted_slot_reason_has_a_length_limit()
    {
        new DeleteDoctorSlotRequestValidator()
            .ShouldFail(new DeleteDoctorSlotRequest { timeSlot = Nine, date = Today, Reason = Long(100_000) }, "Reason", ValidationKeys.TooLong);
    }
}
