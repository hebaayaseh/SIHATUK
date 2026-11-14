using Sehatak.Application.Common;
using Sehatak.Application.DTOs.CreateCenterRequestDto;
using Sehatak.Application.DTOs.Email;
using Sehatak.Application.DTOs.FinancialReport;
using Sehatak.Application.DTOs.PatientCenter;
using Sehatak.Application.DTOs.SearchDoctorDto;
using Sehatak.Application.Validators.Center;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.Misc;
using Sehatak.Domain.Enums;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class CenterValidatorTests
{
    // ---------- created by a super admin ----------

    private readonly CreateCenterRequestDtoValidator _create = new();

    private static createCenterRequestDto NewCenter() => new()
    {
        Name = "Hope Clinic",
        Phone = "0599123456",
        Address = "Main street 1",
        PlanId = 1
    };

    [Fact] public void A_center_with_the_defaults_is_valid() => _create.ShouldBeValid(NewCenter());

    [Fact]
    public void Name_phone_and_address_are_required_with_limits()
    {
        _create.ShouldFail(NewCenter().With("Name", ""), "Name", ValidationKeys.Required);
        _create.ShouldFail(NewCenter().With("Name", Long(257)), "Name", ValidationKeys.TooLong);
        _create.ShouldBeValid(NewCenter().With("Name", Long(256)));
        _create.ShouldFail(NewCenter().With("Phone", "12"), "Phone", ValidationKeys.InvalidPhone);
        _create.ShouldFail(NewCenter().With("Phone", ""), "Phone", ValidationKeys.Required);
        _create.ShouldFail(NewCenter().With("Address", ""), "Address", ValidationKeys.Required);
        _create.ShouldFail(NewCenter().With("Address", Long(501)), "Address", ValidationKeys.TooLong);
    }

    [Fact] public void A_plan_is_required() => _create.ShouldFail(NewCenter().With("PlanId", 0), "PlanId", ValidationKeys.InvalidId);

    [Fact]
    public void The_logo_must_be_an_image()
    {
        _create.ShouldBeValid(NewCenter().With("Logo", FormFileOf("l.png", "image/png")));
        _create.ShouldFail(NewCenter().With("Logo", FormFileOf("l.gif", "image/gif")), "Logo", ValidationKeys.FileTypeNotAllowed);
    }

    [Fact]
    public void A_prepayment_amount_is_required_only_when_prepayment_is_on()
    {
        _create.ShouldBeValid(NewCenter().With("RequiresPrepayment", false).With("PrepaymentAmount", 0m));
        _create.ShouldFail(NewCenter().With("RequiresPrepayment", true).With("PrepaymentAmount", 0m), "PrepaymentAmount", ValidationKeys.NegativeAmount);
        _create.ShouldBeValid(NewCenter().With("RequiresPrepayment", true).With("PrepaymentAmount", 50m));
        _create.ShouldFail(NewCenter().With("PrepaymentAmount", -1m), "PrepaymentAmount", ValidationKeys.NegativeAmount);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(720, true)]
    [InlineData(-1, false)]
    [InlineData(721, false)]
    public void The_refund_window_is_0_to_720_hours(int hours, bool ok)
    {
        if (ok) _create.ShouldBeValid(NewCenter().With("RefundPolicyHours", hours));
        else _create.ShouldFail(NewCenter().With("RefundPolicyHours", hours), "RefundPolicyHours", ValidationKeys.InvalidValue);
    }

    [Fact]
    public void The_partial_refund_is_a_percentage()
    {
        _create.ShouldBeValid(NewCenter().With("PartialRefundPercent", 100m));
        _create.ShouldFail(NewCenter().With("PartialRefundPercent", 100.5m), "PartialRefundPercent", ValidationKeys.InvalidPercent);
        _create.ShouldFail(NewCenter().With("PartialRefundPercent", -1m), "PartialRefundPercent", ValidationKeys.InvalidPercent);
    }

    [Fact]
    public void The_optional_admin_details_are_checked_when_sent()
    {
        _create.ShouldFail(NewCenter().With("AddedBySuperAdminId", 0), "AddedBySuperAdminId", ValidationKeys.InvalidId);
        _create.ShouldFail(NewCenter().With("AdminWhatsappNumber", "abc"), "AdminWhatsappNumber", ValidationKeys.InvalidPhone);
        _create.ShouldFail(NewCenter().With("AdminEmail", "nope"), "AdminEmail", ValidationKeys.InvalidEmail);
        _create.ShouldBeValid(NewCenter().With("AdminEmail", "a@b.co").With("AdminWhatsappNumber", "0599123456").With("AddedBySuperAdminId", 3));
    }

    // ---------- registration request from the public form ----------

    private readonly CenterRegistrationRequestDtoValidator _reg = new();

    private static CenterRegistrationRequestDto Reg() => new()
    {
        CenterName = "Hope Clinic",
        CenterAddress = "Main street 1",
        CenterPhone = "0599123456",
        AdminFirstName = "Rana",
        AdminLastName = "Sami",
        AdminEmail = "rana@test.com",
        AdminPhone = "0599123457",
        PasswordHash = "Str0ngPass",
        PlanId = 1
    };

    [Fact] public void A_complete_registration_is_valid() => _reg.ShouldBeValid(Reg());

    [Fact]
    public void The_center_part_is_checked()
    {
        _reg.ShouldFail(Reg().With("CenterName", ""), "CenterName", ValidationKeys.Required);
        _reg.ShouldFail(Reg().With("CenterName", Long(257)), "CenterName", ValidationKeys.TooLong);
        _reg.ShouldFail(Reg().With("CenterAddress", ""), "CenterAddress", ValidationKeys.Required);
        _reg.ShouldFail(Reg().With("CenterPhone", "x"), "CenterPhone", ValidationKeys.InvalidPhone);
        _reg.ShouldFail(Reg().With("PlanId", 0), "PlanId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void The_admin_part_is_checked()
    {
        _reg.ShouldFail(Reg().With("AdminFirstName", "A"), "AdminFirstName", ValidationKeys.TooShort);
        _reg.ShouldFail(Reg().With("AdminLastName", ""), "AdminLastName", ValidationKeys.Required);
        _reg.ShouldFail(Reg().With("AdminEmail", "nope"), "AdminEmail", ValidationKeys.InvalidEmail);
        _reg.ShouldFail(Reg().With("AdminPhone", ""), "AdminPhone", ValidationKeys.Required);
    }

    [Fact]
    public void The_password_must_be_strong()
    {
        _reg.ShouldFail(Reg().With("PasswordHash", "weak"), "PasswordHash", ValidationKeys.PasswordTooShort);
        _reg.ShouldFail(Reg().With("PasswordHash", "alllowercase1"), "PasswordHash", ValidationKeys.PasswordTooWeak);
    }

    [Fact]
    public void The_money_rules_are_the_same_as_when_a_super_admin_creates_the_center()
    {
        _reg.ShouldFail(Reg().With("RequiresPrepayment", true).With("PrepaymentAmount", 0m), "PrepaymentAmount", ValidationKeys.NegativeAmount);
        _reg.ShouldFail(Reg().With("RefundPolicyHours", 721), "RefundPolicyHours", ValidationKeys.InvalidValue);
        _reg.ShouldFail(Reg().With("PartialRefundPercent", 101m), "PartialRefundPercent", ValidationKeys.InvalidPercent);
        _reg.ShouldFail(Reg().With("LogoUrl", FormFileOf("l.exe", "application/octet-stream")), "LogoUrl", ValidationKeys.FileTypeNotAllowed);
    }

    // ---------- admin and rejection ----------

    [Fact]
    public void A_center_admin_needs_name_email_phone_address_and_city()
    {
        var v = new CreateAdminRequestDtoValidator();
        var ok = new CreateAdminRequestDto { FirstName = "Rana", LastName = "Sami", Email = "r@t.com", PhoneNumber = "0599123456", Address = "A", City = "Hebron" };

        v.ShouldBeValid(ok);
        v.ShouldFail(ok.With("FirstName", ""), "FirstName", ValidationKeys.Required);
        v.ShouldFail(ok.With("LastName", "S"), "LastName", ValidationKeys.TooShort);
        v.ShouldFail(ok.With("Email", "x"), "Email", ValidationKeys.InvalidEmail);
        v.ShouldFail(ok.With("PhoneNumber", "1"), "PhoneNumber", ValidationKeys.InvalidPhone);
        v.ShouldFail(ok.With("Address", ""), "Address", ValidationKeys.Required);
        v.ShouldFail(ok.With("City", Long(101)), "City", ValidationKeys.TooLong);
    }

    [Fact]
    public void A_rejection_needs_a_reason_up_to_500()
    {
        var v = new RejectCenterRequestDtoValidator();

        v.ShouldBeValid(new RejectCenterRequestDto { rejectionReason = Long(500) });
        v.ShouldFail(new RejectCenterRequestDto { rejectionReason = "" }, "rejectionReason", ValidationKeys.Required);
        v.ShouldFail(new RejectCenterRequestDto { rejectionReason = Long(501) }, "rejectionReason", ValidationKeys.TooLong);
    }
}

public class MiscValidatorTests
{
    // ---------- financial report ----------

    private readonly FinancialReportRequestDtoValidator _report = new();

    [Fact]
    public void The_report_year_is_from_2000_to_next_year()
    {
        var nextYear = DateTime.UtcNow.Year + 1;

        _report.ShouldBeValid(new FinancialReportRequestDto { Year = 2000 });
        _report.ShouldBeValid(new FinancialReportRequestDto { Year = nextYear });
        _report.ShouldFail(new FinancialReportRequestDto { Year = 1999 }, "Year", ValidationKeys.InvalidValue);
        _report.ShouldFail(new FinancialReportRequestDto { Year = nextYear + 1 }, "Year", ValidationKeys.InvalidValue);
        _report.ShouldFail(new FinancialReportRequestDto { Year = 0 }, "Year", ValidationKeys.InvalidValue);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(12, true)]
    [InlineData(0, false)]
    [InlineData(13, false)]
    public void The_report_month_is_optional_but_1_to_12(int month, bool ok)
    {
        var dto = new FinancialReportRequestDto { Year = 2025, Month = month };
        if (ok) _report.ShouldBeValid(dto);
        else _report.ShouldFail(dto, "Month", ValidationKeys.InvalidValue);
    }

    [Fact] public void A_whole_year_report_has_no_month() => _report.ShouldBeValid(new FinancialReportRequestDto { Year = 2025, Month = null });

    // ---------- doctor search ----------

    private readonly SearchDoctorRequestValidator _search = new();

    [Fact] public void An_empty_search_is_valid() => _search.ShouldBeValid(new SearchDoctorRequest());

    [Fact]
    public void The_search_filters_have_limits()
    {
        _search.ShouldFail(new SearchDoctorRequest { PageNumber = 0 }, "PageNumber", ValidationKeys.InvalidValue);
        _search.ShouldFail(new SearchDoctorRequest { PageNumber = 10_001 }, "PageNumber", ValidationKeys.InvalidValue);
        _search.ShouldFail(new SearchDoctorRequest { doctorName = Long(101) }, "doctorName", ValidationKeys.TooLong);
        _search.ShouldFail(new SearchDoctorRequest { Specialization = Long(257) }, "Specialization", ValidationKeys.TooLong);
        _search.ShouldFail(new SearchDoctorRequest { departmentId = 0 }, "departmentId", ValidationKeys.InvalidId);
        _search.ShouldBeValid(new SearchDoctorRequest { doctorName = Long(100), Specialization = Long(256), departmentId = 4 });
    }

    // ---------- patients of a center ----------

    [Fact]
    public void A_patient_listing_needs_an_id_and_a_real_status()
    {
        var v = new GetPatientRequestDtoValidator();

        v.ShouldBeValid(new GetPatientRequestDto { patientId = 1, status = AppointmentStatus.Pending });
        v.ShouldFail(new GetPatientRequestDto { patientId = 0, status = AppointmentStatus.Pending }, "patientId", ValidationKeys.InvalidId);
        v.ShouldFail(new GetPatientRequestDto { patientId = 1, status = 0 }, "status", ValidationKeys.InvalidEnum);
        v.ShouldFail(new GetPatientRequestDto { patientId = 1, status = (AppointmentStatus)99 }, "status", ValidationKeys.InvalidEnum);
    }

    // ---------- bulk email ----------

    private readonly SendEmailDtoValidator _email = new();
    private static SendEmailDto Mail(string target = "active") => new() { Target = target, Subject = "Hello", Message = "Body" };

    [Theory]
    [InlineData("active")]
    [InlineData("expired")]
    [InlineData("ACTIVE")]
    [InlineData("  Expired ")]
    public void The_group_targets_need_no_center(string target) => _email.ShouldBeValid(Mail(target));

    [Fact]
    public void A_specific_target_needs_a_center()
    {
        _email.ShouldBeValid(Mail("specific").With("CenterId", 5));
        _email.ShouldFail(Mail("specific"), "CenterId", ValidationKeys.Required);
        _email.ShouldFail(Mail("Specific").With("CenterId", 0), "CenterId", ValidationKeys.InvalidId);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("everyone")]
    public void An_unknown_target_is_refused(string target) =>
        _email.ShouldFail(Mail(target), "Target", ValidationKeys.InvalidValue);

    [Fact]
    public void A_target_is_required() => _email.ShouldFail(Mail(""), "Target", ValidationKeys.Required);

    [Fact]
    public void Subject_and_message_are_required_with_limits()
    {
        _email.ShouldFail(Mail().With("Subject", ""), "Subject", ValidationKeys.Required);
        _email.ShouldFail(Mail().With("Subject", Long(201)), "Subject", ValidationKeys.TooLong);
        _email.ShouldBeValid(Mail().With("Subject", Long(200)));
        _email.ShouldFail(Mail().With("Message", ""), "Message", ValidationKeys.Required);
        _email.ShouldFail(Mail().With("Message", Long(10_001)), "Message", ValidationKeys.TooLong);
        _email.ShouldBeValid(Mail().With("Message", Long(10_000)));
    }
}
