using FluentValidation;
using Sehatak.Application.DTOs.AssignFeaturesWithPlan;
using Sehatak.Application.DTOs.FeatureCenterDto;
using Sehatak.Application.DTOs.FeatureDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Feature
{
    public class CreateFeatureRequestDtoValidator : AbstractValidator<CreateFeatureRequestDto>
    {
        public CreateFeatureRequestDtoValidator()
        {
            RuleFor(x => x.Name).RequiredText(100);
            RuleFor(x => x.Description).OptionalText(ValidationRules.MaxNoteLength);
        }
    }

    public class AssignFeatureToPlanRequestDtoValidator : AbstractValidator<AssignFeatureToPlanRequestDto>
    {
        public AssignFeatureToPlanRequestDtoValidator()
        {
            RuleFor(x => x.featureId).ValidId();
        }
    }

    public class ActiveFetureRequestValidator : AbstractValidator<ActiveFetureRequest>
    {
        public ActiveFetureRequestValidator()
        {
            RuleFor(x => x.FetureId).ValidId();
        }
    }

    public class AddFeatureToCenterRequestValidator : AbstractValidator<AddFeatureToCenterRequest>
    {
        public AddFeatureToCenterRequestValidator()
        {
            RuleFor(x => x.featureId).ValidId();
            RuleFor(x => x.featureName).RequiredText(100);
        }
    }

    public class RemoveFeatureFromCenterRequestValidator : AbstractValidator<RemoveFeatureFromCenterRequest>
    {
        public RemoveFeatureFromCenterRequestValidator()
        {
            RuleFor(x => x.featureId).ValidId();
        }
    }
}
