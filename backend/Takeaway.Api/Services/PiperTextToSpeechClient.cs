using System;
using System.Buffers;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Takeaway.Api.Services;

public class PiperTextToSpeechClient : ITextToSpeechClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<PiperTextToSpeechClient> _logger;

    public PiperTextToSpeechClient(HttpClient httpClient, ILogger<PiperTextToSpeechClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<byte[]> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_httpClient.BaseAddress is null)
        {
            throw new SpeechClientException("Text-to-speech service endpoint is not configured.");
        }

        var payload = JsonSerializer.Serialize(new { text = request.Text, voice = request.Voice }, SerializerOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/audio/speech")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var statusCode = response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Text-to-speech call failed with status {Status}: {Body}", statusCode, error);
            throw new SpeechClientException("Text-to-speech request failed.", statusCode, error);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    yield break;
                }

                var chunk = new byte[bytesRead];
                Array.Copy(buffer, chunk, bytesRead);
                yield return chunk;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
