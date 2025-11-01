using FluentValidation;
using Takeaway.Api.Contracts.Voice;

namespace Takeaway.Api.Validation;

public class VoiceSessionRequestValidator : AbstractValidator<VoiceSessionRequest>
{
    public VoiceSessionRequestValidator()
    {
        RuleFor(x => x.AudioChunks)
            .NotNull().WithMessage("Audio chunks are required.")
            .Must(chunks => chunks.Count > 0).WithMessage("At least one audio chunk must be provided.");

        RuleForEach(x => x.AudioChunks)
            .NotEmpty().WithMessage("Audio chunks must not be empty.");

        RuleFor(x => x.ResponseText)
            .MaximumLength(2000);

        RuleFor(x => x.Voice)
            .MaximumLength(100);
    }
}
