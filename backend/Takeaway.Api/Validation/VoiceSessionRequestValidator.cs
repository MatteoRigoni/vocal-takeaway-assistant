using System.Linq;
using FluentValidation;
using Takeaway.Api.Contracts.Voice;

namespace Takeaway.Api.Validation;

public class VoiceSessionRequestValidator : AbstractValidator<VoiceSessionRequest>
{
    public VoiceSessionRequestValidator()
    {
        RuleFor(x => x.CallerId)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.AudioChunks)
            .NotEmpty()
            .When(x => string.IsNullOrWhiteSpace(x.UtteranceText))
            .WithMessage("Provide audio chunks when no utterance text is supplied.");

        RuleForEach(x => x.AudioChunks)
            .NotEmpty()
            .When(x => x.AudioChunks != null)
            .WithMessage("Individual audio chunks must not be empty.");

        RuleFor(x => x)
            .Must(HasAudioOrText)
            .WithMessage("Provide either audio chunks or an utterance text.");

        RuleFor(x => x.UtteranceText)
            .MaximumLength(2000);

        RuleFor(x => x.Voice)
            .MaximumLength(100);
    }

    private static bool HasAudioOrText(VoiceSessionRequest request)
    {
        var hasAudio = request.AudioChunks is { Count: > 0 } &&
                       request.AudioChunks.Any(chunk => !string.IsNullOrWhiteSpace(chunk));

        if (hasAudio)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(request.UtteranceText);
    }
}
