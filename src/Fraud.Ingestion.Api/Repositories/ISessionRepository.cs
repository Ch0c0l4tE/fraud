using Fraud.Sdk.Contracts;

namespace Fraud.Ingestion.Api.Repositories;

/// <summary>
/// Repository for session storage and retrieval
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Create a new session
    /// </summary>
    Task<Session> CreateAsync(CreateSessionRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a session by ID
    /// </summary>
    Task<Session?> GetByIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if a session exists
    /// </summary>
    Task<bool> ExistsAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Mark a session as completed
    /// </summary>
    Task<Session?> CompleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get sessions by client ID
    /// </summary>
    Task<IReadOnlyList<Session>> GetByClientIdAsync(string clientId, int limit = 100, CancellationToken cancellationToken = default);
}
