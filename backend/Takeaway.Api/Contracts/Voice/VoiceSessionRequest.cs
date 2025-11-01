using System.Collections.Generic;

namespace Takeaway.Api.Contracts.Voice;

public sealed record VoiceSessionRequest(
    IReadOnlyList<string> AudioChunks,
    string? ResponseText,
    string? Voice
);
