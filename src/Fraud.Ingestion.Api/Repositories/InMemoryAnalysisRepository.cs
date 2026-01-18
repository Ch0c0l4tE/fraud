using System.Collections.Concurrent;
using Fraud.Sdk.Contracts;

namespace Fraud.Ingestion.Api.Repositories;

/// <summary>
/// In-memory implementation of IAnalysisRepository for development
/// </summary>
public sealed class InMemoryAnalysisRepository : IAnalysisRepository
{
    private readonly ConcurrentDictionary<Guid, FraudAnalysis> _analyses = new();

    public Task SaveAsync(FraudAnalysis analysis, CancellationToken cancellationToken = default)
    {
        _analyses.AddOrUpdate(analysis.SessionId, analysis, (_, _) => analysis);
        return Task.CompletedTask;
    }

    public Task<FraudAnalysis?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        _analyses.TryGetValue(sessionId, out var analysis);
        return Task.FromResult(analysis);
    }

    public Task<bool> ExistsAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_analyses.ContainsKey(sessionId));
    }
}
