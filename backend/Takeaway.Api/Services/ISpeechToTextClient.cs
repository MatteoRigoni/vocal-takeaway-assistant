using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Takeaway.Api.Services;

public interface ISpeechToTextClient
{
    Task<SpeechRecognitionResult> TranscribeAsync(IAsyncEnumerable<byte[]> audioChunks, CancellationToken cancellationToken = default);
}
