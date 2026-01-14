using System.Collections.Concurrent;
using Fraud.Engine;
using Fraud.Engine.ML;
using Fraud.Engine.Rules;
using Fraud.Sdk.Contracts;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Register fraud engine (using mock ML scorer for development)
builder.Services.AddSingleton<IRuleEngine, RuleEngine>();
builder.Services.AddSingleton<IMLScorer, MockMLScorer>();
builder.Services.AddSingleton<IFraudEvaluator>(sp => 
    new FraudEvaluator(
        sp.GetRequiredService<IRuleEngine>(),
        sp.GetRequiredService<IMLScorer>(),
        "1.0.0-dev"
    ));

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// In-memory storage for development (replace with database in production)
var sessions = new ConcurrentDictionary<Guid, Session>();
var signals = new ConcurrentDictionary<Guid, ConcurrentBag<Signal>>();
var analyses = new ConcurrentDictionary<Guid, FraudAnalysis>();

// ─────────────────────────────────────────────────────────────
// Session Endpoints
// ─────────────────────────────────────────────────────────────

app.MapPost("/api/v1/sessions", (CreateSessionRequest request) =>
{
    var session = new Session
    {
        Id = Guid.NewGuid(),
        ClientId = request.ClientId,
        DeviceFingerprint = request.DeviceFingerprint,
        CreatedAt = DateTimeOffset.UtcNow,
        Metadata = request.Metadata
    };
    
    sessions[session.Id] = session;
    signals[session.Id] = new ConcurrentBag<Signal>();
    
    return Results.Created($"/api/v1/sessions/{session.Id}", new { sessionId = session.Id });
})
.WithName("CreateSession")
.WithTags("Sessions");

app.MapPost("/api/v1/sessions/{sessionId}/signals", (Guid sessionId, AppendSignalsRequest request) =>
{
    if (!sessions.ContainsKey(sessionId))
    {
        return Results.NotFound(new { error = "Session not found" });
    }
    
    if (!signals.TryGetValue(sessionId, out var sessionSignals))
    {
        sessionSignals = new ConcurrentBag<Signal>();
        signals[sessionId] = sessionSignals;
    }
    
    foreach (var signalDto in request.Signals)
    {
        var signal = new Signal
        {
            Id = Guid.NewGuid().ToString(),
            SessionId = sessionId,
            Type = Enum.TryParse<SignalType>(signalDto.Type, true, out var type) ? type : SignalType.Device,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(signalDto.Timestamp),
            Payload = signalDto.Payload
        };
        sessionSignals.Add(signal);
    }
    
    return Results.Accepted();
})
.WithName("AppendSignals")
.WithTags("Sessions");

app.MapPost("/api/v1/sessions/{sessionId}/complete", async (
    Guid sessionId, 
    IFraudEvaluator evaluator) =>
{
    if (!sessions.TryGetValue(sessionId, out var session))
    {
        return Results.NotFound(new { error = "Session not found" });
    }
    
    // Mark session complete
    var completedSession = session with { CompletedAt = DateTimeOffset.UtcNow };
    sessions[sessionId] = completedSession;
    
    // Trigger fraud analysis
    var sessionSignals = signals.TryGetValue(sessionId, out var sigs) 
        ? sigs.ToList() 
        : new List<Signal>();
    
    var analysis = await evaluator.EvaluateAsync(completedSession, sessionSignals);
    analyses[sessionId] = analysis;
    
    return Results.Ok(new { sessionId, status = "completed", analysis });
})
.WithName("CompleteSession")
.WithTags("Sessions");

// ─────────────────────────────────────────────────────────────
// Analysis Endpoints
// ─────────────────────────────────────────────────────────────

app.MapGet("/api/v1/sessions/{sessionId}/analysis", (Guid sessionId) =>
{
    if (!analyses.TryGetValue(sessionId, out var analysis))
    {
        if (!sessions.ContainsKey(sessionId))
        {
            return Results.NotFound(new { error = "Session not found" });
        }
        return Results.NotFound(new { error = "Analysis not yet available. Complete the session first." });
    }
    
    return Results.Ok(analysis);
})
.WithName("GetAnalysis")
.WithTags("Analysis");

app.MapPost("/api/v1/analyze", async (
    AppendSignalsRequest request,
    IFraudEvaluator evaluator) =>
{
    // Synchronous analysis - create temporary session, evaluate, return result
    var session = new Session
    {
        Id = Guid.NewGuid(),
        ClientId = "anonymous",
        DeviceFingerprint = "inline-analysis",
        CreatedAt = DateTimeOffset.UtcNow
    };
    
    var signalList = request.Signals.Select(dto => new Signal
    {
        Id = Guid.NewGuid().ToString(),
        SessionId = session.Id,
        Type = Enum.TryParse<SignalType>(dto.Type, true, out var type) ? type : SignalType.Device,
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dto.Timestamp),
        Payload = dto.Payload
    }).ToList();
    
    var analysis = await evaluator.EvaluateAsync(session, signalList);
    return Results.Ok(analysis);
})
.WithName("AnalyzeInline")
.WithTags("Analysis");

// ─────────────────────────────────────────────────────────────
// Health Check
// ─────────────────────────────────────────────────────────────

app.MapGet("/api/v1/health", () => Results.Ok(new 
{ 
    status = "healthy", 
    timestamp = DateTimeOffset.UtcNow,
    version = "1.0.0-dev"
}))
.WithName("HealthCheck")
.WithTags("Health");

app.Run();
