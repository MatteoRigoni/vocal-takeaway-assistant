using System;
using System.Linq;
using FluentValidation;
using Takeaway.Api.Contracts.Voice;

namespace Takeaway.Api.Validation;

public class VoiceSessionRequestValidator : AbstractValidator<VoiceSessionRequest>
{
    public VoiceSessionRequestValidator()
    {
        RuleForEach(x => x.AudioChunks ?? Array.Empty<string>())
            .NotEmpty().WithMessage("Audio chunks must not be empty.");

        RuleFor(x => x)
            .Must(HasAudioOrResponseText)
            .WithMessage("Provide at least one audio chunk or a response text.");

        RuleFor(x => x.ResponseText)
            .MaximumLength(2000);

        RuleFor(x => x.Voice)
            .MaximumLength(100);
    }

    private static bool HasAudioOrResponseText(VoiceSessionRequest request)
    {
        var hasAudio = request.AudioChunks is { Count: > 0 } &&
                       request.AudioChunks.Any(chunk => !string.IsNullOrWhiteSpace(chunk));

        if (hasAudio)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(request.ResponseText);
    }
}
