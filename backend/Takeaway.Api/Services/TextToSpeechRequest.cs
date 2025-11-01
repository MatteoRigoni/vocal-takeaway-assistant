namespace Takeaway.Api.Services;

public sealed record TextToSpeechRequest(string Text, string? Voice = null);
