using System.Collections.Concurrent;

namespace Fraud.Ingestion.Api.RateLimiting;

/// <summary>
/// Rate limiter for session-based request limiting
/// Uses sliding window algorithm for rate limiting
/// </summary>
public interface ISessionRateLimiter
{
    /// <summary>
    /// Check if a request is allowed for the given session
    /// </summary>
    Task<RateLimitResult> CheckAsync(Guid sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a rate limit check
/// </summary>
public record RateLimitResult
{
    public bool IsAllowed { get; init; }
    public int Remaining { get; init; }
    public int Limit { get; init; }
    public TimeSpan RetryAfter { get; init; }
}

/// <summary>
/// In-memory sliding window rate limiter
/// Limits requests per session to a configurable number per minute
/// </summary>
public sealed class InMemorySlidingWindowRateLimiter : ISessionRateLimiter
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<DateTimeOffset>> _requestTimestamps = new();
    private readonly int _maxRequestsPerMinute;
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    public InMemorySlidingWindowRateLimiter(int maxRequestsPerMinute = 100)
    {
        _maxRequestsPerMinute = maxRequestsPerMinute;
    }

    public Task<RateLimitResult> CheckAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now - _window;

        var timestamps = _requestTimestamps.GetOrAdd(sessionId, _ => new ConcurrentQueue<DateTimeOffset>());

        // Clean up old timestamps outside the window
        while (timestamps.TryPeek(out var oldest) && oldest < windowStart)
        {
            timestamps.TryDequeue(out _);
        }

        var currentCount = timestamps.Count;

        if (currentCount >= _maxRequestsPerMinute)
        {
            // Find when the oldest request in the window will expire
            if (timestamps.TryPeek(out var oldestInWindow))
            {
                var retryAfter = oldestInWindow.Add(_window) - now;
                return Task.FromResult(new RateLimitResult
                {
                    IsAllowed = false,
                    Remaining = 0,
                    Limit = _maxRequestsPerMinute,
                    RetryAfter = retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(1)
                });
            }
        }

        // Add current request timestamp
        timestamps.Enqueue(now);

        return Task.FromResult(new RateLimitResult
        {
            IsAllowed = true,
            Remaining = _maxRequestsPerMinute - currentCount - 1,
            Limit = _maxRequestsPerMinute,
            RetryAfter = TimeSpan.Zero
        });
    }
}

/// <summary>
/// Rate limiting configuration
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    
    public int MaxRequestsPerMinute { get; set; } = 100;
    public bool Enabled { get; set; } = true;
}
