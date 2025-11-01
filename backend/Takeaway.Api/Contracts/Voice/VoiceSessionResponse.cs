using System.Collections.Generic;

namespace Takeaway.Api.Contracts.Voice;

public sealed record VoiceSessionResponse(
    string RecognizedText,
    IReadOnlyList<string> ResponseAudioChunks,
    string PromptText,
    string DialogState,
    bool IsSessionComplete,
    IReadOnlyDictionary<string, string>? Metadata
);
