using System.Collections.Concurrent;
using Fraud.Sdk.Contracts;

namespace Fraud.Ingestion.Api.Repositories;

/// <summary>
/// In-memory implementation of ISignalRepository for development
/// </summary>
public sealed class InMemorySignalRepository : ISignalRepository
{
    private readonly ConcurrentDictionary<Guid, ConcurrentBag<Signal>> _signals = new();

    public Task AppendAsync(Guid sessionId, IEnumerable<Signal> signals, CancellationToken cancellationToken = default)
    {
        var bag = _signals.GetOrAdd(sessionId, _ => new ConcurrentBag<Signal>());
        
        foreach (var signal in signals)
        {
            bag.Add(signal);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Signal>> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_signals.TryGetValue(sessionId, out var signals))
        {
            return Task.FromResult<IReadOnlyList<Signal>>(Array.Empty<Signal>());
        }

        var sortedSignals = signals
            .OrderBy(s => s.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<Signal>>(sortedSignals);
    }

    public Task<int> GetCountBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        if (!_signals.TryGetValue(sessionId, out var signals))
        {
            return Task.FromResult(0);
        }

        return Task.FromResult(signals.Count);
    }

    public Task<IReadOnlyList<Signal>> GetBySessionIdAndTypeAsync(Guid sessionId, SignalType type, CancellationToken cancellationToken = default)
    {
        if (!_signals.TryGetValue(sessionId, out var signals))
        {
            return Task.FromResult<IReadOnlyList<Signal>>(Array.Empty<Signal>());
        }

        var filteredSignals = signals
            .Where(s => s.Type == type)
            .OrderBy(s => s.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<Signal>>(filteredSignals);
    }

    public Task<IReadOnlyList<Signal>> GetBySessionIdAndTimeRangeAsync(
        Guid sessionId, 
        DateTimeOffset start, 
        DateTimeOffset end, 
        CancellationToken cancellationToken = default)
    {
        if (!_signals.TryGetValue(sessionId, out var signals))
        {
            return Task.FromResult<IReadOnlyList<Signal>>(Array.Empty<Signal>());
        }

        var filteredSignals = signals
            .Where(s => s.Timestamp >= start && s.Timestamp <= end)
            .OrderBy(s => s.Timestamp)
            .ToList();

        return Task.FromResult<IReadOnlyList<Signal>>(filteredSignals);
    }
}
