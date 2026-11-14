using FluentValidation;
using Microsoft.AspNetCore.Http;
using Sehatak.Application.Common;
using Sehatak.Application.Validators.Common;
using Shouldly;
using static Sehatak.IntegrationTests.Validators.ValidatorTestHelpers;

namespace Sehatak.IntegrationTests.Validators;

/// <summary>
/// ValidationRules: the shared rule builders every validator is made of. A mistake here is a mistake everywhere,
/// so each rule is tested on its boundaries once, through a tiny validator.
/// </summary>
public class ValidationRulesTests
{
    private sealed record Str(string? Value);
    private sealed record Num(decimal Value);
    private sealed record Id(int Value);
    private sealed record Day(DateOnly Value);
    private sealed record Upload(IFormFile? Value);
    private enum Color { Red = 1, Blue = 2 }
    private sealed record Pick(Color Value);

    private static InlineValidator<T> V<T>(Action<InlineValidator<T>> rules)
    {
        var v = new InlineValidator<T>();
        rules(v);
        return v;
    }

    // ---------- phone ----------

    [Theory]
    [InlineData("0599123456")]
    [InlineData("+970599123456")]
    [InlineData("00970599123456")]
    [InlineData("+14155552671")]
    [InlineData("059-912-3456")]
    [InlineData("(059) 9123456")]
    [InlineData("059 912 3456")]
    [InlineData("1234567")]                 // 7 digits: the shortest accepted
    [InlineData("123456789012345")]         // 15 digits: the longest accepted
    public void These_phone_numbers_are_accepted(string phone) => ValidationRules.BeAPhoneNumber(phone).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("123456")]                  // 6 digits
    [InlineData("1234567890123456")]        // 16 digits
    [InlineData("+")]
    [InlineData("0599abc456")]
    [InlineData("++970599123456")]
    [InlineData("05991234 56 ext")]
    public void These_phone_numbers_are_refused(string? phone) => ValidationRules.BeAPhoneNumber(phone).ShouldBeFalse();

    /// <summary>`$` in a .NET regex also matches before a final "\n", so a phone ending in a line break is accepted and then stored with it.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData("0599123456\n")]
    public void A_phone_number_with_a_line_break_at_the_end_is_refused(string phone) => ValidationRules.BeAPhoneNumber(phone).ShouldBeFalse();

    /// <summary>`\d` matches every Unicode digit, so Arabic-Indic digits pass.</summary>
    [Fact]
    [Trait("Category", "KnownBug")]
    public void A_phone_number_with_non_ascii_digits_is_refused() => ValidationRules.BeAPhoneNumber("0٥٩٩١٢٣٤٥٦").ShouldBeFalse();

    [Fact]
    public void The_phone_rule_reports_required_too_long_and_invalid()
    {
        var v = V<Str>(x => x.RuleFor(s => s.Value!).ValidPhone());

        v.ShouldFail(new Str(""), "Value", ValidationKeys.Required);
        v.ShouldFail(new Str(new string('1', 21)), "Value", ValidationKeys.TooLong);
        v.ShouldFail(new Str("not-a-phone"), "Value", ValidationKeys.InvalidPhone);
        v.ShouldBeValid(new Str("0599123456"));
    }

    [Fact]
    public void The_optional_phone_accepts_nothing_but_checks_what_is_sent()
    {
        var v = V<Str>(x => x.RuleFor(s => s.Value).OptionalPhone());

        v.ShouldBeValid(new Str(null));
        v.ShouldBeValid(new Str("0599123456"));
        v.ShouldFail(new Str("abc"), "Value", ValidationKeys.InvalidPhone);
        v.ShouldFail(new Str(new string('1', 21)), "Value", ValidationKeys.TooLong);
    }

    // ---------- email ----------

    [Theory]
    [InlineData("a@b.co")]
    [InlineData("first.last+tag@sub.example.com")]
    public void A_normal_email_is_valid(string email) =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidEmail()).ShouldBeValid(new Str(email));

    [Theory]
    [InlineData("plain")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    public void A_broken_email_is_refused(string email) =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidEmail()).ShouldFail(new Str(email), "Value", ValidationKeys.InvalidEmail);

    [Fact]
    public void An_email_is_required_and_has_a_length_limit()
    {
        var v = V<Str>(x => x.RuleFor(s => s.Value!).ValidEmail());

        v.ShouldFail(new Str(""), "Value", ValidationKeys.Required);
        v.ShouldFail(new Str(null), "Value", ValidationKeys.Required);
        v.ShouldFail(new Str(new string('a', 142) + "@test.com"), "Value", ValidationKeys.TooLong);   // 151 > 150
        v.ShouldBeValid(new Str(new string('a', 100) + "@test.com"));
    }

    [Fact]
    public void The_optional_email_accepts_nothing()
    {
        var v = V<Str>(x => x.RuleFor(s => s.Value).OptionalEmail());

        v.ShouldBeValid(new Str(null));
        v.ShouldFail(new Str("nope"), "Value", ValidationKeys.InvalidEmail);
    }

    // ---------- password ----------

    [Theory]
    [InlineData("Str0ngPass")]
    [InlineData("Aa1aaaaa")]                // exactly 8
    [InlineData("Str0ng Pass")]             // spaces are fine
    public void A_strong_password_is_valid(string pwd) =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidPassword()).ShouldBeValid(new Str(pwd));

    [Theory]
    [InlineData("alllowercase1")]
    [InlineData("ALLUPPERCASE1")]
    [InlineData("NoDigitsHere")]
    [InlineData("12345678")]
    public void A_weak_password_is_refused(string pwd) =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidPassword()).ShouldFail(new Str(pwd), "Value", ValidationKeys.PasswordTooWeak);

    [Fact]
    public void A_password_has_a_length_range_and_is_required()
    {
        var v = V<Str>(x => x.RuleFor(s => s.Value!).ValidPassword());

        v.ShouldFail(new Str("Aa1aaaa"), "Value", ValidationKeys.PasswordTooShort);                 // 7
        v.ShouldFail(new Str(""), "Value", ValidationKeys.Required);
        v.ShouldFail(new Str("Aa1" + new string('a', 126)), "Value", ValidationKeys.TooLong);        // 129
        v.ShouldBeValid(new Str("Aa1" + new string('a', 125)));                                       // 128
    }

    // ---------- OTP ----------

    [Theory]
    [InlineData("123456")]
    [InlineData("000000")]
    public void A_six_digit_code_is_valid(string code) =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidOtpCode()).ShouldBeValid(new Str(code));

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("12 456")]
    [InlineData("-12345")]
    public void Anything_else_is_not_a_code(string code) =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidOtpCode()).ShouldFail(new Str(code), "Value", ValidationKeys.InvalidOtp);

    [Fact]
    public void A_code_is_required() =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidOtpCode()).ShouldFail(new Str(""), "Value", ValidationKeys.Required);

    /// <summary>Same two regex traps as the phone: a trailing "\n" and non-ASCII digits both match ^\d{6}$.</summary>
    [Theory]
    [Trait("Category", "KnownBug")]
    [InlineData("123456\n")]
    [InlineData("١٢٣٤٥٦")]
    public void A_code_with_a_line_break_or_non_ascii_digits_is_refused(string code) =>
        V<Str>(x => x.RuleFor(s => s.Value!).ValidOtpCode()).ShouldFail(new Str(code), "Value", ValidationKeys.InvalidOtp);

    // ---------- names ----------

    [Fact]
    public void A_person_name_is_two_to_a_hundred_characters()
    {
        var v = V<Str>(x => x.RuleFor(s => s.Value!).PersonName());

        v.ShouldFail(new Str(""), "Value", ValidationKeys.Required);
        v.ShouldFail(new Str("   "), "Value", ValidationKeys.Required);
        v.ShouldFail(new Str("A"), "Value", ValidationKeys.TooShort);
        v.ShouldFail(new Str(Long(101)), "Value", ValidationKeys.TooLong);
        v.ShouldBeValid(new Str("Al"));
        v.ShouldBeValid(new Str(Long(100)));
        v.ShouldBeValid(new Str("رنا"));
    }

    [Fact]
    public void The_optional_name_accepts_nothing_but_not_one_letter()
    {
        var v = V<Str>(x => x.RuleFor(s => s.Value).OptionalPersonName());

        v.ShouldBeValid(new Str(null));
        v.ShouldFail(new Str("A"), "Value", ValidationKeys.TooShort);
        v.ShouldFail(new Str(Long(101)), "Value", ValidationKeys.TooLong);
    }

    // ---------- ids and enums ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_id_must_be_positive(int id) =>
        V<Id>(x => x.RuleFor(s => s.Value).ValidId()).ShouldFail(new Id(id), "Value", ValidationKeys.InvalidId);

    [Fact]
    public void A_positive_id_is_valid() => V<Id>(x => x.RuleFor(s => s.Value).ValidId()).ShouldBeValid(new Id(1));

    [Theory]
    [InlineData(0)]     // the CLR default: an omitted enum
    [InlineData(3)]
    [InlineData(-1)]
    public void An_enum_must_be_a_defined_value_and_never_zero(int value) =>
        V<Pick>(x => x.RuleFor(s => s.Value).ValidEnum()).ShouldFail(new Pick((Color)value), "Value", ValidationKeys.InvalidEnum);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_defined_enum_is_valid(int value) =>
        V<Pick>(x => x.RuleFor(s => s.Value).ValidEnum()).ShouldBeValid(new Pick((Color)value));

    // ---------- money ----------

    [Theory]
    [InlineData("0")]
    [InlineData("0.01")]
    [InlineData("12.5")]
    [InlineData("99999999.99")]
    public void These_amounts_are_valid(string amount) =>
        V<Num>(x => x.RuleFor(s => s.Value).Money()).ShouldBeValid(new Num(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void An_amount_is_not_negative_not_huge_and_has_at_most_two_decimals()
    {
        var v = V<Num>(x => x.RuleFor(s => s.Value).Money());

        v.ShouldFail(new Num(-0.01m), "Value", ValidationKeys.NegativeAmount);
        v.ShouldFail(new Num(100_000_000m), "Value", ValidationKeys.AmountTooLarge);
        v.ShouldFail(new Num(1.234m), "Value", ValidationKeys.InvalidValue);
    }

    [Theory]
    [InlineData("10", true)]
    [InlineData("10.5", true)]
    [InlineData("10.55", true)]
    [InlineData("10.555", false)]
    [InlineData("0.001", false)]
    public void Two_decimals_at_most(string value, bool ok) =>
        ValidationRules.HaveTwoDecimalsOrLess(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(ok);

    [Theory]
    [InlineData("0", true)]
    [InlineData("100", true)]
    [InlineData("50.5", true)]
    [InlineData("-0.1", false)]
    [InlineData("100.1", false)]
    public void A_percent_is_between_0_and_100(string value, bool ok)
    {
        var v = V<Num>(x => x.RuleFor(s => s.Value).Percent());
        var model = new Num(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        if (ok) v.ShouldBeValid(model);
        else v.ShouldFail(model, "Value", ValidationKeys.InvalidPercent);
    }

    // ---------- dates ----------

    [Fact]
    public void A_date_of_birth_is_in_the_past_and_less_than_120_years_ago()
    {
        var today = ClinicClock.Today;
        var v = V<Day>(x => x.RuleFor(s => s.Value).DateOfBirth());

        v.ShouldFail(new Day(default), "Value", ValidationKeys.Required);
        v.ShouldFail(new Day(today), "Value", ValidationKeys.DateInFuture);
        v.ShouldFail(new Day(today.AddDays(1)), "Value", ValidationKeys.DateInFuture);
        v.ShouldFail(new Day(today.AddYears(-120)), "Value", ValidationKeys.InvalidDateOfBirth);
        v.ShouldFail(new Day(today.AddYears(-121)), "Value", ValidationKeys.InvalidDateOfBirth);
        v.ShouldBeValid(new Day(today.AddDays(-1)));
        v.ShouldBeValid(new Day(today.AddYears(-120).AddDays(1)));
    }

    [Fact]
    public void A_booking_date_cannot_be_in_the_past_but_today_is_fine()
    {
        var today = ClinicClock.Today;
        var v = V<Day>(x => x.RuleFor(s => s.Value).NotInThePast());

        v.ShouldFail(new Day(default), "Value", ValidationKeys.Required);
        v.ShouldFail(new Day(today.AddDays(-1)), "Value", ValidationKeys.DateInPast);
        v.ShouldBeValid(new Day(today));
        v.ShouldBeValid(new Day(today.AddYears(1)));
    }

    [Fact]
    public void An_omitted_date_is_refused_by_the_real_date_rule()
    {
        V<Day>(x => x.RuleFor(s => s.Value).RealDate()).ShouldFail(new Day(default), "Value", ValidationKeys.Required);
        V<Day>(x => x.RuleFor(s => s.Value).RealDate()).ShouldBeValid(new Day(new DateOnly(2030, 1, 1)));
    }

    // ---------- files ----------

    [Theory]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("A.PNG", "IMAGE/PNG")]
    [InlineData("my.photo.png", "image/png")]
    public void An_image_of_an_allowed_type_is_valid(string name, string type) =>
        V<Upload>(x => x.RuleFor(s => s.Value).OptionalImage()).ShouldBeValid(new Upload(FormFileOf(name, type)));

    [Fact]
    public void No_file_is_fine_for_an_optional_image() =>
        V<Upload>(x => x.RuleFor(s => s.Value).OptionalImage()).ShouldBeValid(new Upload(null));

    [Theory]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.svg", "image/svg+xml")]
    [InlineData("a.html", "text/html")]
    [InlineData("a.pdf", "application/pdf")]      // a document, not an image
    [InlineData("noextension", "image/png")]
    [InlineData("a.png", "text/html")]            // right name, wrong content type
    [InlineData("a.png", "")]
    public void Other_image_types_are_refused(string name, string type) =>
        V<Upload>(x => x.RuleFor(s => s.Value).OptionalImage()).ShouldFail(new Upload(FormFileOf(name, type)), "Value", ValidationKeys.FileTypeNotAllowed);

    [Fact]
    public void The_image_size_limit_is_5_MB_inclusive()
    {
        var v = V<Upload>(x => x.RuleFor(s => s.Value).OptionalImage());

        v.ShouldBeValid(new Upload(FormFileOf("a.png", "image/png", ValidationRules.MaxImageBytes)));
        v.ShouldFail(new Upload(FormFileOf("a.png", "image/png", ValidationRules.MaxImageBytes + 1)), "Value", ValidationKeys.FileTooLarge);
        v.ShouldFail(new Upload(FormFileOf("a.png", "image/png", 0)), "Value", ValidationKeys.FileRequired);
    }

    [Theory]
    [InlineData("../a.png")]
    [InlineData("..\\a.png")]
    [InlineData("dir/a.png")]
    [InlineData("dir\\a.png")]
    [InlineData("a\0.png")]
    public void A_file_name_with_a_path_in_it_is_refused(string name) =>
        V<Upload>(x => x.RuleFor(s => s.Value).OptionalImage()).ShouldFail(new Upload(FormFileOf(name, "image/png")), "Value", ValidationKeys.FileNameInvalid);

    [Fact]
    public void A_file_name_longer_than_255_characters_is_refused() =>
        V<Upload>(x => x.RuleFor(s => s.Value).OptionalImage())
            .ShouldFail(new Upload(FormFileOf(new string('a', 252) + ".png", "image/png")), "Value", ValidationKeys.FileNameInvalid);

    [Fact]
    public void A_required_image_must_be_sent()
    {
        var v = V<Upload>(x => x.RuleFor(s => s.Value!).RequiredImage());

        v.ShouldFail(new Upload(null), "Value", ValidationKeys.FileRequired);
        v.ShouldBeValid(new Upload(FormFileOf("a.png", "image/png")));
    }

    [Theory]
    [InlineData("a.pdf", "application/pdf")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    public void A_document_may_be_an_image_or_a_pdf(string name, string type) =>
        V<Upload>(x => x.RuleFor(s => s.Value).OptionalDocument()).ShouldBeValid(new Upload(FormFileOf(name, type)));

    [Theory]
    [InlineData("a.webp", "image/webp")]          // an image type, but not allowed for documents
    [InlineData("a.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("a.exe", "application/octet-stream")]
    public void Other_document_types_are_refused(string name, string type) =>
        V<Upload>(x => x.RuleFor(s => s.Value).OptionalDocument()).ShouldFail(new Upload(FormFileOf(name, type)), "Value", ValidationKeys.FileTypeNotAllowed);

    // ---------- urls and text ----------

    [Theory]
    [InlineData("https://example.com/a?b=1")]
    [InlineData("http://example.com")]
    public void An_http_url_is_valid(string url) => V<Str>(x => x.RuleFor(s => s.Value!).HttpUrl()).ShouldBeValid(new Str(url));

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("example.com")]
    [InlineData("//example.com")]
    public void Anything_but_http_and_https_is_refused(string url) =>
        V<Str>(x => x.RuleFor(s => s.Value!).HttpUrl()).ShouldFail(new Str(url), "Value", ValidationKeys.InvalidUrl);

    [Fact]
    public void Required_and_optional_text_have_their_limits()
    {
        var required = V<Str>(x => x.RuleFor(s => s.Value!).RequiredText(10));
        var optional = V<Str>(x => x.RuleFor(s => s.Value).OptionalText(10));

        required.ShouldFail(new Str(""), "Value", ValidationKeys.Required);
        required.ShouldFail(new Str("   "), "Value", ValidationKeys.Required);
        required.ShouldFail(new Str(Long(11)), "Value", ValidationKeys.TooLong);
        required.ShouldBeValid(new Str(Long(10)));
        optional.ShouldBeValid(new Str(null));
        optional.ShouldBeValid(new Str(""));
        optional.ShouldFail(new Str(Long(11)), "Value", ValidationKeys.TooLong);
    }

    // ---------- paging ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10_001)]
    public void A_page_number_must_be_between_1_and_10000(int page) =>
        new PagedRequestValidator().ShouldFail(new PagedRequest { PageNumber = page }, "PageNumber", ValidationKeys.InvalidValue);

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public void A_page_number_inside_the_range_is_valid(int page) =>
        new PagedRequestValidator().ShouldBeValid(new PagedRequest { PageNumber = page });

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(500, 50)]
    public void A_page_size_never_leaves_the_range_because_the_setter_clamps_it(int sent, int expected)
    {
        var r = new PagedRequest { PageSize = sent };

        r.PageSize.ShouldBe(expected);
        new PagedRequestValidator().ShouldBeValid(r);
    }
}
