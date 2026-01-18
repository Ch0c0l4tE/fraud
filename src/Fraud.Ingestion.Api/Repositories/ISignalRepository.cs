using Fraud.Sdk.Contracts;

namespace Fraud.Ingestion.Api.Repositories;

/// <summary>
/// Repository for signal storage and retrieval
/// </summary>
public interface ISignalRepository
{
    /// <summary>
    /// Append signals to a session
    /// </summary>
    Task AppendAsync(Guid sessionId, IEnumerable<Signal> signals, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get all signals for a session
    /// </summary>
    Task<IReadOnlyList<Signal>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get signal count for a session
    /// </summary>
    Task<int> GetCountBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get signals by session ID and type
    /// </summary>
    Task<IReadOnlyList<Signal>> GetBySessionIdAndTypeAsync(Guid sessionId, SignalType type, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get signals within a time range
    /// </summary>
    Task<IReadOnlyList<Signal>> GetBySessionIdAndTimeRangeAsync(
        Guid sessionId, 
        DateTimeOffset start, 
        DateTimeOffset end, 
        CancellationToken cancellationToken = default);
}
