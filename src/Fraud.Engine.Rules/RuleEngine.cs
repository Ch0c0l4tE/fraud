using Fraud.Sdk.Contracts;

namespace Fraud.Engine.Rules;

/// <summary>
/// Rule-based fraud detection engine (Phase 3)
/// </summary>
public class RuleEngine : IRuleEngine
{
    private readonly List<IFraudRule> _rules;

    public RuleEngine(IEnumerable<IFraudRule>? rules = null)
    {
        var rulesList = rules?.ToList();
        // Use default rules if no rules provided or if empty collection is provided
        _rules = (rulesList == null || rulesList.Count == 0) ? GetDefaultRules() : rulesList;
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
            // Velocity & Movement Rules
            new MouseVelocityRule(),
            new MousePatternRule(),
            
            // Keystroke Rules
            new KeystrokeDynamicsRule(),
            new TypingSpeedRule(),
            
            // Bot Detection Rules
            new BotSignatureRule(),
            new HeadlessBrowserRule(),
            
            // Behavioral Rules
            new FormInteractionRule(),
            new SessionPatternRule(),
            
            // Fingerprint Rules
            new FingerprintAnomalyRule(),
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
/// Helper methods for extracting values from signal payloads
/// Handles both direct values and JsonElement from System.Text.Json
/// </summary>
internal static class PayloadHelper
{
    public static string? GetString(Dictionary<string, object?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is string s)
            return s;

        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.String)
                return je.GetString();
            return je.ToString();
        }

        return value.ToString();
    }

    public static double GetDouble(Dictionary<string, object?> payload, string key, double defaultValue = 0)
    {
        if (!payload.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is double d)
            return d;

        if (value is int i)
            return i;

        if (value is long l)
            return l;

        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.Number)
                return je.GetDouble();
            if (double.TryParse(je.ToString(), out var parsed))
                return parsed;
        }

        if (double.TryParse(value.ToString(), out var result))
            return result;

        return defaultValue;
    }

    public static int GetInt(Dictionary<string, object?> payload, string key, int defaultValue = 0)
    {
        return (int)GetDouble(payload, key, defaultValue);
    }

    public static bool GetBool(Dictionary<string, object?> payload, string key, bool defaultValue = false)
    {
        if (!payload.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is bool b)
            return b;

        if (value is System.Text.Json.JsonElement je)
        {
            if (je.ValueKind == System.Text.Json.JsonValueKind.True)
                return true;
            if (je.ValueKind == System.Text.Json.JsonValueKind.False)
                return false;
        }

        var str = value.ToString()?.ToLowerInvariant();
        return str == "true" || str == "1";
    }
}

// ─────────────────────────────────────────────────────────────
// Velocity & Movement Rules
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Detects abnormal mouse velocity patterns indicating automation
/// </summary>
public class MouseVelocityRule : IFraudRule
{
    public string Name => "mouse_velocity_anomaly";

    // Human mouse velocity typically ranges 0.1-30 px/ms
    private const double MaxHumanVelocity = 35.0;
    private const double SuspiciousMaxVelocity = 50.0;
    
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
        var stdDev = Math.Sqrt(velocities.Select(v => Math.Pow(v - avgVelocity, 2)).Average());

        var score = 0.0;
        var reasons = new List<string>();

        // Inhumanly fast mouse movement
        if (maxVelocity > SuspiciousMaxVelocity)
        {
            score = Math.Max(score, Math.Min(0.9, 0.5 + (maxVelocity - SuspiciousMaxVelocity) / 100));
            reasons.Add($"Extreme velocity: {maxVelocity:F1}px/ms");
        }
        else if (maxVelocity > MaxHumanVelocity)
        {
            score = Math.Max(score, 0.3);
            reasons.Add($"High velocity: {maxVelocity:F1}px/ms");
        }

        // Too consistent velocity (robotic movement)
        var coefficientOfVariation = avgVelocity > 0 ? stdDev / avgVelocity : 0;
        if (coefficientOfVariation < 0.1 && mouseSignals.Count > 50)
        {
            score = Math.Max(score, 0.6);
            reasons.Add($"Robotic consistency: CV={coefficientOfVariation:F3}");
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.15,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

/// <summary>
/// Analyzes mouse movement patterns for bot-like behavior
/// </summary>
public class MousePatternRule : IFraudRule
{
    public string Name => "mouse_pattern_anomaly";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var mouseSignals = signals
            .Where(s => s.Type == SignalType.MouseMove)
            .OrderBy(s => s.Timestamp)
            .ToList();

        if (mouseSignals.Count < 20)
            return Task.FromResult<RiskFactor?>(null);

        var score = 0.0;
        var reasons = new List<string>();

        // Extract positions
        var positions = mouseSignals
            .Select(s => (
                X: s.Payload.TryGetValue("x", out var x) ? Convert.ToDouble(x) : 0,
                Y: s.Payload.TryGetValue("y", out var y) ? Convert.ToDouble(y) : 0
            ))
            .ToList();

        // Check for straight-line movements (bots often move in straight lines)
        var straightLineCount = 0;
        for (int i = 2; i < positions.Count; i++)
        {
            var (x1, y1) = positions[i - 2];
            var (x2, y2) = positions[i - 1];
            var (x3, y3) = positions[i];
            
            // Calculate if three points are collinear
            var crossProduct = (y2 - y1) * (x3 - x2) - (y3 - y2) * (x2 - x1);
            if (Math.Abs(crossProduct) < 1.0) // Nearly straight
                straightLineCount++;
        }

        var straightLineRatio = (double)straightLineCount / (positions.Count - 2);
        if (straightLineRatio > 0.8)
        {
            score = Math.Max(score, 0.7);
            reasons.Add($"Too many straight-line movements: {straightLineRatio:P0}");
        }

        // Check for grid-like movement (snapping to coordinates)
        var gridSnapCount = positions.Count(p => 
            Math.Abs(p.X % 10) < 1 && Math.Abs(p.Y % 10) < 1);
        var gridSnapRatio = (double)gridSnapCount / positions.Count;
        if (gridSnapRatio > 0.5)
        {
            score = Math.Max(score, 0.5);
            reasons.Add($"Grid-snapping detected: {gridSnapRatio:P0}");
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.1,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

// ─────────────────────────────────────────────────────────────
// Keystroke Rules
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Analyzes keystroke timing patterns for bot detection
/// </summary>
public class KeystrokeDynamicsRule : IFraudRule
{
    public string Name => "keystroke_dynamics_anomaly";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var keystrokeSignals = signals
            .Where(s => s.Type == SignalType.KeystrokeDynamics)
            .ToList();

        if (keystrokeSignals.Count < 5)
            return Task.FromResult<RiskFactor?>(null);

        var dwellTimes = keystrokeSignals
            .Select(s => s.Payload.TryGetValue("dwellTimeMs", out var v) ? Convert.ToDouble(v) : 0.0)
            .Where(d => d > 0)
            .ToList();

        var flightTimes = keystrokeSignals
            .Select(s => s.Payload.TryGetValue("flightTimeMs", out var v) ? Convert.ToDouble(v) : 0.0)
            .Where(f => f > 0)
            .ToList();

        if (dwellTimes.Count == 0)
            return Task.FromResult<RiskFactor?>(null);

        var avgDwell = dwellTimes.Average();
        var dwellStdDev = dwellTimes.Count > 1 
            ? Math.Sqrt(dwellTimes.Select(d => Math.Pow(d - avgDwell, 2)).Average())
            : 0;
        
        var avgFlight = flightTimes.Count > 0 ? flightTimes.Average() : 0;

        var score = 0.0;
        var reasons = new List<string>();

        // Inhumanly fast typing (dwell time < 20ms is nearly impossible)
        if (avgDwell < 20)
        {
            score = Math.Max(score, 0.9);
            reasons.Add($"Inhuman typing speed: {avgDwell:F1}ms avg dwell");
        }
        else if (avgDwell < 40)
        {
            score = Math.Max(score, 0.5);
            reasons.Add($"Suspiciously fast typing: {avgDwell:F1}ms avg dwell");
        }

        // Too consistent timing (robotic)
        if (dwellStdDev < 3 && keystrokeSignals.Count > 20)
        {
            score = Math.Max(score, 0.8);
            reasons.Add($"Robotic consistency: stdDev={dwellStdDev:F2}ms");
        }
        else if (dwellStdDev < 8 && keystrokeSignals.Count > 30)
        {
            score = Math.Max(score, 0.5);
            reasons.Add($"Low variance in timing: stdDev={dwellStdDev:F2}ms");
        }

        // Very fast flight times between keys
        if (avgFlight > 0 && avgFlight < 30 && flightTimes.Count > 10)
        {
            score = Math.Max(score, 0.6);
            reasons.Add($"Rapid key transitions: {avgFlight:F1}ms avg flight");
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.2,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

/// <summary>
/// Detects abnormal typing speeds (WPM)
/// </summary>
public class TypingSpeedRule : IFraudRule
{
    public string Name => "typing_speed_anomaly";
    
    // World record is ~200 WPM, professional typists ~80-100 WPM
    private const double MaxReasonableWpm = 150;
    private const double SuspiciousWpm = 120;

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var keystrokeSignals = signals
            .Where(s => s.Type == SignalType.KeystrokeDynamics)
            .ToList();

        // Try to extract typing stats from keystroke_dynamics signals
        var statsSignal = keystrokeSignals.FirstOrDefault(s => 
            s.Payload.TryGetValue("estimatedWpm", out _));

        if (statsSignal == null)
            return Task.FromResult<RiskFactor?>(null);

        if (!statsSignal.Payload.TryGetValue("estimatedWpm", out var wpmVal))
            return Task.FromResult<RiskFactor?>(null);

        var wpm = Convert.ToDouble(wpmVal);
        
        var score = 0.0;
        string? reason = null;

        if (wpm > MaxReasonableWpm)
        {
            score = Math.Min(0.95, 0.6 + (wpm - MaxReasonableWpm) / 200);
            reason = $"Superhuman typing speed: {wpm:F0} WPM";
        }
        else if (wpm > SuspiciousWpm)
        {
            score = 0.3 + (wpm - SuspiciousWpm) / 100;
            reason = $"Very fast typing: {wpm:F0} WPM";
        }

        if (score > 0 && reason != null)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.15,
                Description = reason
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

// ─────────────────────────────────────────────────────────────
// Bot Detection Rules
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Detects known bot/automation tool signatures
/// </summary>
public class BotSignatureRule : IFraudRule
{
    public string Name => "bot_signature_detected";

    private static readonly HashSet<string> BotSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        "HeadlessChrome",
        "PhantomJS",
        "Selenium",
        "WebDriver",
        "Puppeteer",
        "Playwright",
        "Nightmare",
        "CasperJS",
        "SlimerJS",
        "Zombie",
        "HtmlUnit"
    };

    private static readonly HashSet<string> SuspiciousPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "bot",
        "crawler",
        "spider",
        "scraper",
        "automation"
    };

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var deviceSignal = signals.FirstOrDefault(s => s.Type == SignalType.Device);
        
        if (deviceSignal == null)
            return Task.FromResult<RiskFactor?>(null);

        var score = 0.0;
        var reasons = new List<string>();

        // Check user agent using PayloadHelper for JsonElement handling
        var userAgent = PayloadHelper.GetString(deviceSignal.Payload, "userAgent");
        if (!string.IsNullOrEmpty(userAgent))
        {
            // Check for direct bot signatures
            foreach (var signature in BotSignatures)
            {
                if (userAgent.Contains(signature, StringComparison.OrdinalIgnoreCase))
                {
                    score = 0.95;
                    reasons.Add($"Known automation tool: {signature}");
                    break;
                }
            }

            // Check for suspicious patterns
            if (score < 0.9)
            {
                foreach (var pattern in SuspiciousPatterns)
                {
                    if (userAgent.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        score = Math.Max(score, 0.7);
                        reasons.Add($"Suspicious user agent pattern: {pattern}");
                    }
                }
            }
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.25,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

/// <summary>
/// Detects headless browser characteristics
/// </summary>
public class HeadlessBrowserRule : IFraudRule
{
    public string Name => "headless_browser_detected";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var fingerprintSignal = signals.FirstOrDefault(s => s.Type == SignalType.Fingerprint);
        var deviceSignal = signals.FirstOrDefault(s => s.Type == SignalType.Device);

        if (fingerprintSignal == null && deviceSignal == null)
            return Task.FromResult<RiskFactor?>(null);

        var score = 0.0;
        var reasons = new List<string>();

        // Check fingerprint for headless indicators
        if (fingerprintSignal != null)
        {
            // Missing or empty canvas fingerprint
            if (fingerprintSignal.Payload.TryGetValue("canvas", out var canvas))
            {
                var canvasStr = canvas?.ToString() ?? "";
                if (string.IsNullOrEmpty(canvasStr) || canvasStr == "0" || canvasStr.Length < 8)
                {
                    score = Math.Max(score, 0.6);
                    reasons.Add("Missing/invalid canvas fingerprint");
                }
            }

            // Missing or empty WebGL fingerprint
            if (fingerprintSignal.Payload.TryGetValue("webgl", out var webgl))
            {
                var webglStr = webgl?.ToString() ?? "";
                if (string.IsNullOrEmpty(webglStr) || webglStr == "0")
                {
                    score = Math.Max(score, 0.5);
                    reasons.Add("Missing WebGL fingerprint");
                }
                
                // Check for suspicious renderer
                if (fingerprintSignal.Payload.TryGetValue("webglRenderer", out var renderer))
                {
                    var rendererStr = renderer?.ToString() ?? "";
                    if (rendererStr.Contains("SwiftShader", StringComparison.OrdinalIgnoreCase) ||
                        rendererStr.Contains("Mesa", StringComparison.OrdinalIgnoreCase) && 
                        rendererStr.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase))
                    {
                        score = Math.Max(score, 0.7);
                        reasons.Add($"Software renderer detected: {rendererStr}");
                    }
                }
            }

            // Missing audio fingerprint
            if (fingerprintSignal.Payload.TryGetValue("audio", out var audio))
            {
                var audioStr = audio?.ToString() ?? "";
                if (string.IsNullOrEmpty(audioStr) || audioStr == "0")
                {
                    score = Math.Max(score, 0.4);
                    reasons.Add("Missing audio fingerprint");
                }
            }
        }

        // Check device info for headless indicators
        if (deviceSignal != null)
        {
            // Check for webdriver property
            var webdriver = PayloadHelper.GetBool(deviceSignal.Payload, "webdriver");
            if (webdriver == true)
            {
                score = 0.95;
                reasons.Add("navigator.webdriver is true");
            }

            // Missing plugins
            var pluginCount = PayloadHelper.GetInt(deviceSignal.Payload, "pluginCount");
            if (pluginCount == 0)
            {
                score = Math.Max(score, 0.5);
                reasons.Add("No browser plugins detected");
            }
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.2,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

// ─────────────────────────────────────────────────────────────
// Behavioral Rules
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Analyzes form interaction patterns
/// </summary>
public class FormInteractionRule : IFraudRule
{
    public string Name => "form_interaction_anomaly";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var formSignals = signals.Where(s => s.Type == SignalType.FormInteraction).ToList();

        if (formSignals.Count == 0)
            return Task.FromResult<RiskFactor?>(null);

        var score = 0.0;
        var reasons = new List<string>();

        // Extract form timing data
        var timeToFillValues = formSignals
            .Select(s => s.Payload.TryGetValue("timeToFill", out var v) ? Convert.ToDouble(v) : 0)
            .Where(t => t > 0)
            .ToList();

        var correctionCounts = formSignals
            .Select(s => s.Payload.TryGetValue("corrections", out var v) ? Convert.ToInt32(v) : 0)
            .ToList();

        var pasteDetected = formSignals.Any(s => 
            s.Payload.TryGetValue("pasteDetected", out var v) && (v is true || v?.ToString() == "true"));

        if (timeToFillValues.Count > 0)
        {
            var avgTimeToFill = timeToFillValues.Average();
            var minTimeToFill = timeToFillValues.Min();

            // Inhumanly fast form fill (< 500ms per field)
            if (minTimeToFill < 300)
            {
                score = Math.Max(score, 0.85);
                reasons.Add($"Inhuman form speed: {minTimeToFill:F0}ms min");
            }
            else if (avgTimeToFill < 500)
            {
                score = Math.Max(score, 0.6);
                reasons.Add($"Very fast form fill: {avgTimeToFill:F0}ms avg");
            }
        }

        // Zero corrections across many fields is suspicious
        if (correctionCounts.Count > 3 && correctionCounts.All(c => c == 0))
        {
            score = Math.Max(score, 0.4);
            reasons.Add("No typing corrections across all fields");
        }

        // Paste in all fields is suspicious
        if (pasteDetected && formSignals.Count > 2)
        {
            var pasteCount = formSignals.Count(s => 
                s.Payload.TryGetValue("pasteDetected", out var v) && (v is true || v?.ToString() == "true"));
            
            if (pasteCount == formSignals.Count)
            {
                score = Math.Max(score, 0.5);
                reasons.Add("All fields filled via paste");
            }
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.15,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

/// <summary>
/// Analyzes overall session patterns for anomalies
/// </summary>
public class SessionPatternRule : IFraudRule
{
    public string Name => "session_pattern_anomaly";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        if (signals.Count == 0)
            return Task.FromResult<RiskFactor?>(null);

        var score = 0.0;
        var reasons = new List<string>();

        // Check signal distribution
        var signalsByType = signals.GroupBy(s => s.Type).ToDictionary(g => g.Key, g => g.Count());

        // Missing expected signals (a real user should have mouse + keyboard)
        var hasMouseEvents = signalsByType.ContainsKey(SignalType.MouseMove) || 
                            signalsByType.ContainsKey(SignalType.MouseClick);
        var hasKeyboardEvents = signalsByType.ContainsKey(SignalType.Keystroke) || 
                               signalsByType.ContainsKey(SignalType.KeystrokeDynamics);
        var hasFingerprint = signalsByType.ContainsKey(SignalType.Fingerprint);
        var hasDevice = signalsByType.ContainsKey(SignalType.Device);

        if (!hasDevice || !hasFingerprint)
        {
            score = Math.Max(score, 0.7);
            reasons.Add("Missing device/fingerprint signals");
        }

        if (signals.Count > 10 && !hasMouseEvents)
        {
            score = Math.Max(score, 0.4);
            reasons.Add("No mouse activity detected");
        }

        // Very short session with action (suspicious)
        var sessionDuration = signals.Count > 1
            ? (signals.Max(s => s.Timestamp) - signals.Min(s => s.Timestamp)).TotalMilliseconds
            : 0;

        if (sessionDuration < 1000 && signals.Count > 20)
        {
            score = Math.Max(score, 0.8);
            reasons.Add($"Rapid session: {signals.Count} signals in {sessionDuration:F0}ms");
        }

        // Signal rate anomaly (too many signals per second)
        var signalRate = sessionDuration > 0 ? signals.Count / (sessionDuration / 1000.0) : 0;
        if (signalRate > 50)
        {
            score = Math.Max(score, 0.6);
            reasons.Add($"High signal rate: {signalRate:F1}/sec");
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.1,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}

// ─────────────────────────────────────────────────────────────
// Fingerprint Rules
// ─────────────────────────────────────────────────────────────

/// <summary>
/// Detects fingerprint anomalies and spoofing attempts
/// </summary>
public class FingerprintAnomalyRule : IFraudRule
{
    public string Name => "fingerprint_anomaly";

    public Task<RiskFactor?> EvaluateAsync(IReadOnlyList<Signal> signals, CancellationToken cancellationToken = default)
    {
        var fingerprintSignal = signals.FirstOrDefault(s => s.Type == SignalType.Fingerprint);
        var deviceSignal = signals.FirstOrDefault(s => s.Type == SignalType.Device);

        if (fingerprintSignal == null || deviceSignal == null)
            return Task.FromResult<RiskFactor?>(null);

        var score = 0.0;
        var reasons = new List<string>();

        // Check for timezone mismatch
        if (fingerprintSignal.Payload.TryGetValue("timezoneOffset", out var fpOffset) &&
            deviceSignal.Payload.TryGetValue("timezoneOffset", out var devOffset))
        {
            var fpOffsetVal = Convert.ToInt32(fpOffset);
            var devOffsetVal = Convert.ToInt32(devOffset);
            
            if (Math.Abs(fpOffsetVal - devOffsetVal) > 60)
            {
                score = Math.Max(score, 0.6);
                reasons.Add($"Timezone offset mismatch: {fpOffsetVal} vs {devOffsetVal}");
            }
        }

        // Check for screen resolution anomalies
        if (deviceSignal.Payload.TryGetValue("screenWidth", out var width) &&
            deviceSignal.Payload.TryGetValue("screenHeight", out var height))
        {
            var w = Convert.ToInt32(width);
            var h = Convert.ToInt32(height);
            
            // Very unusual resolutions
            if (w == 0 || h == 0)
            {
                score = Math.Max(score, 0.7);
                reasons.Add("Invalid screen resolution");
            }
            else if ((w == 800 && h == 600) || (w == 1 && h == 1))
            {
                score = Math.Max(score, 0.5);
                reasons.Add($"Suspicious resolution: {w}x{h}");
            }
        }

        // Check for language mismatch
        if (fingerprintSignal.Payload.TryGetValue("languages", out var languages) &&
            deviceSignal.Payload.TryGetValue("language", out var language))
        {
            var langStr = language?.ToString() ?? "";
            var langsStr = languages?.ToString() ?? "";
            
            if (!string.IsNullOrEmpty(langStr) && !string.IsNullOrEmpty(langsStr) &&
                !langsStr.Contains(langStr.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 0.4);
                reasons.Add("Language configuration mismatch");
            }
        }

        if (score > 0)
        {
            return Task.FromResult<RiskFactor?>(new RiskFactor
            {
                Name = Name,
                Score = score,
                Weight = 0.1,
                Description = string.Join("; ", reasons)
            });
        }

        return Task.FromResult<RiskFactor?>(null);
    }
}
