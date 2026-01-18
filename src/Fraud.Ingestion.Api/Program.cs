using FluentValidation;
using Fraud.Engine;
using Fraud.Engine.ML;
using Fraud.Engine.Rules;
using Fraud.Ingestion.Api.Models;
using Fraud.Ingestion.Api.RateLimiting;
using Fraud.Ingestion.Api.Repositories;
using Fraud.Ingestion.Api.Validators;
using Fraud.Sdk.Contracts;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
// Service Registration
// ─────────────────────────────────────────────────────────────

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

// Repositories (in-memory for development)
builder.Services.AddSingleton<ISessionRepository, InMemorySessionRepository>();
builder.Services.AddSingleton<ISignalRepository, InMemorySignalRepository>();
builder.Services.AddSingleton<IAnalysisRepository, InMemoryAnalysisRepository>();

// Rate limiting
var rateLimitConfig = builder.Configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>() 
    ?? new RateLimitingOptions();
builder.Services.AddSingleton<ISessionRateLimiter>(new InMemorySlidingWindowRateLimiter(rateLimitConfig.MaxRequestsPerMinute));
builder.Services.AddSingleton(rateLimitConfig);

// Validators
builder.Services.AddScoped<IValidator<CreateSessionRequest>, CreateSessionRequestValidator>();
builder.Services.AddScoped<IValidator<AppendSignalsRequest>, AppendSignalsRequestValidator>();
builder.Services.AddScoped<IValidator<SignalDto>, SignalDtoValidator>();

// Fraud engine
builder.Services.AddSingleton<IRuleEngine, RuleEngine>();
builder.Services.AddSingleton<IMLScorer, MockMLScorer>();
builder.Services.AddSingleton<IFraudEvaluator>(sp => 
    new FraudEvaluator(
        sp.GetRequiredService<IRuleEngine>(),
        sp.GetRequiredService<IMLScorer>(),
        "1.0.0-dev"
    ));

var app = builder.Build();

// ─────────────────────────────────────────────────────────────
// Middleware
// ─────────────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

// ─────────────────────────────────────────────────────────────
// Helper Functions
// ─────────────────────────────────────────────────────────────

static SignalType ParseSignalType(string typeString)
{
    var normalized = typeString.Replace("_", "").ToLowerInvariant();
    
    return normalized switch
    {
        "mousemove" => SignalType.MouseMove,
        "mouseclick" or "click" => SignalType.MouseClick,
        "keystroke" => SignalType.Keystroke,
        "keystrokedynamics" => SignalType.KeystrokeDynamics,
        "scroll" => SignalType.Scroll,
        "touch" => SignalType.Touch,
        "visibility" => SignalType.Visibility,
        "focus" => SignalType.Focus,
        "paste" => SignalType.Paste,
        "device" => SignalType.Device,
        "performance" => SignalType.Performance,
        "fingerprint" => SignalType.Fingerprint,
        "forminteraction" => SignalType.FormInteraction,
        "accelerometer" => SignalType.Accelerometer,
        "gyroscope" => SignalType.Gyroscope,
        "applifecycle" => SignalType.AppLifecycle,
        "jailbreakdetection" => SignalType.JailbreakDetection,
        "rootdetection" => SignalType.RootDetection,
        _ => SignalType.Unknown
    };
}

// ─────────────────────────────────────────────────────────────
// Session Endpoints
// ─────────────────────────────────────────────────────────────

app.MapPost("/api/v1/sessions", async (
    CreateSessionRequest request,
    IValidator<CreateSessionRequest> validator,
    ISessionRepository sessionRepo) =>
{
    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        
        return Results.BadRequest(ApiResponse<SessionCreatedResponse>.Fail(
            "VALIDATION_ERROR",
            "Request validation failed",
            errors));
    }

    var session = await sessionRepo.CreateAsync(request);
    
    var response = ApiResponse<SessionCreatedResponse>.Ok(new SessionCreatedResponse
    {
        SessionId = session.Id,
        CreatedAt = session.CreatedAt
    });
    
    return Results.Created($"/api/v1/sessions/{session.Id}", response);
})
.WithName("CreateSession")
.WithTags("Sessions");

app.MapPost("/api/v1/sessions/{sessionId}/signals", async (
    Guid sessionId,
    AppendSignalsRequest request,
    IValidator<AppendSignalsRequest> validator,
    ISessionRepository sessionRepo,
    ISignalRepository signalRepo,
    ISessionRateLimiter rateLimiter,
    RateLimitingOptions rateLimitOptions) =>
{
    if (rateLimitOptions.Enabled)
    {
        var rateLimitResult = await rateLimiter.CheckAsync(sessionId);
        if (!rateLimitResult.IsAllowed)
        {
            return Results.Json(
                ApiResponse<SignalsAppendedResponse>.Fail(
                    "RATE_LIMIT_EXCEEDED",
                    $"Rate limit exceeded. Retry after {rateLimitResult.RetryAfter.TotalSeconds:F0} seconds"),
                statusCode: 429);
        }
    }

    if (!await sessionRepo.ExistsAsync(sessionId))
    {
        return Results.NotFound(ApiResponse<SignalsAppendedResponse>.Fail(
            "SESSION_NOT_FOUND",
            $"Session {sessionId} not found"));
    }

    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        
        return Results.BadRequest(ApiResponse<SignalsAppendedResponse>.Fail(
            "VALIDATION_ERROR",
            "Signal validation failed",
            errors));
    }

    var domainSignals = request.Signals.Select(dto => new Signal
    {
        Id = Guid.NewGuid().ToString(),
        SessionId = sessionId,
        Type = ParseSignalType(dto.Type),
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dto.Timestamp),
        Payload = dto.Payload
    }).ToList();

    await signalRepo.AppendAsync(sessionId, domainSignals);
    
    var totalCount = await signalRepo.GetCountBySessionIdAsync(sessionId);
    
    return Results.Ok(ApiResponse<SignalsAppendedResponse>.Ok(new SignalsAppendedResponse
    {
        SessionId = sessionId,
        SignalsReceived = domainSignals.Count,
        TotalSignals = totalCount
    }));
})
.WithName("AppendSignals")
.WithTags("Sessions");

app.MapPost("/api/v1/sessions/{sessionId}/complete", async (
    Guid sessionId,
    ISessionRepository sessionRepo,
    ISignalRepository signalRepo,
    IAnalysisRepository analysisRepo,
    IFraudEvaluator evaluator) =>
{
    var session = await sessionRepo.GetByIdAsync(sessionId);
    if (session is null)
    {
        return Results.NotFound(ApiResponse<SessionCompletedResponse>.Fail(
            "SESSION_NOT_FOUND",
            $"Session {sessionId} not found"));
    }

    var completedSession = await sessionRepo.CompleteAsync(sessionId);
    if (completedSession is null)
    {
        return Results.Problem("Failed to complete session");
    }

    var sessionSignals = await signalRepo.GetBySessionIdAsync(sessionId);
    var analysis = await evaluator.EvaluateAsync(completedSession, sessionSignals.ToList());
    
    await analysisRepo.SaveAsync(analysis);

    return Results.Ok(ApiResponse<SessionCompletedResponse>.Ok(new SessionCompletedResponse
    {
        SessionId = sessionId,
        CompletedAt = completedSession.CompletedAt ?? DateTimeOffset.UtcNow,
        SignalCount = sessionSignals.Count,
        AnalysisAvailable = true
    }));
})
.WithName("CompleteSession")
.WithTags("Sessions");

// ─────────────────────────────────────────────────────────────
// Analysis Endpoints
// ─────────────────────────────────────────────────────────────

app.MapGet("/api/v1/sessions/{sessionId}/analysis", async (
    Guid sessionId,
    ISessionRepository sessionRepo,
    IAnalysisRepository analysisRepo) =>
{
    if (!await sessionRepo.ExistsAsync(sessionId))
    {
        return Results.NotFound(ApiResponse<FraudAnalysis>.Fail(
            "SESSION_NOT_FOUND",
            $"Session {sessionId} not found"));
    }

    var analysis = await analysisRepo.GetBySessionIdAsync(sessionId);
    if (analysis is null)
    {
        return Results.NotFound(ApiResponse<FraudAnalysis>.Fail(
            "ANALYSIS_NOT_READY",
            "Analysis not yet available. Complete the session first."));
    }

    return Results.Ok(ApiResponse<FraudAnalysis>.Ok(analysis));
})
.WithName("GetAnalysis")
.WithTags("Analysis");

app.MapPost("/api/v1/analyze", async (
    AppendSignalsRequest request,
    IValidator<AppendSignalsRequest> validator,
    IFraudEvaluator evaluator) =>
{
    var validationResult = await validator.ValidateAsync(request);
    if (!validationResult.IsValid)
    {
        var errors = validationResult.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        
        return Results.BadRequest(ApiResponse<FraudAnalysis>.Fail(
            "VALIDATION_ERROR",
            "Signal validation failed",
            errors));
    }

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
        Type = ParseSignalType(dto.Type),
        Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(dto.Timestamp),
        Payload = dto.Payload
    }).ToList();

    var analysis = await evaluator.EvaluateAsync(session, signalList);
    return Results.Ok(ApiResponse<FraudAnalysis>.Ok(analysis));
})
.WithName("AnalyzeInline")
.WithTags("Analysis");

// ─────────────────────────────────────────────────────────────
// Health & Debug Endpoints
// ─────────────────────────────────────────────────────────────

app.MapGet("/api/v1/health", () => Results.Ok(new
{
    status = "healthy",
    timestamp = DateTimeOffset.UtcNow,
    version = "1.0.0-dev"
}))
.WithName("HealthCheck")
.WithTags("Health");

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/v1/debug/sessions/{sessionId}/signals", async (
        Guid sessionId,
        ISignalRepository signalRepo) =>
    {
        var signals = await signalRepo.GetBySessionIdAsync(sessionId);
        return Results.Ok(new 
        { 
            sessionId, 
            count = signals.Count,
            signals = signals.Take(100)
        });
    })
    .WithName("DebugListSignals")
    .WithTags("Debug");
}

app.Run();
