using Fraud.Sdk.Contracts;

namespace Fraud.Ingestion.Api.Repositories;

/// <summary>
/// Repository for analysis result storage and retrieval
/// </summary>
public interface IAnalysisRepository
{
    /// <summary>
    /// Store an analysis result
    /// </summary>
    Task SaveAsync(FraudAnalysis analysis, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get analysis for a session
    /// </summary>
    Task<FraudAnalysis?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Check if analysis exists for a session
    /// </summary>
    Task<bool> ExistsAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
