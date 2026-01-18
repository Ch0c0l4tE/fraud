using System.Collections.Concurrent;
using Fraud.Sdk.Contracts;

namespace Fraud.Ingestion.Api.Repositories;

/// <summary>
/// In-memory implementation of ISessionRepository for development
/// </summary>
public sealed class InMemorySessionRepository : ISessionRepository
{
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    public Task<Session> CreateAsync(CreateSessionRequest request, CancellationToken cancellationToken = default)
    {
        var session = new Session
        {
            Id = Guid.NewGuid(),
            ClientId = request.ClientId,
            DeviceFingerprint = request.DeviceFingerprint,
            CreatedAt = DateTimeOffset.UtcNow,
            Metadata = request.Metadata
        };

        if (!_sessions.TryAdd(session.Id, session))
        {
            throw new InvalidOperationException("Failed to create session - ID collision");
        }

        return Task.FromResult(session);
    }

    public Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task<bool> ExistsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_sessions.ContainsKey(sessionId));
    }

    public Task<Session?> CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<Session?>(null);
        }

        var completedSession = session with { CompletedAt = DateTimeOffset.UtcNow };
        _sessions.TryUpdate(sessionId, completedSession, session);
        
        return Task.FromResult<Session?>(completedSession);
    }

    public Task<IReadOnlyList<Session>> GetByClientIdAsync(string clientId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var sessions = _sessions.Values
            .Where(s => s.ClientId == clientId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<Session>>(sessions);
    }
}
