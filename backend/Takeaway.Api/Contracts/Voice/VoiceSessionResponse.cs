using System.Collections.Generic;

namespace Takeaway.Api.Contracts.Voice;

public sealed record VoiceSessionResponse(string RecognizedText, IReadOnlyList<string> ResponseAudioChunks);
