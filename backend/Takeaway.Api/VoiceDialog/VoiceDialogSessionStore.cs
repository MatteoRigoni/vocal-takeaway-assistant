using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Takeaway.Api.VoiceDialog;

public interface IVoiceDialogSessionStore
{
    Task<VoiceDialogSession> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken);

    Task SaveAsync(VoiceDialogSession session, CancellationToken cancellationToken);

    Task ClearAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed class InMemoryVoiceDialogSessionStore : IVoiceDialogSessionStore
{
    private readonly ConcurrentDictionary<string, VoiceDialogSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _idleTimeout;
    private readonly ILogger<InMemoryVoiceDialogSessionStore> _logger;

    public InMemoryVoiceDialogSessionStore(TimeSpan? idleTimeout, ILogger<InMemoryVoiceDialogSessionStore> logger)
    {
        _idleTimeout = idleTimeout ?? TimeSpan.FromMinutes(30);
        _logger = logger;
    }

    public Task<VoiceDialogSession> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id must be provided", nameof(sessionId));
        }

        var now = DateTimeOffset.UtcNow;
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            if (now - existing.UpdatedAt > _idleTimeout)
            {
                _logger.LogDebug("Expiring dialog session {SessionId} after {IdleTimeout} idle time.", sessionId, _idleTimeout);
                _sessions.TryRemove(sessionId, out _);
            }
            else
            {
                return Task.FromResult(existing);
            }
        }

        var session = new VoiceDialogSession(sessionId);
        _sessions[sessionId] = session;
        return Task.FromResult(session);
    }

    public Task SaveAsync(VoiceDialogSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(session);

        session.Touch();
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Task.CompletedTask;
        }

        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
