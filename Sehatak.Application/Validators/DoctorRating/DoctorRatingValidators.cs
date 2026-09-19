using FluentValidation;
using Sehatak.Application.DTOs.DoctorRatingDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.DoctorRating
{
    public class AddDoctorRatingRequestValidator : AbstractValidator<AddDoctorRatingRequest>
    {
        public AddDoctorRatingRequestValidator()
        {
            RuleFor(x => x.AppointmentId).ValidId();
            RuleFor(x => x.Rating)
                .InclusiveBetween(1, 5).WithMessage(ValidationKeys.InvalidRating);
            RuleFor(x => x.Review).OptionalText(1000);
        }
    }

    public class UpdateDoctorRatingRequestValidator : AbstractValidator<UpdateDoctorRatingRequest>
    {
        public UpdateDoctorRatingRequestValidator()
        {
            RuleFor(x => x.RatingId).ValidId();
            RuleFor(x => x.Rating)
                .InclusiveBetween(1, 5).WithMessage(ValidationKeys.InvalidRating);
            RuleFor(x => x.Review).OptionalText(1000);

            RuleFor(x => x)
                .Must(x => x.Rating is not null || x.Review is not null)
                .WithMessage(ValidationKeys.AtLeastOneField)
                .OverridePropertyName("request");
        }
    }
}
