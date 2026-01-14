using Fraud.Sdk.Contracts;

namespace Fraud.Engine.Rules;

/// <summary>
/// Rule-based fraud detection engine (Phase 3a)
/// </summary>
public class RuleEngine : IRuleEngine
{
    private readonly List<IFraudRule> _rules;

    public RuleEngine(IEnumerable<IFraudRule>? rules = null)
    {
        _rules = rules?.ToList() ?? GetDefaultRules();
    }

    public async Task<IReadOnlyList<RiskFactor>> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var results = new List<RiskFactor>();

        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await rule.EvaluateAsync(signals, cancellationToken);
            if (result != null)
            {
                results.Add(result);
            }
        }

        return results;
    }

    private static List<IFraudRule> GetDefaultRules()
    {
        return
        [
            new MouseVelocityRule(),
            new KeystrokeDynamicsRule(),
            new DeviceFingerprintRule()
        ];
    }
}

/// <summary>
/// Interface for individual fraud detection rules
/// </summary>
public interface IFraudRule
{
    string Name { get; }
    Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default);
}

/// <summary>
/// Detects abnormal mouse velocity patterns
/// </summary>
public class MouseVelocityRule : IFraudRule
{
    public string Name => "mouse_velocity_anomaly";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var mouseSignals = signals.Where(s => s.Type == SignalType.MouseMove).ToList();
        
        if (mouseSignals.Count < 10)
            return Task.FromResult<RiskFactor?>(null);

        var velocities = mouseSignals
            .Select(s => s.Payload.TryGetValue("velocity", out var v) ? Convert.ToDouble(v) : 0.0)
            .Where(v => v > 0)
            .ToList();

        if (velocities.Count == 0)
            return Task.FromResult<RiskFactor?>(null);

        var avgVelocity = velocities.Average();
        var maxVelocity = velocities.Max();

        // Abnormally high velocity could indicate automation
        var score = maxVelocity > 50.0 ? Math.Min(1.0, maxVelocity / 100.0) : 0.0;

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.25,
                Description = $"Abnormal mouse velocity detected: max={maxVelocity:F2}, avg={avgVelocity:F2}"
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

/// <summary>
/// Analyzes keystroke timing patterns
/// </summary>
public class KeystrokeDynamicsRule : IFraudRule
{
    public string Name => "keystroke_dynamics_anomaly";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var keystrokeSignals = signals.Where(s => s.Type == SignalType.Keystroke).ToList();
        
        if (keystrokeSignals.Count < 5)
            return Task.FromResult<RiskFactor?>(null);

        var dwellTimes = keystrokeSignals
            .Select(s => s.Payload.TryGetValue("dwellTimeMs", out var v) ? Convert.ToDouble(v) : 0.0)
            .Where(d => d > 0)
            .ToList();

        if (dwellTimes.Count == 0)
            return Task.FromResult<RiskFactor?>(null);

        var avgDwell = dwellTimes.Average();
        var stdDev = Math.Sqrt(dwellTimes.Select(d => Math.Pow(d - avgDwell, 2)).Average());

        // Too consistent (low variance) suggests automation
        // Too fast (low dwell time) suggests bots
        var score = 0.0;
        
        if (avgDwell < 20) // Inhumanly fast
            score = 0.9;
        else if (stdDev < 5 && keystrokeSignals.Count > 20) // Too consistent
            score = 0.7;

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.3,
                Description = $"Suspicious keystroke pattern: avgDwell={avgDwell:F2}ms, stdDev={stdDev:F2}"
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

/// <summary>
/// Checks for suspicious device fingerprint patterns
/// </summary>
public class DeviceFingerprintRule : IFraudRule
{
    public string Name => "device_fingerprint_anomaly";

    private static readonly HashSet<string> SuspiciousSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "HeadlessChrome",
        "PhantomJS",
        "Selenium",
        "WebDriver",
        "Puppeteer"
    };

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var deviceSignal = signals.FirstOrDefault(s => s.Type == SignalType.Device);
        
        if (deviceSignal == null)
            return Task.FromResult<RiskFactor?>(null);

        var score = 0.0;
        var reasons = new List<string>();

        // Check user agent
        if (deviceSignal.Payload.TryGetValue("userAgent", out var ua) && ua is string userAgent)
        {
            foreach (var signature in SuspiciousSignatures)
            {
                if (userAgent.Contains(signature, StringComparison.OrdinalIgnoreCase))
                {
                    score = Math.Max(score, 0.95);
                    reasons.Add($"Automation signature detected: {signature}");
                }
            }
        }

        // Check for missing properties that real browsers have
        if (!deviceSignal.Payload.ContainsKey("language") || deviceSignal.Payload["language"] == null)
        {
            score = Math.Max(score, 0.5);
            reasons.Add("Missing language property");
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.35,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}
