using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Takeaway.Api.Services;

public class FasterWhisperSpeechToTextClient : ISpeechToTextClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ILogger<FasterWhisperSpeechToTextClient> _logger;

    public FasterWhisperSpeechToTextClient(HttpClient httpClient, ILogger<FasterWhisperSpeechToTextClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SpeechRecognitionResult> TranscribeAsync(IAsyncEnumerable<byte[]> audioChunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioChunks);

        if (_httpClient.BaseAddress is null)
        {
            throw new SpeechClientException("Speech-to-text service endpoint is not configured.");
        }

        await using var buffer = new MemoryStream();
        await foreach (var chunk in audioChunks.WithCancellation(cancellationToken))
        {
            if (chunk is null || chunk.Length == 0)
            {
                continue;
            }

            await buffer.WriteAsync(chunk, 0, chunk.Length, cancellationToken);
        }

        if (buffer.Length == 0)
        {
            throw new SpeechClientException("No audio data was provided for transcription.");
        }

        buffer.Position = 0;

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(buffer);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(streamContent, "audio_file", "audio.wav");

        using var response = await _httpClient.PostAsync("v1/audio/transcriptions", content, cancellationToken);
        var statusCode = response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Speech-to-text call failed with status {Status}: {Body}", statusCode, error);
            throw new SpeechClientException("Speech-to-text request failed.", statusCode, error);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<FasterWhisperResponse>(responseStream, SerializerOptions, cancellationToken);
        if (payload?.Text is null)
        {
            throw new SpeechClientException("Speech-to-text response did not contain transcribed text.", statusCode);
        }

        return new SpeechRecognitionResult(payload.Text);
    }

    private sealed record FasterWhisperResponse(string? Text);
}
