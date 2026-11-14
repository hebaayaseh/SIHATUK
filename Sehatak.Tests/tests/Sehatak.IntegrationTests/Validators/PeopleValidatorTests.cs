using Sehatak.Application.Common;
using Sehatak.Application.DTOs.EditProfile.EditProfileActors;
using Sehatak.Application.DTOs.EditProfile.EditSuperAdmin;
using Sehatak.Application.DTOs.FeedbackDto;
using Sehatak.Application.DTOs.SubPatientDto;
using Sehatak.Application.Validators.Common;
using Sehatak.Application.Validators.EditProfile;
using Sehatak.Application.Validators.Feedback;
using Sehatak.Application.Validators.SubPatient;
using Sehatak.Domain.Enums;
using Sehatak.Domain.Enums.SharedEnums;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class SubPatientValidatorTests
{
    private static DateOnly Today => ClinicClock.Today;

    private static SummarySubPatientRequestDto Child() => new()
    {
        SubPatientFirstName = "Lina",
        SubPatientLastName = "Sami",
        BloodType = BloodType.A_Positive,
        Gender = Gender.Female,
        WhatAppNumber = "0599123456",
        DateOfBith = Today.AddYears(-8)
    };

    private readonly SummarySubPatientRequestDtoValidator _one = new();

    [Fact] public void A_complete_sub_patient_is_valid() => _one.ShouldBeValid(Child());

    [Fact]
    public void Names_are_required_two_to_a_hundred()
    {
        foreach (var p in new[] { "SubPatientFirstName", "SubPatientLastName" })
        {
            _one.ShouldFail(Child().With(p, ""), p, ValidationKeys.Required);
            _one.ShouldFail(Child().With(p, "L"), p, ValidationKeys.TooShort);
            _one.ShouldFail(Child().With(p, Long(101)), p, ValidationKeys.TooLong);
        }
    }

    [Fact]
    public void Blood_type_and_gender_must_be_real_choices()
    {
        _one.ShouldFail(Child().With("BloodType", (BloodType)0), "BloodType", ValidationKeys.InvalidEnum);
        _one.ShouldFail(Child().With("BloodType", (BloodType)9), "BloodType", ValidationKeys.InvalidEnum);
        _one.ShouldFail(Child().With("Gender", (Gender)0), "Gender", ValidationKeys.InvalidEnum);
    }

    [Fact]
    public void The_phone_is_required_and_valid()
    {
        _one.ShouldFail(Child().With("WhatAppNumber", ""), "WhatAppNumber", ValidationKeys.Required);
        _one.ShouldFail(Child().With("WhatAppNumber", "abc"), "WhatAppNumber", ValidationKeys.InvalidPhone);
    }

    [Fact]
    public void The_date_of_birth_is_in_the_past_and_not_ancient()
    {
        _one.ShouldFail(Child().With("DateOfBith", default(DateOnly)), "DateOfBith", ValidationKeys.Required);
        _one.ShouldFail(Child().With("DateOfBith", Today), "DateOfBith", ValidationKeys.DateInFuture);
        _one.ShouldFail(Child().With("DateOfBith", Today.AddYears(-121)), "DateOfBith", ValidationKeys.InvalidDateOfBirth);
    }

    [Fact]
    public void A_batch_has_between_1_and_20_children_each_checked()
    {
        var v = new AddSubPatientRequestDtoValidator();
        AddSubPatientRequestDto Batch(int n) => new() { SubPatients = Enumerable.Range(0, n).Select(_ => Child()).ToList() };

        v.ShouldBeValid(Batch(1));
        v.ShouldBeValid(Batch(20));
        v.ShouldFail(Batch(21), "SubPatients", ValidationKeys.ListTooLarge);
        v.ShouldFail(Batch(0), "SubPatients", ValidationKeys.ListEmpty);
        v.ShouldFail(new AddSubPatientRequestDto { SubPatients = null! }, "SubPatients", ValidationKeys.ListEmpty);
        v.ShouldFail(new AddSubPatientRequestDto { SubPatients = new() { Child(), Child().With("SubPatientFirstName", "") } },
            "SubPatients[1].SubPatientFirstName", ValidationKeys.Required);
    }

    // ---------- update ----------

    private readonly UpdateSubPatientRequestDtoValidator _update = new();

    [Fact]
    public void An_update_changing_one_field_is_valid()
    {
        _update.ShouldBeValid(new UpdateSubPatientRequestDto { SubPatientFirstName = "Lina" });
        _update.ShouldBeValid(new UpdateSubPatientRequestDto { BloodType = BloodType.O_Negative });
        _update.ShouldBeValid(new UpdateSubPatientRequestDto { Gender = Gender.Male });
        _update.ShouldBeValid(new UpdateSubPatientRequestDto { WhatAppNumber = "0599123456" });
        _update.ShouldBeValid(new UpdateSubPatientRequestDto { DateOfBith = Today.AddYears(-3) });
    }

    [Fact]
    public void An_update_changing_nothing_is_refused() =>
        _update.ShouldFail(new UpdateSubPatientRequestDto(), "request", ValidationKeys.AtLeastOneField);

    [Fact]
    public void An_update_checks_what_is_sent()
    {
        _update.ShouldFail(new UpdateSubPatientRequestDto { SubPatientFirstName = "L" }, "SubPatientFirstName", ValidationKeys.TooShort);
        _update.ShouldFail(new UpdateSubPatientRequestDto { SubPatientLastName = Long(101) }, "SubPatientLastName", ValidationKeys.TooLong);
        _update.ShouldFail(new UpdateSubPatientRequestDto { BloodType = (BloodType)0 }, "BloodType", ValidationKeys.InvalidEnum);
        _update.ShouldFail(new UpdateSubPatientRequestDto { Gender = (Gender)5 }, "Gender", ValidationKeys.InvalidEnum);
        _update.ShouldFail(new UpdateSubPatientRequestDto { WhatAppNumber = "x" }, "WhatAppNumber", ValidationKeys.InvalidPhone);
        _update.ShouldFail(new UpdateSubPatientRequestDto { DateOfBith = Today }, "DateOfBith", ValidationKeys.DateInFuture);
    }
}

public class ProfileValidatorTests
{
    // ---------- patient ----------

    private readonly EditPatientInformationRequestValidator _patient = new();

    [Fact]
    public void Any_single_change_is_a_valid_patient_edit()
    {
        _patient.ShouldBeValid(new EditPatientInformationRequest { firstNmae = "Rana" });
        _patient.ShouldBeValid(new EditPatientInformationRequest { address = "A" });
        _patient.ShouldBeValid(new EditPatientInformationRequest { city = "Hebron" });
        _patient.ShouldBeValid(new EditPatientInformationRequest { phoneNumber = "0599123456" });
        _patient.ShouldBeValid(new EditPatientInformationRequest { profileImage = FormFileOf("p.png", "image/png") });
        _patient.ShouldBeValid(new EditPatientInformationRequest { RemoveProfileImage = true });
    }

    [Fact]
    public void An_empty_patient_edit_is_refused() =>
        _patient.ShouldFail(new EditPatientInformationRequest(), "request", ValidationKeys.AtLeastOneField);

    [Fact]
    public void Removing_and_replacing_the_image_in_one_request_is_refused() =>
        _patient.ShouldFail(new EditPatientInformationRequest { RemoveProfileImage = true, profileImage = FormFileOf("p.png", "image/png") },
            "RemoveProfileImage", ValidationKeys.InvalidValue);

    [Fact]
    public void A_patient_edit_checks_what_is_sent()
    {
        _patient.ShouldFail(new EditPatientInformationRequest { firstNmae = "A" }, "firstNmae", ValidationKeys.TooShort);
        _patient.ShouldFail(new EditPatientInformationRequest { lastNmae = Long(101) }, "lastNmae", ValidationKeys.TooLong);
        _patient.ShouldFail(new EditPatientInformationRequest { address = Long(501) }, "address", ValidationKeys.TooLong);
        _patient.ShouldFail(new EditPatientInformationRequest { city = Long(101) }, "city", ValidationKeys.TooLong);
        _patient.ShouldFail(new EditPatientInformationRequest { phoneNumber = "abc" }, "phoneNumber", ValidationKeys.InvalidPhone);
        _patient.ShouldFail(new EditPatientInformationRequest { profileImage = FormFileOf("p.gif", "image/gif") }, "profileImage", ValidationKeys.FileTypeNotAllowed);
    }

    // ---------- staff ----------

    private readonly EditSttafInformationRequestValidator _staff = new();

    [Fact]
    public void The_staff_only_fields_count_as_changes()
    {
        _staff.ShouldBeValid(new EditSttafInformationRequest { Specialization = "Cardiology" });
        _staff.ShouldBeValid(new EditSttafInformationRequest { Bio = "Hello" });
        _staff.ShouldBeValid(new EditSttafInformationRequest { OnlineEnabled = false });
        _staff.ShouldBeValid(new EditSttafInformationRequest { RemoveProfileImage = true });
        _staff.ShouldFail(new EditSttafInformationRequest(), "request", ValidationKeys.AtLeastOneField);
    }

    [Fact]
    public void Staff_text_fields_have_limits()
    {
        _staff.ShouldBeValid(new EditSttafInformationRequest { Specialization = Long(256), Bio = Long(1000) });
        _staff.ShouldFail(new EditSttafInformationRequest { Specialization = Long(257) }, "Specialization", ValidationKeys.TooLong);
        _staff.ShouldFail(new EditSttafInformationRequest { Bio = Long(1001) }, "Bio", ValidationKeys.TooLong);
        _staff.ShouldFail(new EditSttafInformationRequest { RemoveProfileImage = true, profileImage = FormFileOf("p.png", "image/png") },
            "RemoveProfileImage", ValidationKeys.InvalidValue);
    }

    // ---------- center ----------

    private readonly EditCenterInformationRequestValidator _center = new();

    [Fact] public void The_defaults_of_a_center_edit_are_valid() => _center.ShouldBeValid(new EditCenterInformationRequest());

    [Fact]
    public void Prepayment_needs_an_amount_only_when_switched_on()
    {
        _center.ShouldBeValid(new EditCenterInformationRequest { RequiresPrepayment = false, PrepaymentAmount = 0m });
        _center.ShouldBeValid(new EditCenterInformationRequest { RequiresPrepayment = true, PrepaymentAmount = 25m });
        _center.ShouldFail(new EditCenterInformationRequest { RequiresPrepayment = true, PrepaymentAmount = 0m }, "PrepaymentAmount", ValidationKeys.NegativeAmount);
        _center.ShouldFail(new EditCenterInformationRequest { RequiresPrepayment = true, PrepaymentAmount = null }, "PrepaymentAmount", ValidationKeys.Required);
    }

    [Fact]
    public void A_center_edit_checks_what_is_sent()
    {
        _center.ShouldFail(new EditCenterInformationRequest { PrepaymentAmount = -1m }, "PrepaymentAmount", ValidationKeys.NegativeAmount);
        _center.ShouldFail(new EditCenterInformationRequest { PartialRefundPercent = 101m }, "PartialRefundPercent", ValidationKeys.InvalidPercent);
        _center.ShouldFail(new EditCenterInformationRequest { RefundPolicyHours = -1 }, "RefundPolicyHours", ValidationKeys.InvalidValue);
        _center.ShouldFail(new EditCenterInformationRequest { RefundPolicyHours = 721 }, "RefundPolicyHours", ValidationKeys.InvalidValue);
        _center.ShouldFail(new EditCenterInformationRequest { AdminWhatsappNumber = "x" }, "AdminWhatsappNumber", ValidationKeys.InvalidPhone);
        _center.ShouldFail(new EditCenterInformationRequest { Phone = "x" }, "Phone", ValidationKeys.InvalidPhone);
        _center.ShouldFail(new EditCenterInformationRequest { Address = Long(501) }, "Address", ValidationKeys.TooLong);
        _center.ShouldFail(new EditCenterInformationRequest { LogoUrl = FormFileOf("l.bmp", "image/bmp") }, "LogoUrl", ValidationKeys.FileTypeNotAllowed);
    }

    // ---------- super admin ----------

    [Fact]
    public void A_super_admin_name_is_two_to_a_hundred()
    {
        var v = new EditNameRequestValidator();

        v.ShouldBeValid(new EditNameRequest { Name = "Rana" });
        v.ShouldFail(new EditNameRequest { Name = "" }, "Name", ValidationKeys.Required);
        v.ShouldFail(new EditNameRequest { Name = "R" }, "Name", ValidationKeys.TooShort);
        v.ShouldFail(new EditNameRequest { Name = Long(101) }, "Name", ValidationKeys.TooLong);
    }

    [Fact]
    public void A_new_profile_image_is_required_and_must_be_an_image()
    {
        var v = new EditProfileImageRequestValidator();

        v.ShouldBeValid(new EditProfileImageRequest { ImageFile = FormFileOf("p.jpg", "image/jpeg") });
        v.ShouldFail(new EditProfileImageRequest { ImageFile = null! }, "ImageFile", ValidationKeys.FileRequired);
        v.ShouldFail(new EditProfileImageRequest { ImageFile = FormFileOf("p.exe", "application/octet-stream") }, "ImageFile", ValidationKeys.FileTypeNotAllowed);
    }
}

public class FeedbackValidatorTests
{
    [Fact]
    public void A_feedback_from_a_user_needs_a_type_a_subject_and_a_message()
    {
        var v = new SubmitFeedbackRequestValidator();
        var ok = new SubmitFeedbackRequestDto { Type = FeedbackType.Bug, Subject = "Crash", Message = "It crashed" };

        v.ShouldBeValid(ok);
        v.ShouldFail(ok.With("Type", (FeedbackType)0), "Type", ValidationKeys.InvalidEnum);
        v.ShouldFail(ok.With("Type", (FeedbackType)5), "Type", ValidationKeys.InvalidEnum);
        v.ShouldFail(ok.With("Subject", ""), "Subject", ValidationKeys.Required);
        v.ShouldFail(ok.With("Subject", Long(201)), "Subject", ValidationKeys.TooLong);
        v.ShouldFail(ok.With("Message", ""), "Message", ValidationKeys.Required);
        v.ShouldFail(ok.With("Message", Long(2001)), "Message", ValidationKeys.TooLong);
        v.ShouldBeValid(new SubmitFeedbackRequestDto { Type = FeedbackType.Bug, Subject = Long(200), Message = Long(2000) });
    }

    private readonly SubmitGuestFeedbackRequestValidator _guest = new();
    private static SubmitGuestFeedbackRequestDto Guest() => new()
    {
        Name = "Visitor", Email = "v@test.com", Type = FeedbackType.Help, Subject = "Question", Message = "How do I register?"
    };

    [Fact]
    public void A_guest_feedback_needs_a_name_and_an_email()
    {
        _guest.ShouldBeValid(Guest());
        _guest.ShouldFail(Guest().With("Name", ""), "Name", ValidationKeys.Required);
        _guest.ShouldFail(Guest().With("Name", "V"), "Name", ValidationKeys.TooShort);
        _guest.ShouldFail(Guest().With("Email", "nope"), "Email", ValidationKeys.InvalidEmail);
        _guest.ShouldFail(Guest().With("Email", ""), "Email", ValidationKeys.Required);
    }

    [Fact]
    public void A_guest_phone_and_center_are_optional_but_checked_when_sent()
    {
        _guest.ShouldBeValid(Guest().With("Phone", "0599123456").With("CenterId", 4));
        _guest.ShouldFail(Guest().With("Phone", "abc"), "Phone", ValidationKeys.InvalidPhone);
        _guest.ShouldFail(Guest().With("CenterId", 0), "CenterId", ValidationKeys.InvalidId);
    }

    [Fact]
    public void A_guest_feedback_has_the_same_text_limits()
    {
        _guest.ShouldFail(Guest().With("Type", (FeedbackType)0), "Type", ValidationKeys.InvalidEnum);
        _guest.ShouldFail(Guest().With("Subject", Long(201)), "Subject", ValidationKeys.TooLong);
        _guest.ShouldFail(Guest().With("Message", Long(2001)), "Message", ValidationKeys.TooLong);
    }

    [Fact]
    public void A_reply_needs_text_and_a_real_status()
    {
        var v = new ReplyFeedbackRequestValidator();

        v.ShouldBeValid(new ReplyFeedbackRequestDto { Reply = "Thanks", Status = FeedbackStatus.Resolved });
        v.ShouldBeValid(new ReplyFeedbackRequestDto { Reply = "Thanks" });                       // Status defaults to Resolved
        v.ShouldFail(new ReplyFeedbackRequestDto { Reply = "" }, "Reply", ValidationKeys.Required);
        v.ShouldFail(new ReplyFeedbackRequestDto { Reply = Long(2001) }, "Reply", ValidationKeys.TooLong);
        v.ShouldFail(new ReplyFeedbackRequestDto { Reply = "x", Status = (FeedbackStatus)0 }, "Status", ValidationKeys.InvalidEnum);
        v.ShouldFail(new ReplyFeedbackRequestDto { Reply = "x", Status = (FeedbackStatus)4 }, "Status", ValidationKeys.InvalidEnum);
    }

    [Fact]
    public void A_status_change_needs_a_real_status()
    {
        var v = new UpdateFeedbackStatusRequestValidator();

        v.ShouldBeValid(new UpdateFeedbackStatusRequestDto { Status = FeedbackStatus.InProgress });
        v.ShouldFail(new UpdateFeedbackStatusRequestDto { Status = 0 }, "Status", ValidationKeys.InvalidEnum);
    }
}
