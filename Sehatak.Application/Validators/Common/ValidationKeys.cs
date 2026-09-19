namespace Sehatak.Application.Validators.Common
{
    /// <summary>
    /// Resource keys returned by the validators.
    /// They follow the same convention as BusinessException keys so the API layer
    /// can resolve them through IStringLocalizer("Messages").
    /// Every key here must exist in Messages.en.resx and Messages.ar.resx.
    /// </summary>
    public static class ValidationKeys
    {
        // generic
        public const string Required = "Validation.Required";
        public const string TooLong = "Validation.TooLong";
        public const string TooShort = "Validation.TooShort";
        public const string InvalidValue = "Validation.InvalidValue";
        public const string InvalidId = "Validation.InvalidId";
        public const string InvalidEnum = "Validation.InvalidEnum";
        public const string AtLeastOneField = "Validation.AtLeastOneFieldRequired";

        // identity
        public const string InvalidEmail = "Validation.InvalidEmail";
        public const string InvalidPhone = "Validation.InvalidPhone";
        public const string PasswordTooShort = "Validation.PasswordTooShort";
        public const string PasswordTooWeak = "Validation.PasswordTooWeak";
        public const string PasswordMismatch = "Validation.PasswordMismatch";
        public const string SamePassword = "Validation.SamePassword";
        public const string InvalidOtp = "Validation.InvalidOtpFormat";

        // dates & times
        public const string InvalidDate = "Validation.InvalidDate";
        public const string DateInPast = "Validation.DateInPast";
        public const string DateInFuture = "Validation.DateInFuture";
        public const string DateTooFar = "Validation.DateTooFar";
        public const string EndBeforeStart = "Validation.EndTimeBeforeStartTime";
        public const string ShiftTooShort = "Validation.ShiftTooShort";
        public const string InvalidDateOfBirth = "Validation.InvalidDateOfBirth";

        // money & numbers
        public const string NegativeAmount = "Validation.NegativeAmount";
        public const string AmountTooLarge = "Validation.AmountTooLarge";
        public const string InvalidPercent = "Validation.InvalidPercent";
        public const string InvalidQuantity = "Validation.InvalidQuantity";
        public const string InvalidRating = "Validation.InvalidRating";
        public const string InvalidSlotDuration = "Validation.InvalidSlotDuration";

        // files
        public const string FileRequired = "Validation.FileRequired";
        public const string FileTooLarge = "Validation.FileTooLarge";
        public const string FileTypeNotAllowed = "Validation.FileTypeNotAllowed";
        public const string FileNameInvalid = "Validation.FileNameInvalid";

        // collections
        public const string ListEmpty = "Validation.ListEmpty";
        public const string ListTooLarge = "Validation.ListTooLarge";
        public const string DuplicateItems = "Validation.DuplicateItems";

        // urls
        public const string InvalidUrl = "Validation.InvalidUrl";
    }
}
