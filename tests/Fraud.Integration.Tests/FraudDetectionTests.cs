using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Fraud.Integration.Tests;

/// <summary>
/// Integration tests for the fraud detection API endpoints.
/// Tests the complete flow: session creation → signal submission → completion → analysis.
/// </summary>
public class FraudDetectionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FraudDetectionTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // ─────────────────────────────────────────────────────────────
    // Session Lifecycle Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_ReturnsSessionId()
    {
        // Arrange
        var request = new { clientId = "test-client", deviceFingerprint = "test-fp" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/sessions", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ApiResponse<SessionCreatedData>>(_jsonOptions);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.NotEqual(Guid.Empty, content.Data!.SessionId);
    }

    [Fact]
    public async Task CompleteSession_TriggersAnalysis()
    {
        // Arrange - Create session
        var sessionId = await CreateSessionAsync();

        // Act - Complete session
        var response = await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadFromJsonAsync<ApiResponse<SessionCompletedData>>(_jsonOptions);
        Assert.NotNull(content);
        Assert.True(content.Success);
        Assert.True(content.Data!.AnalysisAvailable);
    }

    // ─────────────────────────────────────────────────────────────
    // Bot Detection Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BotDetection_HeadlessChrome_FlagsAsReview()
    {
        // Arrange
        var sessionId = await CreateSessionAsync();

        // Send bot-like signals with HeadlessChrome
        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 HeadlessChrome/120.0.0.0 Safari/537.36",
                    ["webdriver"] = true,
                    ["pluginCount"] = 0
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.NotNull(analysis);
        Assert.True(analysis.Data!.ConfidenceScore > 0.3, "HeadlessChrome should trigger elevated score");
        Assert.Contains(analysis.Data.RiskFactors, r => r.Name == "bot_signature_detected");
    }

    [Fact]
    public async Task BotDetection_Puppeteer_FlagsAsReview()
    {
        // Arrange
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 Puppeteer/21.0.0",
                    ["webdriver"] = true,
                    ["pluginCount"] = 0
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.Contains(analysis.Data!.RiskFactors, r => 
            r.Name == "bot_signature_detected" && 
            r.Description.Contains("Puppeteer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BotDetection_Selenium_FlagsAsReview()
    {
        // Arrange
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 (Windows) Selenium WebDriver",
                    ["webdriver"] = true,
                    ["pluginCount"] = 0
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.Contains(analysis.Data!.RiskFactors, r => r.Name == "bot_signature_detected");
    }

    // ─────────────────────────────────────────────────────────────
    // Headless Browser Detection Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HeadlessBrowser_SwiftShader_FlagsAsReview()
    {
        // Arrange
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "fingerprint",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["canvas"] = "",
                    ["webgl"] = "0",
                    ["webglRenderer"] = "SwiftShader"
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.Contains(analysis.Data!.RiskFactors, r => 
            r.Name == "headless_browser_detected" && 
            r.Description.Contains("SwiftShader"));
    }

    [Fact]
    public async Task HeadlessBrowser_WebdriverTrue_FlagsAsReview()
    {
        // Arrange
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                    ["webdriver"] = true,
                    ["pluginCount"] = 3
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.Contains(analysis.Data!.RiskFactors, r => 
            r.Name == "headless_browser_detected" && 
            r.Description.Contains("webdriver"));
    }

    [Fact]
    public async Task HeadlessBrowser_NoPlugins_FlagsAsReview()
    {
        // Arrange
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                    ["webdriver"] = false,
                    ["pluginCount"] = 0
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.Contains(analysis.Data!.RiskFactors, r => 
            r.Name == "headless_browser_detected" && 
            r.Description.Contains("No browser plugins"));
    }

    // ─────────────────────────────────────────────────────────────
    // Combined Bot Signals Test
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CombinedBotSignals_MultipleFlagsDetected()
    {
        // Arrange - Full bot scenario
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 HeadlessChrome/120.0.0.0",
                    ["webdriver"] = true,
                    ["pluginCount"] = 0
                }
            },
            new SignalDto
            {
                Type = "fingerprint",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 100,
                Payload = new Dictionary<string, object?>
                {
                    ["canvas"] = "",
                    ["webgl"] = "0",
                    ["webglRenderer"] = "SwiftShader"
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.True(analysis.Data!.ConfidenceScore >= 0.5, "Combined bot signals should produce high score");
        Assert.Contains(analysis.Data.RiskFactors, r => r.Name == "bot_signature_detected");
        Assert.Contains(analysis.Data.RiskFactors, r => r.Name == "headless_browser_detected");
    }

    // ─────────────────────────────────────────────────────────────
    // Normal User Tests (No False Positives)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NormalUser_ChromeWindows_NoRuleFlags()
    {
        // Arrange - Normal Chrome user
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                    ["webdriver"] = false,
                    ["pluginCount"] = 5
                }
            },
            new SignalDto
            {
                Type = "fingerprint",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 100,
                Payload = new Dictionary<string, object?>
                {
                    ["canvas"] = "abc123uniquehash",
                    ["webgl"] = "webgl-fingerprint-hash",
                    ["webglRenderer"] = "NVIDIA GeForce RTX 3080/PCIe/SSE2"
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert - Only ML mock score should appear, no rule-based detections
        Assert.DoesNotContain(analysis.Data!.RiskFactors, r => r.Name == "bot_signature_detected");
        Assert.DoesNotContain(analysis.Data.RiskFactors, r => r.Name == "headless_browser_detected");
    }

    [Fact]
    public async Task NormalUser_SafariMac_NoRuleFlags()
    {
        // Arrange - Normal Safari user
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_2) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
                    ["webdriver"] = false,
                    ["pluginCount"] = 3
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.DoesNotContain(analysis.Data!.RiskFactors, r => r.Name == "bot_signature_detected");
    }

    [Fact]
    public async Task NormalUser_FirefoxLinux_NoRuleFlags()
    {
        // Arrange - Normal Firefox user
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0",
                    ["webdriver"] = false,
                    ["pluginCount"] = 2
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert
        Assert.DoesNotContain(analysis.Data!.RiskFactors, r => r.Name == "bot_signature_detected");
    }

    // ─────────────────────────────────────────────────────────────
    // Verdict Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verdict_HighScore_ReturnsReviewOrBlock()
    {
        // Arrange - Maximum bot signals
        var sessionId = await CreateSessionAsync();

        await SendSignalsAsync(sessionId, new[]
        {
            new SignalDto
            {
                Type = "device",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Payload = new Dictionary<string, object?>
                {
                    ["userAgent"] = "Mozilla/5.0 HeadlessChrome Puppeteer",
                    ["webdriver"] = true,
                    ["pluginCount"] = 0
                }
            },
            new SignalDto
            {
                Type = "fingerprint",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 100,
                Payload = new Dictionary<string, object?>
                {
                    ["canvas"] = "",
                    ["webgl"] = "0",
                    ["webglRenderer"] = "SwiftShader"
                }
            }
        });

        await _client.PostAsync($"/api/v1/sessions/{sessionId}/complete", null);

        // Act
        var analysis = await GetAnalysisAsync(sessionId);

        // Assert - Should be Review (1) or Block (2), not Allow (0)
        Assert.True(analysis.Data!.Verdict >= 1, "High-risk session should be Review or Block");
    }

    // ─────────────────────────────────────────────────────────────
    // Helper Methods
    // ─────────────────────────────────────────────────────────────

    private async Task<Guid> CreateSessionAsync()
    {
        var request = new { clientId = "test-client", deviceFingerprint = "test-fp" };
        var response = await _client.PostAsJsonAsync("/api/v1/sessions", request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<ApiResponse<SessionCreatedData>>(_jsonOptions);
        return content!.Data!.SessionId;
    }

    private async Task SendSignalsAsync(Guid sessionId, SignalDto[] signals)
    {
        var request = new { sessionId, signals };
        var response = await _client.PostAsJsonAsync($"/api/v1/sessions/{sessionId}/signals", request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<ApiResponse<AnalysisData>> GetAnalysisAsync(Guid sessionId)
    {
        var response = await _client.GetAsync($"/api/v1/sessions/{sessionId}/analysis");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiResponse<AnalysisData>>(_jsonOptions))!;
    }

    // ─────────────────────────────────────────────────────────────
    // DTOs for JSON Deserialization
    // ─────────────────────────────────────────────────────────────

    private record ApiResponse<T>
    {
        public bool Success { get; init; }
        public T? Data { get; init; }
    }

    private record SessionCreatedData
    {
        public Guid SessionId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private record SessionCompletedData
    {
        public Guid SessionId { get; init; }
        public bool AnalysisAvailable { get; init; }
    }

    private record AnalysisData
    {
        public Guid SessionId { get; init; }
        public int Verdict { get; init; }
        public double ConfidenceScore { get; init; }
        public List<RiskFactorData> RiskFactors { get; init; } = new();
        public string ModelVersion { get; init; } = "";
    }

    private record RiskFactorData
    {
        public string Name { get; init; } = "";
        public double Score { get; init; }
        public double Weight { get; init; }
        public string Description { get; init; } = "";
    }

    private record SignalDto
    {
        public string Type { get; init; } = "";
        public long Timestamp { get; init; }
        public Dictionary<string, object?> Payload { get; init; } = new();
    }
}
