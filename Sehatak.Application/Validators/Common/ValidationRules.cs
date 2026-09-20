using FluentValidation;
using Microsoft.AspNetCore.Http;
using Sehatak.Application.Common;
using System.Text.RegularExpressions;

namespace Sehatak.Application.Validators.Common
{
    /// <summary>
    /// Shared rule builders so every DTO validates identity, money, dates and
    /// uploads the same way. Tune the constants here, not in each validator.
    /// </summary>
    public static class ValidationRules
    {
        // ---- tunable limits -------------------------------------------------
        public const int MaxNameLength = 100;
        public const int MaxEmailLength = 150;
        public const int MaxPhoneLength = 20;
        public const int MaxAddressLength = 500;
        public const int MaxCityLength = 100;
        public const int MaxNoteLength = 500;
        public const int MaxReasonLength = 500;
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 128;

        public const long MaxImageBytes = 5 * 1024 * 1024;     // 5 MB
        public const long MaxDocumentBytes = 5 * 1024 * 1024;  // 5 MB

        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        public static readonly string[] DocumentExtensions = { ".jpg", ".jpeg", ".png", ".pdf" };

        public static readonly string[] ImageContentTypes =
            { "image/jpeg", "image/png", "image/webp" };
        public static readonly string[] DocumentContentTypes =
            { "image/jpeg", "image/png", "application/pdf" };

        // Accepts local PS/IL mobile formats and generic E.164:
        //   0599123456 / 00970599123456 / +970599123456 / +972... / +14155552671
        private static readonly Regex PhoneRegex = new(
            @"^(\+|00)?[1-9]\d{6,14}$", RegexOptions.Compiled);

        // At least one lowercase, one uppercase and one digit.
        private static readonly Regex StrongPasswordRegex = new(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", RegexOptions.Compiled);

        private static readonly Regex OtpRegex = new(@"^\d{6}$", RegexOptions.Compiled);

        // ---- identity -------------------------------------------------------

        public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(MaxEmailLength).WithMessage(ValidationKeys.TooLong)
                .EmailAddress().WithMessage(ValidationKeys.InvalidEmail);

        // null is skipped by MaximumLength / EmailAddress, so no extra When() is needed.
        public static IRuleBuilderOptions<T, string?> OptionalEmail<T>(this IRuleBuilder<T, string?> rule) =>
            rule.MaximumLength(MaxEmailLength).WithMessage(ValidationKeys.TooLong)
                .EmailAddress().WithMessage(ValidationKeys.InvalidEmail);

        public static IRuleBuilderOptions<T, string> ValidPhone<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(MaxPhoneLength).WithMessage(ValidationKeys.TooLong)
                .Must(BeAPhoneNumber).WithMessage(ValidationKeys.InvalidPhone);

        public static IRuleBuilderOptions<T, string?> OptionalPhone<T>(this IRuleBuilder<T, string?> rule) =>
            rule.MaximumLength(MaxPhoneLength).WithMessage(ValidationKeys.TooLong)
                .Must(p => p is null || BeAPhoneNumber(p)).WithMessage(ValidationKeys.InvalidPhone);

        public static bool BeAPhoneNumber(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var normalized = value.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
            return PhoneRegex.IsMatch(normalized);
        }

        public static IRuleBuilderOptions<T, string> ValidPassword<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage(ValidationKeys.Required)
                .MinimumLength(MinPasswordLength).WithMessage(ValidationKeys.PasswordTooShort)
                .MaximumLength(MaxPasswordLength).WithMessage(ValidationKeys.TooLong)
                .Matches(StrongPasswordRegex).WithMessage(ValidationKeys.PasswordTooWeak);

        public static IRuleBuilderOptions<T, string> ValidOtpCode<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage(ValidationKeys.Required)
                .Matches(OtpRegex).WithMessage(ValidationKeys.InvalidOtp);

        public static IRuleBuilderOptions<T, string> PersonName<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage(ValidationKeys.Required)
                .MinimumLength(2).WithMessage(ValidationKeys.TooShort)
                .MaximumLength(MaxNameLength).WithMessage(ValidationKeys.TooLong);

        public static IRuleBuilderOptions<T, string?> OptionalPersonName<T>(this IRuleBuilder<T, string?> rule) =>
            rule.MinimumLength(2).WithMessage(ValidationKeys.TooShort)
                .MaximumLength(MaxNameLength).WithMessage(ValidationKeys.TooLong);

        // ---- identifiers ----------------------------------------------------

        public static IRuleBuilderOptions<T, int> ValidId<T>(this IRuleBuilder<T, int> rule) =>
            rule.GreaterThan(0).WithMessage(ValidationKeys.InvalidId);

        public static IRuleBuilderOptions<T, int?> ValidOptionalId<T>(this IRuleBuilder<T, int?> rule) =>
            rule.GreaterThan(0).WithMessage(ValidationKeys.InvalidId);

        /// <summary>
        /// Enums in this solution start at 1, so the CLR default (0) is never a
        /// legal value. Without this rule a missing enum silently persists as 0.
        /// </summary>
        public static IRuleBuilderOptions<T, TEnum> ValidEnum<T, TEnum>(this IRuleBuilder<T, TEnum> rule)
            where TEnum : struct, Enum =>
            rule.IsInEnum().WithMessage(ValidationKeys.InvalidEnum)
                .Must(v => Convert.ToInt32(v) != 0).WithMessage(ValidationKeys.InvalidEnum);

        public static IRuleBuilderOptions<T, TEnum?> ValidOptionalEnum<T, TEnum>(this IRuleBuilder<T, TEnum?> rule)
            where TEnum : struct, Enum =>
            rule.IsInEnum().WithMessage(ValidationKeys.InvalidEnum)
                .Must(v => v is null || Convert.ToInt32(v.Value) != 0).WithMessage(ValidationKeys.InvalidEnum);

        // ---- money ----------------------------------------------------------

        public static IRuleBuilderOptions<T, decimal> Money<T>(this IRuleBuilder<T, decimal> rule) =>
            rule.GreaterThanOrEqualTo(0).WithMessage(ValidationKeys.NegativeAmount)
                .LessThanOrEqualTo(99_999_999.99m).WithMessage(ValidationKeys.AmountTooLarge)
                .Must(HaveTwoDecimalsOrLess).WithMessage(ValidationKeys.InvalidValue);

        public static IRuleBuilderOptions<T, decimal?> OptionalMoney<T>(this IRuleBuilder<T, decimal?> rule) =>
            rule.GreaterThanOrEqualTo(0).WithMessage(ValidationKeys.NegativeAmount)
                .LessThanOrEqualTo(99_999_999.99m).WithMessage(ValidationKeys.AmountTooLarge)
                .Must(v => v is null || HaveTwoDecimalsOrLess(v.Value)).WithMessage(ValidationKeys.InvalidValue);

        /// <summary>DB columns are decimal(10,2); more precision is silently truncated.</summary>
        public static bool HaveTwoDecimalsOrLess(decimal value) =>
            decimal.Round(value, 2) == value;

        public static IRuleBuilderOptions<T, decimal> Percent<T>(this IRuleBuilder<T, decimal> rule) =>
            rule.InclusiveBetween(0, 100).WithMessage(ValidationKeys.InvalidPercent);

        public static IRuleBuilderOptions<T, decimal?> OptionalPercent<T>(this IRuleBuilder<T, decimal?> rule) =>
            rule.InclusiveBetween(0, 100).WithMessage(ValidationKeys.InvalidPercent);

        // ---- dates ----------------------------------------------------------

        public static DateOnly Today => ClinicClock.Today;

        public static IRuleBuilderOptions<T, DateOnly> NotInThePast<T>(this IRuleBuilder<T, DateOnly> rule) =>
            rule.Must(d => d != default).WithMessage(ValidationKeys.Required)
                .GreaterThanOrEqualTo(_ => Today).WithMessage(ValidationKeys.DateInPast);

        public static IRuleBuilderOptions<T, DateOnly?> OptionalNotInThePast<T>(this IRuleBuilder<T, DateOnly?> rule) =>
            rule.GreaterThanOrEqualTo(_ => Today).WithMessage(ValidationKeys.DateInPast);

        /// <summary>Guards against 0001-01-01 arriving because the caller omitted the field.</summary>
        public static IRuleBuilderOptions<T, DateOnly> RealDate<T>(this IRuleBuilder<T, DateOnly> rule) =>
            rule.Must(d => d != default).WithMessage(ValidationKeys.Required);

        public static IRuleBuilderOptions<T, DateOnly> DateOfBirth<T>(this IRuleBuilder<T, DateOnly> rule) =>
            rule.Must(d => d != default).WithMessage(ValidationKeys.Required)
                .LessThan(_ => Today).WithMessage(ValidationKeys.DateInFuture)
                .GreaterThan(_ => Today.AddYears(-120)).WithMessage(ValidationKeys.InvalidDateOfBirth);

        public static IRuleBuilderOptions<T, DateOnly?> OptionalDateOfBirth<T>(this IRuleBuilder<T, DateOnly?> rule) =>
            rule.LessThan(_ => Today).WithMessage(ValidationKeys.DateInFuture)
                .GreaterThan(_ => Today.AddYears(-120)).WithMessage(ValidationKeys.InvalidDateOfBirth);

        public static IRuleBuilderOptions<T, DateTime> RealDateTime<T>(this IRuleBuilder<T, DateTime> rule) =>
            rule.Must(d => d != default).WithMessage(ValidationKeys.Required);

        public static IRuleBuilderOptions<T, TimeOnly> RealTime<T>(this IRuleBuilder<T, TimeOnly> rule) =>
            rule.Must(t => t != default).WithMessage(ValidationKeys.Required);

        // ---- files ----------------------------------------------------------

        public static IRuleBuilderOptions<T, IFormFile?> OptionalImage<T>(this IRuleBuilder<T, IFormFile?> rule) =>
            rule.Must(f => f is null || f.Length > 0).WithMessage(ValidationKeys.FileRequired)
                .Must(f => f is null || f.Length <= MaxImageBytes).WithMessage(ValidationKeys.FileTooLarge)
                .Must(f => f is null || HasAllowedExtension(f, ImageExtensions)).WithMessage(ValidationKeys.FileTypeNotAllowed)
                .Must(f => f is null || HasAllowedContentType(f, ImageContentTypes)).WithMessage(ValidationKeys.FileTypeNotAllowed)
                .Must(f => f is null || HasSafeFileName(f)).WithMessage(ValidationKeys.FileNameInvalid);

        public static IRuleBuilderOptions<T, IFormFile> RequiredImage<T>(this IRuleBuilder<T, IFormFile> rule) =>
            rule.NotNull().WithMessage(ValidationKeys.FileRequired)
                .Must(f => f is not null && f.Length > 0).WithMessage(ValidationKeys.FileRequired)
                .Must(f => f is null || f.Length <= MaxImageBytes).WithMessage(ValidationKeys.FileTooLarge)
                .Must(f => f is null || HasAllowedExtension(f, ImageExtensions)).WithMessage(ValidationKeys.FileTypeNotAllowed)
                .Must(f => f is null || HasAllowedContentType(f, ImageContentTypes)).WithMessage(ValidationKeys.FileTypeNotAllowed)
                .Must(f => f is null || HasSafeFileName(f)).WithMessage(ValidationKeys.FileNameInvalid);

        public static IRuleBuilderOptions<T, IFormFile?> OptionalDocument<T>(this IRuleBuilder<T, IFormFile?> rule) =>
            rule.Must(f => f is null || f.Length > 0).WithMessage(ValidationKeys.FileRequired)
                .Must(f => f is null || f.Length <= MaxDocumentBytes).WithMessage(ValidationKeys.FileTooLarge)
                .Must(f => f is null || HasAllowedExtension(f, DocumentExtensions)).WithMessage(ValidationKeys.FileTypeNotAllowed)
                .Must(f => f is null || HasAllowedContentType(f, DocumentContentTypes)).WithMessage(ValidationKeys.FileTypeNotAllowed)
                .Must(f => f is null || HasSafeFileName(f)).WithMessage(ValidationKeys.FileNameInvalid);

        public static bool HasAllowedExtension(IFormFile file, string[] allowed)
        {
            var ext = Path.GetExtension(file.FileName);
            return !string.IsNullOrEmpty(ext) &&
                   allowed.Contains(ext.ToLowerInvariant());
        }

        public static bool HasAllowedContentType(IFormFile file, string[] allowed) =>
            !string.IsNullOrWhiteSpace(file.ContentType) &&
            allowed.Contains(file.ContentType.ToLowerInvariant());

        /// <summary>Blocks path traversal and NUL injection in the original file name.</summary>
        public static bool HasSafeFileName(IFormFile file)
        {
            var name = file.FileName;
            if (string.IsNullOrWhiteSpace(name) || name.Length > 255) return false;
            if (name.Contains("..") || name.Contains('/') || name.Contains('\\') || name.Contains('\0'))
                return false;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        // ---- misc -----------------------------------------------------------

        public static IRuleBuilderOptions<T, string?> OptionalText<T>(this IRuleBuilder<T, string?> rule, int max) =>
            rule.MaximumLength(max).WithMessage(ValidationKeys.TooLong);

        public static IRuleBuilderOptions<T, string> RequiredText<T>(this IRuleBuilder<T, string> rule, int max) =>
            rule.NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(max).WithMessage(ValidationKeys.TooLong);

        public static IRuleBuilderOptions<T, string> HttpUrl<T>(this IRuleBuilder<T, string> rule) =>
            rule.NotEmpty().WithMessage(ValidationKeys.Required)
                .MaximumLength(2048).WithMessage(ValidationKeys.TooLong)
                .Must(BeAnHttpUrl).WithMessage(ValidationKeys.InvalidUrl);

        public static bool BeAnHttpUrl(string? value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
