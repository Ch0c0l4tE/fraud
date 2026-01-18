using System.Text.Json.Serialization;

namespace Fraud.Ingestion.Api.Models;

/// <summary>
/// Standard API response wrapper
/// </summary>
/// <typeparam name="T">The type of data being returned</typeparam>
public record ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public T? Data { get; init; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiError? Error { get; init; }

    [JsonPropertyName("meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ApiMeta? Meta { get; init; }

    public static ApiResponse<T> Ok(T data, ApiMeta? meta = null) => new()
    {
        Success = true,
        Data = data,
        Meta = meta
    };

    public static ApiResponse<T> Fail(string code, string message, Dictionary<string, string[]>? details = null) => new()
    {
        Success = false,
        Error = new ApiError
        {
            Code = code,
            Message = message,
            Details = details
        }
    };
}

/// <summary>
/// API error information
/// </summary>
public record ApiError
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Details { get; init; }
}

/// <summary>
/// API response metadata
/// </summary>
public record ApiMeta
{
    [JsonPropertyName("requestId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("rateLimit")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RateLimitInfo? RateLimit { get; init; }
}

/// <summary>
/// Rate limit information
/// </summary>
public record RateLimitInfo
{
    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("remaining")]
    public int Remaining { get; init; }

    [JsonPropertyName("resetAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ResetAt { get; init; }
}

/// <summary>
/// Session creation response
/// </summary>
public record SessionCreatedResponse
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Signals appended response
/// </summary>
public record SignalsAppendedResponse
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }

    [JsonPropertyName("signalsReceived")]
    public int SignalsReceived { get; init; }

    [JsonPropertyName("totalSignals")]
    public int TotalSignals { get; init; }
}

/// <summary>
/// Session completed response
/// </summary>
public record SessionCompletedResponse
{
    [JsonPropertyName("sessionId")]
    public Guid SessionId { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset CompletedAt { get; init; }

    [JsonPropertyName("signalCount")]
    public int SignalCount { get; init; }

    [JsonPropertyName("analysisAvailable")]
    public bool AnalysisAvailable { get; init; }
}
