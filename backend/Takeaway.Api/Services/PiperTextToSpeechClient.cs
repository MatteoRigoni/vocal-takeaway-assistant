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
    
    private const string DefaultVoice = "it_IT-paola-medium";

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

        _logger.LogInformation("Requesting TTS for text: {Text} with voice: {Voice}", request.Text, request.Voice ?? "default");

        var voice = request.Voice ?? DefaultVoice;
        // Il servizio Piper si aspetta solo il campo 'text'
        var payload = JsonSerializer.Serialize(new { text = request.Text }, SerializerOptions);

        _logger.LogDebug("TTS request payload: {Payload}", payload);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, string.Empty)
        {
            Content = new StringContent(request.Text, Encoding.UTF8, "text/plain")
        };
        
        // Aggiungiamo l'header Accept per specificare che vogliamo audio WAV
        httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("audio/wav"));

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var statusCode = response.StatusCode;
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Text-to-speech call failed with status {Status}: {Body}", statusCode, error);
            
            if (statusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogError("TTS endpoint not found. Make sure Piper is running and the endpoint is correct");
            }
            
            throw new SpeechClientException("Text-to-speech request failed.", statusCode, error);
        }
        
        _logger.LogInformation("Successfully received TTS response with status {Status}", statusCode);

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
