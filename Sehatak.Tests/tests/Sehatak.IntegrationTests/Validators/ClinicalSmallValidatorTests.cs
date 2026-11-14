using Sehatak.Application.Common;
using Sehatak.Application.DTOs.ConsultationDto;
using Sehatak.Application.DTOs.DepartmentDto;
using Sehatak.Application.DTOs.DoctorRatingDto;
using Sehatak.Application.DTOs.EmergencyDto;
using Sehatak.Application.DTOs.FollowUpDto;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.Consultation;
using Sehatak.Application.Validators.Department;
using Sehatak.Application.Validators.DoctorRating;
using Sehatak.Application.Validators.Emergency;
using Sehatak.Application.Validators.FollowUp;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class ConsultationValidatorTests
{
    [Fact]
    public void A_video_link_is_a_required_http_url()
    {
        var v = new ConfirmPaymentRequestValidator();

        v.ShouldBeValid(new ConfirmPaymentRequest { VideoLink = "https://meet.example.com/abc" });
        v.ShouldFail(new ConfirmPaymentRequest { VideoLink = "" }, "VideoLink", ValidationKeys.Required);
        v.ShouldFail(new ConfirmPaymentRequest { VideoLink = null }, "VideoLink", ValidationKeys.Required);
        v.ShouldFail(new ConfirmPaymentRequest { VideoLink = "javascript:alert(1)" }, "VideoLink", ValidationKeys.InvalidUrl);
        v.ShouldFail(new ConfirmPaymentRequest { VideoLink = "meet.example.com" }, "VideoLink", ValidationKeys.InvalidUrl);
        v.ShouldFail(new ConfirmPaymentRequest { VideoLink = "https://x.com/" + Long(2048) }, "VideoLink", ValidationKeys.TooLong);
    }

    [Fact]
    public void A_consultation_time_must_be_in_the_future()
    {
        var v = new DoctorScheduleConsultationRequestValidator();
        string[] Messages(DateTime d) => v.Validate(d).Errors.Select(e => e.ErrorMessage).ToArray();

        Messages(DateTime.UtcNow.AddHours(1)).ShouldBeEmpty();
        Messages(DateTime.UtcNow.AddMinutes(-1)).ShouldContain(ValidationKeys.DateInPast);
        Messages(default).ShouldContain(ValidationKeys.Required);
    }

    [Fact]
    public void A_rejection_reason_is_required_up_to_500()
    {
        var v = new RejectReasonRequestValidator();

        v.ShouldBeValid(new RejectReasonRequest { Reason = Long(500) });
        v.ShouldFail(new RejectReasonRequest { Reason = "" }, "Reason", ValidationKeys.Required);
        v.ShouldFail(new RejectReasonRequest { Reason = Long(501) }, "Reason", ValidationKeys.TooLong);
    }
}

public class DepartmentValidatorTests
{
    [Fact]
    public void A_new_department_needs_a_name_and_may_have_a_description_and_logo()
    {
        var v = new DepartmentRequestDtoValidator();

        v.ShouldBeValid(new DepartmentRequestDto { departmentName = "Cardiology" });
        v.ShouldFail(new DepartmentRequestDto { departmentName = "" }, "departmentName", ValidationKeys.Required);
        v.ShouldFail(new DepartmentRequestDto { departmentName = Long(101) }, "departmentName", ValidationKeys.TooLong);
        v.ShouldFail(new DepartmentRequestDto { departmentName = "X", departmentDescription = Long(501) }, "departmentDescription", ValidationKeys.TooLong);
        v.ShouldBeValid(new DepartmentRequestDto { departmentName = "X", logo = FormFileOf("l.webp", "image/webp") });
        v.ShouldFail(new DepartmentRequestDto { departmentName = "X", logo = FormFileOf("l.svg", "image/svg+xml") }, "logo", ValidationKeys.FileTypeNotAllowed);
    }

    private readonly DepartmentUpdateRequestDtoValidator _update = new();

    [Fact]
    public void An_update_needs_an_id_and_at_least_one_change()
    {
        _update.ShouldBeValid(new DepartmentUpdateRequestDto { departmentId = 1, departmentName = "New" });
        _update.ShouldBeValid(new DepartmentUpdateRequestDto { departmentId = 1, departmentdiscription = "d" });
        _update.ShouldBeValid(new DepartmentUpdateRequestDto { departmentId = 1, logo = FormFileOf("l.png", "image/png") });
        _update.ShouldFail(new DepartmentUpdateRequestDto { departmentId = 1 }, "request", ValidationKeys.AtLeastOneField);
        _update.ShouldFail(new DepartmentUpdateRequestDto { departmentId = 0, departmentName = "x" }, "departmentId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void A_sent_name_cannot_be_blank_or_too_long()
    {
        _update.ShouldFail(new DepartmentUpdateRequestDto { departmentId = 1, departmentName = "  " }, "departmentName", ValidationKeys.Required);
        _update.ShouldFail(new DepartmentUpdateRequestDto { departmentId = 1, departmentName = Long(101) }, "departmentName", ValidationKeys.TooLong);
    }

    [Fact]
    public void Removing_a_department_or_a_staff_member_needs_a_positive_id()
    {
        new DepartmentRemoveRequestDtoValidator().ShouldBeValid(new DepartmentRemoveRequestDto { departmentId = 1 });
        new DepartmentRemoveRequestDtoValidator().ShouldFail(new DepartmentRemoveRequestDto { departmentId = 0 }, "departmentId", ValidationKeys.InvalidId);
        new RemoveStaffRequestDtoValidator().ShouldBeValid(new RemoveStaffRequestDto { userId = 1 });
        new RemoveStaffRequestDtoValidator().ShouldFail(new RemoveStaffRequestDto { userId = -1 }, "userId", ValidationKeys.InvalidId);
    }
}

public class DoctorRatingValidatorTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(6, false)]
    [InlineData(-1, false)]
    public void A_rating_is_1_to_5(int rating, bool ok)
    {
        var v = new AddDoctorRatingRequestValidator();
        var dto = new AddDoctorRatingRequest { AppointmentId = 1, Rating = rating };

        if (ok) v.ShouldBeValid(dto);
        else v.ShouldFail(dto, "Rating", ValidationKeys.InvalidRating);
    }

    [Fact]
    public void A_rating_needs_an_appointment_and_a_review_up_to_1000()
    {
        var v = new AddDoctorRatingRequestValidator();

        v.ShouldFail(new AddDoctorRatingRequest { AppointmentId = 0, Rating = 5 }, "AppointmentId", ValidationKeys.InvalidId);
        v.ShouldBeValid(new AddDoctorRatingRequest { AppointmentId = 1, Rating = 5, Review = Long(1000) });
        v.ShouldFail(new AddDoctorRatingRequest { AppointmentId = 1, Rating = 5, Review = Long(1001) }, "Review", ValidationKeys.TooLong);
    }

    private readonly UpdateDoctorRatingRequestValidator _update = new();

    [Fact]
    public void An_update_changes_the_rating_or_the_review()
    {
        _update.ShouldBeValid(new UpdateDoctorRatingRequest { RatingId = 1, Rating = 3 });
        _update.ShouldBeValid(new UpdateDoctorRatingRequest { RatingId = 1, Review = "ok" });
        _update.ShouldFail(new UpdateDoctorRatingRequest { RatingId = 1 }, "request", ValidationKeys.AtLeastOneField);
    }

    [Fact]
    public void An_update_checks_what_is_sent()
    {
        _update.ShouldFail(new UpdateDoctorRatingRequest { RatingId = 0, Rating = 3 }, "RatingId", ValidationKeys.InvalidId);
        _update.ShouldFail(new UpdateDoctorRatingRequest { RatingId = 1, Rating = 0 }, "Rating", ValidationKeys.InvalidRating);
        _update.ShouldFail(new UpdateDoctorRatingRequest { RatingId = 1, Rating = 6 }, "Rating", ValidationKeys.InvalidRating);
        _update.ShouldFail(new UpdateDoctorRatingRequest { RatingId = 1, Review = Long(1001) }, "Review", ValidationKeys.TooLong);
    }
}

public class EmergencyValidatorTests
{
    private readonly EmergencyRequestDtoValidator _create = new();
    private static EmergencyRequestDto Case() => new() { PatientName = "Ali Hasan", DoctorUserId = 1, IsInsurance = false, AmountPaid = 100m };

    [Fact] public void A_paid_emergency_is_valid() => _create.ShouldBeValid(Case());
    [Fact] public void An_insured_emergency_with_no_payment_is_valid() => _create.ShouldBeValid(Case().With("IsInsurance", true).With("AmountPaid", 0m));

    [Fact]
    public void The_patient_name_is_two_to_150()
    {
        _create.ShouldFail(Case().With("PatientName", ""), "PatientName", ValidationKeys.Required);
        _create.ShouldFail(Case().With("PatientName", "A"), "PatientName", ValidationKeys.TooShort);
        _create.ShouldFail(Case().With("PatientName", Long(151)), "PatientName", ValidationKeys.TooLong);
        _create.ShouldBeValid(Case().With("PatientName", Long(150)));
    }

    [Fact]
    public void The_doctor_and_the_amount_are_checked()
    {
        _create.ShouldFail(Case().With("DoctorUserId", 0), "DoctorUserId", ValidationKeys.InvalidId);
        _create.ShouldFail(Case().With("AmountPaid", -1m), "AmountPaid", ValidationKeys.NegativeAmount);
        _create.ShouldFail(Case().With("AmountPaid", 1.234m), "AmountPaid", ValidationKeys.InvalidValue);
    }

    
    // ---------- update ----------

    private readonly UpdateEmergencyRequestDtoValidator _update = new();

    [Fact]
    public void An_update_with_one_field_is_valid()
    {
        _update.ShouldBeValid(new UpdateEmergencyRequestDto { EmergencyId = 1, PatientName = "Ali" });
        _update.ShouldBeValid(new UpdateEmergencyRequestDto { EmergencyId = 1, IsInsurance = false });
        _update.ShouldBeValid(new UpdateEmergencyRequestDto { EmergencyId = 1, AmountPaid = 0m });
    }

    [Fact]
    public void An_update_with_nothing_is_refused() =>
        _update.ShouldFail(new UpdateEmergencyRequestDto { EmergencyId = 1 }, "request", ValidationKeys.AtLeastOneField);

    [Fact]
    public void An_update_checks_what_is_sent()
    {
        _update.ShouldFail(new UpdateEmergencyRequestDto { EmergencyId = 0, PatientName = "Ali" }, "EmergencyId", ValidationKeys.InvalidId);
        _update.ShouldFail(new UpdateEmergencyRequestDto { EmergencyId = 1, PatientName = "A" }, "PatientName", ValidationKeys.TooShort);
        _update.ShouldFail(new UpdateEmergencyRequestDto { EmergencyId = 1, PatientName = "  " }, "PatientName", ValidationKeys.Required);
        _update.ShouldFail(new UpdateEmergencyRequestDto { EmergencyId = 1, AmountPaid = -5m }, "AmountPaid", ValidationKeys.NegativeAmount);
    }

}

public class FollowUpValidatorTests
{
    private static DateOnly Today => ClinicClock.Today;

    public static IEnumerable<object[]> Kinds() => new[]
    {
        new object[] { "Doctor" }, new object[] { "Receptionist" }, new object[] { "Update" }
    };

    private static (FluentValidation.IValidator v, object m) Build(string kind) => kind switch
    {
        "Doctor" => (new DoctorAddFollowUpRequestDtoValidator(), new DoctorAddFollowUpRequestDto { OriginalAppointmentId = 1, PatientId = 2, AllowFollowUpDate = Today.AddDays(7) }),
        "Receptionist" => (new ReceptionistAddFollowUpRequestDtoValidator(), new ReceptionistAddFollowUpRequestDto { OriginalAppointmentId = 1, PatientId = 2, DoctorId = 3, AllowFollowUpDate = Today.AddDays(7) }),
        "Update" => (new UpdateFollowUpRequestDtoValidator(), new UpdateFollowUpRequestDto { FollowUpId = 1, PatientId = 2, AllowFollowUpDate = Today.AddDays(7) }),
        _ => throw new ArgumentException(kind)
    };

    private static void Set(object m, string p, object? v) => m.GetType().GetProperty(p)!.SetValue(m, v);

    private static string[] Errors(FluentValidation.IValidator v, object m, string prop) =>
        v.Validate(new FluentValidation.ValidationContext<object>(m)).Errors.Where(e => e.PropertyName == prop).Select(e => e.ErrorMessage).ToArray();

    [Theory]
    [MemberData(nameof(Kinds))]
    public void The_allowed_date_is_from_today_to_twelve_months_ahead(string kind)
    {
        foreach (var d in new[] { Today, Today.AddMonths(12) })
        {
            var (v, m) = Build(kind);
            Set(m, "AllowFollowUpDate", d);
            Errors(v, m, "AllowFollowUpDate").ShouldBeEmpty($"{kind} {d}");
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_date_outside_that_range_or_missing_is_refused(string kind)
    {
        var (v, m) = Build(kind);

        Set(m, "AllowFollowUpDate", Today.AddDays(-1));
        Errors(v, m, "AllowFollowUpDate").ShouldContain(ValidationKeys.DateInPast);

        Set(m, "AllowFollowUpDate", Today.AddMonths(12).AddDays(1));
        Errors(v, m, "AllowFollowUpDate").ShouldContain(ValidationKeys.DateTooFar);

        Set(m, "AllowFollowUpDate", null);
        Errors(v, m, "AllowFollowUpDate").ShouldContain(ValidationKeys.Required);
    }

    [Theory]
    [InlineData("Doctor", "OriginalAppointmentId")]
    [InlineData("Doctor", "PatientId")]
    [InlineData("Receptionist", "OriginalAppointmentId")]
    [InlineData("Receptionist", "PatientId")]
    [InlineData("Receptionist", "DoctorId")]
    [InlineData("Update", "FollowUpId")]
    [InlineData("Update", "PatientId")]
    public void The_ids_must_be_positive(string kind, string prop)
    {
        var (v, m) = Build(kind);
        Set(m, prop, 0);
        Errors(v, m, prop).ShouldContain(ValidationKeys.InvalidId);
    }
}
