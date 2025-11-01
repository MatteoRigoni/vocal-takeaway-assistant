using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Takeaway.Api.Services;
using Xunit;

namespace Takeaway.Api.Tests.Unit;

public class SpeechClientTests
{
    [Fact]
    public async Task FasterWhisperSpeechToTextClient_TranscribeAsync_ReturnsText()
    {
        var handler = new TestHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/audio/transcriptions", request.RequestUri!.AbsolutePath);
            Assert.Contains("multipart/form-data", request.Content!.Headers.ContentType!.MediaType!, StringComparison.OrdinalIgnoreCase);

            using var stream = await request.Content!.ReadAsStreamAsync();
            Assert.True(stream.Length > 0);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\":\"hello\"}", Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://speech.local/")
        };

        var client = new FasterWhisperSpeechToTextClient(httpClient, NullLogger<FasterWhisperSpeechToTextClient>.Instance);

        var result = await client.TranscribeAsync(ToAsyncEnumerable(new byte[] { 1, 2, 3 }), CancellationToken.None);

        Assert.Equal("hello", result.Text);
    }

    [Fact]
    public async Task FasterWhisperSpeechToTextClient_TranscribeAsync_ThrowsOnError()
    {
        var handler = new TestHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("oops")
        }));

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://speech.local/")
        };

        var client = new FasterWhisperSpeechToTextClient(httpClient, NullLogger<FasterWhisperSpeechToTextClient>.Instance);

        var exception = await Assert.ThrowsAsync<SpeechClientException>(() => client.TranscribeAsync(ToAsyncEnumerable(new byte[] { 1 }), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("oops", exception.ResponseBody);
    }

    [Fact]
    public async Task PiperTextToSpeechClient_SynthesizeAsync_StreamsAudio()
    {
        var handler = new TestHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/audio/speech", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"text\":\"respond\"", body);
            Assert.Contains("\"voice\":\"test\"", body);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 5, 6, 7, 8 })
            };
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://tts.local/")
        };

        var client = new PiperTextToSpeechClient(httpClient, NullLogger<PiperTextToSpeechClient>.Instance);

        var chunks = new List<byte[]>();
        await foreach (var chunk in client.SynthesizeAsync(new TextToSpeechRequest("respond", "test"), CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, chunks.SelectMany(b => b).ToArray());
    }

    [Fact]
    public async Task PiperTextToSpeechClient_SynthesizeAsync_ThrowsOnError()
    {
        var handler = new TestHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("unavailable")
        }));

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://tts.local/")
        };

        var client = new PiperTextToSpeechClient(httpClient, NullLogger<PiperTextToSpeechClient>.Instance);

        var exception = await Assert.ThrowsAsync<SpeechClientException>(async () =>
        {
            await foreach (var _ in client.SynthesizeAsync(new TextToSpeechRequest("respond"), CancellationToken.None))
            {
            }
        });

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        Assert.Equal("unavailable", exception.ResponseBody);
    }

    private static async IAsyncEnumerable<byte[]> ToAsyncEnumerable(params byte[][] chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
            await Task.Yield();
        }
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public TestHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
