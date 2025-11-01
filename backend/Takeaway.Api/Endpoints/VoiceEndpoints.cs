using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Takeaway.Api.Contracts.Voice;
using Takeaway.Api.Extensions;
using Takeaway.Api.Services;
using Takeaway.Api.Validation;

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
                CancellationToken cancellationToken
            ) =>
            {
                var validationResult = await validator.ValidateAsync(request, cancellationToken);
                if (!validationResult.IsValid)
                {
                    return TypedResults.BadRequest(validationResult.ToProblemDetails());
                }

                List<byte[]> audio;
                try
                {
                    audio = DecodeChunks(request.AudioChunks);
                }
                catch (FormatException ex)
                {
                    return TypedResults.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                    {
                        [nameof(request.AudioChunks)] = new[] { ex.Message }
                    }));
                }

                if (audio.Count == 0)
                {
                    return TypedResults.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
                    {
                        [nameof(request.AudioChunks)] = new[] { "At least one audio chunk must contain data." }
                    }));
                }

                SpeechRecognitionResult transcription;
                try
                {
                    transcription = await speechToTextClient.TranscribeAsync(ToStream(audio), cancellationToken);
                }
                catch (SpeechClientException ex)
                {
                    return ToProblemResult("Speech-to-text service error", ex);
                }
                catch (HttpRequestException ex)
                {
                    return TypedResults.Problem(title: "Speech-to-text service unreachable", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                var responseAudio = new List<string>();
                if (!string.IsNullOrWhiteSpace(request.ResponseText))
                {
                    try
                    {
                        await foreach (var chunk in textToSpeechClient.SynthesizeAsync(new TextToSpeechRequest(request.ResponseText!, request.Voice), cancellationToken))
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

                return TypedResults.Ok(new VoiceSessionResponse(transcription.Text, responseAudio));
            })
        .WithName("CreateVoiceSession")
        .WithSummary("Transcribe incoming audio and optionally synthesize a response.");

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
