using Microsoft.AspNetCore.Http;
using Sehatak.Application.Common;
using Sehatak.Application.DTOs.Auth;
using Sehatak.Application.DTOs.PatienRegisterDto;
using Sehatak.Application.DTOs.PatientLoginDto;
using Sehatak.Application.DTOs.StaffLogIn;
using Sehatak.Application.Validators.Auth;
using Sehatak.Application.Validators.Common;
using Sehatak.Domain.Enums;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestDtoValidator _v = new();

    private static RegisterRequestDto Good() => new()
    {
        firstName = "Rana",
        lastName = "Sami",
        email = "rana@test.com",
        phoneNumber = "0599123456",
        password = "Str0ng!Pass",
        address = "Main street 1",
        city = "Hebron",
        gender = Gender.Female,
        bloodType = BloodType.A_Positive,
        DateOfBith = ClinicClock.Today.AddYears(-30)
    };

    [Fact] public void A_complete_registration_is_valid() => _v.ShouldBeValid(Good());
    [Fact] public void The_profile_image_is_optional() => _v.ShouldBeValid(Good().With("ProfileImage", null));

    [Theory]
    [InlineData("firstName")]
    [InlineData("lastName")]
    public void Names_are_required_two_to_a_hundred(string prop)
    {
        _v.ShouldFail(Good().With(prop, ""), prop, ValidationKeys.Required);
        _v.ShouldFail(Good().With(prop, "A"), prop, ValidationKeys.TooShort);
        _v.ShouldFail(Good().With(prop, Long(101)), prop, ValidationKeys.TooLong);
        _v.ShouldBeValid(Good().With(prop, "Al"));
        _v.ShouldBeValid(Good().With(prop, Long(100)));
    }

    [Fact]
    public void The_email_is_checked()
    {
        _v.ShouldFail(Good().With("email", ""), "email", ValidationKeys.Required);
        _v.ShouldFail(Good().With("email", "nope"), "email", ValidationKeys.InvalidEmail);
    }

    [Fact]
    public void The_phone_is_checked()
    {
        _v.ShouldFail(Good().With("phoneNumber", ""), "phoneNumber", ValidationKeys.Required);
        _v.ShouldFail(Good().With("phoneNumber", "12"), "phoneNumber", ValidationKeys.InvalidPhone);
    }

    [Fact]
    public void The_password_must_be_strong()
    {
        _v.ShouldFail(Good().With("password", "short1A"), "password", ValidationKeys.PasswordTooShort);
        _v.ShouldFail(Good().With("password", "alllowercase1"), "password", ValidationKeys.PasswordTooWeak);
        _v.ShouldFail(Good().With("password", ""), "password", ValidationKeys.Required);
    }

    [Theory]
    [InlineData("address", 500)]
    [InlineData("city", 100)]
    public void Address_and_city_are_required_with_a_limit(string prop, int max)
    {
        _v.ShouldFail(Good().With(prop, ""), prop, ValidationKeys.Required);
        _v.ShouldFail(Good().With(prop, Long(max + 1)), prop, ValidationKeys.TooLong);
        _v.ShouldBeValid(Good().With(prop, Long(max)));
    }

    [Fact]
    public void Gender_and_blood_type_must_be_real_choices()
    {
        _v.ShouldFail(Good().With("gender", (Gender)0), "gender", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Good().With("gender", (Gender)9), "gender", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Good().With("bloodType", (BloodType)0), "bloodType", ValidationKeys.InvalidEnum);
        _v.ShouldFail(Good().With("bloodType", (BloodType)9), "bloodType", ValidationKeys.InvalidEnum);
    }

    [Fact]
    public void The_date_of_birth_is_in_the_past_and_not_ancient()
    {
        _v.ShouldFail(Good().With("DateOfBith", default(DateOnly)), "DateOfBith", ValidationKeys.Required);
        _v.ShouldFail(Good().With("DateOfBith", ClinicClock.Today), "DateOfBith", ValidationKeys.DateInFuture);
        _v.ShouldFail(Good().With("DateOfBith", ClinicClock.Today.AddYears(-150)), "DateOfBith", ValidationKeys.InvalidDateOfBirth);
    }

    [Fact]
    public void A_profile_image_must_be_an_image()
    {
        _v.ShouldBeValid(Good().With("ProfileImage", FormFileOf("me.png", "image/png")));
        _v.ShouldFail(Good().With("ProfileImage", FormFileOf("me.exe", "application/octet-stream")), "ProfileImage", ValidationKeys.FileTypeNotAllowed);
        _v.ShouldFail(Good().With("ProfileImage", FormFileOf("me.png", "image/png", ValidationRules.MaxImageBytes + 1)), "ProfileImage", ValidationKeys.FileTooLarge);
    }

    [Fact]
    public void An_empty_request_reports_every_required_field_at_once()
    {
        var r = _v.Validate(new RegisterRequestDto());

        foreach (var p in new[] { "firstName", "lastName", "email", "phoneNumber", "password", "address", "city", "gender", "bloodType", "DateOfBith" })
            r.Errors.Select(e => e.PropertyName).ShouldContain(p);
    }
}

public class VerifyOtpValidatorTests
{
    private readonly VerifyOtpRequestDtoValidator _v = new();

    [Fact] public void A_user_and_a_six_digit_code_are_valid() => _v.ShouldBeValid(new VerifyOtpRequestDto { UserId = 1, code = "123456" });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_user_id_must_be_positive(int id) =>
        _v.ShouldFail(new VerifyOtpRequestDto { UserId = id, code = "123456" }, "UserId", ValidationKeys.InvalidId);

    [Theory]
    [InlineData("", ValidationKeys.Required)]
    [InlineData("12345", ValidationKeys.InvalidOtp)]
    [InlineData("1234567", ValidationKeys.InvalidOtp)]
    [InlineData("abcdef", ValidationKeys.InvalidOtp)]
    public void The_code_must_be_exactly_six_digits(string code, string key) =>
        _v.ShouldFail(new VerifyOtpRequestDto { UserId = 1, code = code }, "code", key);
}

public class LoginValidatorTests
{
    [Fact]
    public void Patient_login_needs_an_email_and_any_password_up_to_128()
    {
        var v = new PatientRequestDtoValidator();

        v.ShouldBeValid(new PatientRequestDto { email = "a@b.co", password = "x" });      // login must not enforce strength: old accounts exist
        v.ShouldFail(new PatientRequestDto { email = "", password = "x" }, "email", ValidationKeys.Required);
        v.ShouldFail(new PatientRequestDto { email = "bad", password = "x" }, "email", ValidationKeys.InvalidEmail);
        v.ShouldFail(new PatientRequestDto { email = "a@b.co", password = "" }, "password", ValidationKeys.Required);
        v.ShouldFail(new PatientRequestDto { email = "a@b.co", password = Long(129) }, "password", ValidationKeys.TooLong);
        v.ShouldBeValid(new PatientRequestDto { email = "a@b.co", password = Long(128) });
    }

    [Fact]
    public void Staff_login_follows_the_same_rules()
    {
        var v = new StaffLoginRequestDtoValidator();

        v.ShouldBeValid(new StaffLoginRequestDto { Email = "a@b.co", Password = "x" });
        v.ShouldFail(new StaffLoginRequestDto { Email = "", Password = "x" }, "Email", ValidationKeys.Required);
        v.ShouldFail(new StaffLoginRequestDto { Email = "bad", Password = "x" }, "Email", ValidationKeys.InvalidEmail);
        v.ShouldFail(new StaffLoginRequestDto { Email = "a@b.co", Password = "" }, "Password", ValidationKeys.Required);
        v.ShouldFail(new StaffLoginRequestDto { Email = "a@b.co", Password = Long(129) }, "Password", ValidationKeys.TooLong);
    }
}

public class RefreshTokenValidatorTests
{
    [Fact]
    public void Refresh_token_is_required_and_limited_to_512()
    {
        var v = new RefreshTokenRequestDtoValidator();

        v.ShouldBeValid(new RefreshTokenRequestDto { RefreshToken = Long(512) });
        v.ShouldFail(new RefreshTokenRequestDto { RefreshToken = "" }, "RefreshToken", ValidationKeys.Required);
        v.ShouldFail(new RefreshTokenRequestDto { RefreshToken = Long(513) }, "RefreshToken", ValidationKeys.TooLong);
    }

    [Fact]
    public void Logout_has_the_same_limits()
    {
        var v = new LogoutRequestDtoValidator();

        v.ShouldBeValid(new LogoutRequestDto { RefreshToken = Long(512) });
        v.ShouldFail(new LogoutRequestDto { RefreshToken = "" }, "RefreshToken", ValidationKeys.Required);
        v.ShouldFail(new LogoutRequestDto { RefreshToken = Long(513) }, "RefreshToken", ValidationKeys.TooLong);
    }
}
