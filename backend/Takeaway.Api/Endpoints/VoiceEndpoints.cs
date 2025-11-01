using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Takeaway.Api.Contracts.Voice;
using Takeaway.Api.Extensions;
using Takeaway.Api.Services;
using Takeaway.Api.Validation;
using Takeaway.Api.VoiceDialog;

namespace Takeaway.Api.Endpoints;

public static class VoiceEndpoints
{
    public static IEndpointRouteBuilder MapVoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/voice");

        group.MapPost("/session",
            async Task<Results<Ok<VoiceSessionResponse>, BadRequest<ValidationProblemDetails>, ProblemHttpResult>>(
                VoiceSessionRequest request,
                IValidator<VoiceSessionRequest> validator,
                ISpeechToTextClient speechToTextClient,
                ITextToSpeechClient textToSpeechClient,
                IVoiceDialogSessionStore sessionStore,
                IVoiceDialogStateMachine stateMachine,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken
            ) =>
            {
                var logger = loggerFactory.CreateLogger("VoiceSession");

                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                {
                    return TypedResults.BadRequest(validationResult.ToProblemDetails());
                }

                var audioChunks = request.AudioChunks ?? Array.Empty<string>();
                List<byte[]> audio;
                try
                {
                    audio = DecodeChunks(audioChunks);
                }
                catch (FormatException ex)
                {
                    return TypedResults.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                    {
                        [nameof(request.AudioChunks)] = new[] { ex.Message }
                    }));
                }

                var recognizedText = request.UtteranceText?.Trim() ?? string.Empty;
                if (audio.Count > 0)
                {
                    try
                    {
                        var transcription = await speechToTextClient.TranscribeAsync(ToStream(audio), cancellationToken);
                        recognizedText = transcription.Text;
                    }
                    catch (SpeechClientException ex)
                    {
                        return ToProblemResult("Speech-to-text service error", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        return TypedResults.Problem(title: "Speech-to-text service unreachable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                }

                var session = await sessionStore.GetOrCreateAsync(request.CallerId, cancellationToken);
                VoiceDialogResult dialogResult;
                try
                {
                    dialogResult = await stateMachine.HandleAsync(
                        session,
                        new VoiceDialogEvent(VoiceDialogEventType.Utterance, recognizedText),
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Dialog state machine failed for session {SessionId}.", session.Id);
                    var prompt = "Something went wrong while processing your request. Please try again.";
                    return TypedResults.Ok(new VoiceSessionResponse(
                        recognizedText,
                        Array.Empty<string>(),
                        prompt,
                        VoiceDialogState.Error.ToString(),
                        true,
                        new Dictionary<string, string> { ["error"] = "dialog-failure" }
                    ));
                }

                await sessionStore.SaveAsync(session, cancellationToken);

                if (dialogResult.IsSessionComplete)
                {
                    await sessionStore.ClearAsync(session.Id, cancellationToken);
                }

                var responseAudio = new List<string>();
                if (!string.IsNullOrWhiteSpace(dialogResult.PromptText))
                {
                    try
                    {
                        await foreach (var chunk in textToSpeechClient.SynthesizeAsync(new TextToSpeechRequest(dialogResult.PromptText, request.Voice), cancellationToken))
                        {
                            responseAudio.Add(Convert.ToBase64String(chunk));
                        }
                    }
                    catch (SpeechClientException ex)
                    {
                        return ToProblemResult("Text-to-speech service error", ex);
                    }
                    catch (HttpRequestException ex)
                    {
                        return TypedResults.Problem(title: "Text-to-speech service unreachable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                }

                return TypedResults.Ok(new VoiceSessionResponse(
                    recognizedText,
                    responseAudio,
                    dialogResult.PromptText,
                    dialogResult.State.ToString(),
                    dialogResult.IsSessionComplete,
                    dialogResult.Metadata
                ));
            })
        .WithName("CreateVoiceSession")
        .WithSummary("Transcribe incoming audio, orchestrate the dialog flow, and synthesize the response.");

        return app;
    }

    private static List<byte[]> DecodeChunks(IReadOnlyList<string> audioChunks)
    {
        var decoded = new List<byte[]>(audioChunks.Count);
        foreach (var chunk in audioChunks)
        {
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            decoded.Add(Convert.FromBase64String(chunk));
        }

        return decoded;
    }

    private static async IAsyncEnumerable<byte[]> ToStream(IEnumerable<byte[]> audioChunks)
    {
        foreach (var chunk in audioChunks)
        {
            if (chunk is null || chunk.Length == 0)
            {
                continue;
            }

            yield return chunk;
            await Task.Yield();
        }
    }

    private static ProblemHttpResult ToProblemResult(string title, SpeechClientException exception)
    {
        var status = exception.StatusCode.HasValue ? (int)exception.StatusCode.Value : StatusCodes.Status502BadGateway;
        var detail = !string.IsNullOrWhiteSpace(exception.ResponseBody) ? exception.ResponseBody : exception.Message;
        return TypedResults.Problem(title: title, detail: detail, statusCode: status);
    }
}
