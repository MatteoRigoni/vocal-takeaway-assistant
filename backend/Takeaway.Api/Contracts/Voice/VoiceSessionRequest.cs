using System.Collections.Generic;

namespace Takeaway.Api.Contracts.Voice;

public sealed record VoiceSessionRequest(
    string CallerId,
    IReadOnlyList<string>? AudioChunks,
    string? UtteranceText,
    VoiceOrderSlotsDto? Slots,
    string? Voice
);
