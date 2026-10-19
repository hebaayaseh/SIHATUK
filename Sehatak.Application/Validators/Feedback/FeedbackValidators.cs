using FluentValidation;
using Sehatak.Application.DTOs.FeedbackDto;
using Sehatak.Application.Validators.Common;

namespace Sehatak.Application.Validators.Feedback
{
    public class SubmitFeedbackRequestValidator : AbstractValidator<SubmitFeedbackRequestDto>
    {
        public SubmitFeedbackRequestValidator()
        {
            RuleFor(x => x.Type).ValidEnum();
            RuleFor(x => x.Subject).RequiredText(200);
            RuleFor(x => x.Message).RequiredText(2000);
        }
    }

    public class SubmitGuestFeedbackRequestValidator : AbstractValidator<SubmitGuestFeedbackRequestDto>
    {
        public SubmitGuestFeedbackRequestValidator()
        {
            RuleFor(x => x.Name).PersonName();
            RuleFor(x => x.Email).ValidEmail();
            RuleFor(x => x.Phone).OptionalPhone();
            RuleFor(x => x.CenterId).ValidOptionalId();
            RuleFor(x => x.Type).ValidEnum();
            RuleFor(x => x.Subject).RequiredText(200);
            RuleFor(x => x.Message).RequiredText(2000);
        }
    }

    public class ReplyFeedbackRequestValidator : AbstractValidator<ReplyFeedbackRequestDto>
    {
        public ReplyFeedbackRequestValidator()
        {
            RuleFor(x => x.Reply).RequiredText(2000);
            RuleFor(x => x.Status).ValidEnum();
        }
    }

    public class UpdateFeedbackStatusRequestValidator : AbstractValidator<UpdateFeedbackStatusRequestDto>
    {
        public UpdateFeedbackStatusRequestValidator()
        {
            RuleFor(x => x.Status).ValidEnum();
        }
    }
}
