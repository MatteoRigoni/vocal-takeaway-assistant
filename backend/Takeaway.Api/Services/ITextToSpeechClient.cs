using System.Collections.Generic;
using System.Threading;

namespace Takeaway.Api.Services;

public interface ITextToSpeechClient
{
    IAsyncEnumerable<byte[]> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default);
}
