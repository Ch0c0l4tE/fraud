namespace Fraud.Sdk.Contracts;

/// <summary>
/// Signal types captured by client SDKs
/// </summary>
public enum SignalType
{
    // Browser/Web signals
    MouseMove,
    MouseClick,
    Keystroke,
    KeystrokeDynamics,
    Scroll,
    Touch,
    Visibility,
    Focus,
    Paste,
    Device,
    Performance,
    Fingerprint,
    FormInteraction,
    
    // Mobile signals
    Accelerometer,
    Gyroscope,
    AppLifecycle,
    JailbreakDetection,
    RootDetection,
    
    // Unknown/custom
    Unknown
}

/// <summary>
/// A captured behavioral signal
/// </summary>
public record Signal
{
    public required string Id { get; init; }
    public required Guid SessionId { get; init; }
    public required SignalType Type { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required Dictionary<string, object?> Payload { get; init; }
}

/// <summary>
/// Session information
/// </summary>
public record Session
{
    public required Guid Id { get; init; }
    public required string ClientId { get; init; }
    public required string DeviceFingerprint { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Request to create a new session
/// </summary>
public record CreateSessionRequest
{
    public required string ClientId { get; init; }
    public required string DeviceFingerprint { get; init; }
    public Dictionary<string, object?>? Metadata { get; init; }
}

/// <summary>
/// Request to append signals to a session
/// </summary>
public record AppendSignalsRequest
{
    public required Guid SessionId { get; init; }
    public required List<SignalDto> Signals { get; init; }
}

/// <summary>
/// Signal data transfer object
/// </summary>
public record SignalDto
{
    public required string Type { get; init; }
    public required long Timestamp { get; init; }
    public required Dictionary<string, object?> Payload { get; init; }
}

/// <summary>
/// Fraud analysis result
/// </summary>
public record FraudAnalysis
{
    public required Guid SessionId { get; init; }
    public required FraudVerdict Verdict { get; init; }
    public required double ConfidenceScore { get; init; }
    public required List<RiskFactor> RiskFactors { get; init; }
    public required string ModelVersion { get; init; }
    public required DateTimeOffset EvaluatedAt { get; init; }
}

/// <summary>
/// Fraud verdict
/// </summary>
public enum FraudVerdict
{
    Allow,
    Review,
    Block
}

/// <summary>
/// A risk factor contributing to the fraud score
/// </summary>
public record RiskFactor
{
    public required string Name { get; init; }
    public required double Score { get; init; }
    public required double Weight { get; init; }
    public string? Description { get; init; }
}

// ─────────────────────────────────────────────────────────────
// Engine Interfaces (in Contracts to avoid circular references)
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Interface for evaluating sessions for fraud
/// </summary>
public interface IFraudEvaluator
{
    /// <summary>
    /// Evaluate a session and return fraud analysis
    /// </summary>
    Task<FraudAnalysis> EvaluateAsync(Session session, IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for rule-based fraud detection
/// </summary>
public interface IRuleEngine
{
    Task<IReadOnlyList<RiskFactor>> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for ML-based fraud scoring
/// </summary>
public interface IMLScorer
{
    Task<IReadOnlyList<RiskFactor>> ScoreAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default);
}
