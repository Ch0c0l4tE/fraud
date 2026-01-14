# Fraud Detection Platform — Project Plan

## Executive Summary

A behavioral fraud detection system that captures user/session signals from web and mobile clients, ingests them via an API, and evaluates them using a fraud engine with AI-powered scoring.

---

## Components Overview

| Component | Technology | Purpose |
|-----------|------------|---------|
| **Browser SDK** | JavaScript (ES6+) | Capture user behavior in web applications |
| **Mobile SDK** | Swift (iOS) / Kotlin (Android) | Capture user behavior in native mobile apps |
| **Ingestion API** | .NET 10 Web API | Receive and store behavioral signals |
| **Fraud Engine** | .NET 10 + ML.NET / ONNX | Evaluate sessions and calculate fraud scores |
| **Analysis API** | .NET 10 Web API | Query fraud status for a given session |

---

## Why AI/ML for Fraud Detection?

### Recommendation: Use Machine Learning

Traditional rule-based fraud detection has limitations:
- Rules become outdated as fraud patterns evolve
- High false-positive rates with static thresholds
- Cannot detect novel attack vectors
- Difficult to correlate complex behavioral patterns

**AI/ML Advantages:**

1. **Pattern Recognition**: ML models can identify subtle, non-linear correlations between hundreds of behavioral signals that humans cannot manually codify.

2. **Adaptive Learning**: Models can be retrained as new fraud patterns emerge.

3. **Confidence Scoring**: ML outputs probability scores (0.0–1.0) rather than binary decisions, enabling nuanced risk tiers.

4. **Anomaly Detection**: Unsupervised models can flag sessions that deviate from "normal" behavior even for unknown attack types.

5. **Feature Importance**: ML can reveal which signals contribute most to fraud, informing product decisions.

### Proposed ML Approach

| Layer | Model Type | Purpose |
|-------|------------|---------|
| Real-time scoring | Gradient Boosted Trees (LightGBM/XGBoost via ONNX) | Fast inference (~1ms) for live requests |
| Behavioral embedding | Autoencoder / LSTM | Learn session "fingerprints" for anomaly detection |
| Ensemble | Stacking classifier | Combine multiple signals for final confidence |

**Fallback**: Start with a rule-based engine (Phase 3a), then layer ML on top (Phase 3b). This lets you ship quickly while building training data.

---

## Development Phases

### Phase 1: Behavior Capture SDKs
**Goal**: Build client SDKs that capture and transmit behavioral signals.

#### 1.1 Browser SDK (`fraud-tracker.js`)
Signals to capture:
- Mouse movements, velocity, acceleration
- Keystroke dynamics (dwell time, flight time)
- Touch events (for touch screens)
- Scroll patterns
- Page visibility / focus changes
- Device fingerprinting (screen, timezone, language, WebGL, Canvas)
- Navigation timing & performance metrics
- Copy/paste events
- Form interaction patterns

**Deliverables**:
- `fraud-tracker.js` — embeddable script
- NPM package `@fraud/browser-sdk`
- TypeScript types
- Mock server endpoint for testing

#### 1.2 Mobile SDKs
**iOS (Swift)**:
- Touch pressure, gesture patterns
- Accelerometer / gyroscope
- Device info, jailbreak detection
- App lifecycle events

**Android (Kotlin)**:
- Touch patterns, gesture velocity
- Sensor data
- Device info, root detection
- App lifecycle events

**Deliverables**:
- `FraudTracker.swift` — iOS SDK
- `FraudTracker.kt` — Android SDK
- Shared signal schema

---

### Phase 2: Ingestion API
**Goal**: Receive signals from all SDKs and persist them.

#### Endpoints
| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/sessions` | Create a new session |
| POST | `/api/v1/sessions/{id}/signals` | Append signals to session |
| POST | `/api/v1/sessions/{id}/complete` | Mark session complete, trigger analysis |

#### Data Model
```
Session {
  id: UUID
  clientId: string
  deviceFingerprint: string
  createdAt: DateTime
  completedAt: DateTime?
  metadata: JSON
}

Signal {
  id: UUID
  sessionId: UUID
  type: string (mouse|keyboard|touch|scroll|visibility|...)
  timestamp: DateTime
  payload: JSON
}
```

#### Phase 2 Deliverables
- `Fraud.Ingestion.Api` project
- In-memory storage (dev) / PostgreSQL + TimescaleDB (prod)
- Signal validation & rate limiting
- Mock fraud engine returning random scores

---

### Phase 3: Fraud Engine
**Goal**: Evaluate sessions and compute fraud confidence.

#### 3a — Rule-Based Engine (MVP)
- Velocity thresholds (too-fast typing, mouse teleportation)
- Device fingerprint anomalies
- Session duration outliers
- Known bad patterns (headless browsers, automation signatures)

#### 3b — ML Engine
- Feature extraction pipeline
- Model training harness (offline)
- ONNX model serving (online)
- A/B testing framework for model versions

#### Output Schema
```json
{
  "sessionId": "uuid",
  "verdict": "ALLOW" | "REVIEW" | "BLOCK",
  "confidenceScore": 0.87,
  "riskFactors": [
    { "name": "keystroke_too_fast", "weight": 0.3 },
    { "name": "mouse_linear_path", "weight": 0.25 }
  ],
  "modelVersion": "v1.2.0",
  "evaluatedAt": "2026-01-14T12:00:00Z"
}
```

#### Phase 3 Deliverables
- `Fraud.Engine` class library
- `Fraud.Engine.Rules` — rule-based scoring
- `Fraud.Engine.ML` — ML model inference
- Unit tests with synthetic sessions

---

### Phase 4: Analysis API
**Goal**: Expose fraud verdicts to consumers.

#### Endpoints
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/sessions/{id}/analysis` | Get fraud analysis for session |
| POST | `/api/v1/analyze` | Synchronous analysis (signals in body) |
| GET | `/api/v1/health` | Health check |

#### Phase 4 Deliverables
- Extend `Fraud.Ingestion.Api` or separate `Fraud.Analysis.Api`
- Caching layer for repeated queries
- Webhook support for async notifications
- Dashboard metrics endpoint

---

## Mock Strategy Per Phase

| Current Phase | Mocked Component | Mock Behavior |
|---------------|------------------|---------------|
| 1 (SDKs) | Ingestion API | Local HTTP server echoes signals |
| 2 (Ingestion) | Fraud Engine | Returns random score 0.0–1.0 |
| 3 (Engine) | — | Real engine, test with synthetic data |
| 4 (Analysis) | — | Full integration |

---

## Project Structure

```
fraud/
├── docs/
│   ├── PROJECT_PLAN.md          # This file
│   ├── ARCHITECTURE.md          # Diagrams
│   └── PROMPTS.md               # Prompt templates per phase
├── src/
│   ├── Fraud.Ingestion.Api/     # .NET 10 Web API
│   ├── Fraud.Engine/            # .NET 10 Class Library
│   ├── Fraud.Engine.Rules/      # Rule-based scoring
│   ├── Fraud.Engine.ML/         # ML inference
│   ├── Fraud.Sdk.Contracts/     # Shared DTOs/contracts
│   └── sdks/
│       ├── browser/             # JavaScript SDK
│       │   ├── src/
│       │   ├── package.json
│       │   └── tsconfig.json
│       ├── ios/                 # Swift SDK
│       └── android/             # Kotlin SDK
├── tests/
│   ├── Fraud.Ingestion.Api.Tests/
│   ├── Fraud.Engine.Tests/
│   └── Fraud.Integration.Tests/
├── tools/
│   └── mock-server/             # Mock API for SDK development
├── Fraud.sln
└── README.md
```

---

## Prompt Guidelines for Each Phase

### Phase 1 Prompts
```
CONTEXT: Building browser SDK for fraud detection. Must capture mouse, keyboard,
touch, scroll, visibility, and device fingerprint signals. Send via batched HTTP POST.

TASK: Implement [specific feature, e.g., "keystroke dynamics capture"].

CONSTRAINTS:
- Pure JavaScript, no dependencies
- Must work in IE11+ (or specify modern-only)
- Signals batched every 500ms or 50 events
- Include TypeScript types

OUTPUT: fraud-tracker.js module with the feature implemented.
```

### Phase 2 Prompts
```
CONTEXT: Building .NET 10 ingestion API for fraud signals. Receives JSON payloads
from browser/mobile SDKs. Stores in PostgreSQL/TimescaleDB.

TASK: Implement [specific feature, e.g., "POST /api/v1/sessions/{id}/signals endpoint"].

CONSTRAINTS:
- .NET 10 minimal API style
- Validate signal schema with FluentValidation
- Rate limit: 100 requests/session/minute
- Return 202 Accepted for valid signals

OUTPUT: Controller/endpoint code with validation.
```

### Phase 3 Prompts
```
CONTEXT: Building fraud engine. Evaluates session signals and outputs confidence score.
Phase 3a = rule-based, Phase 3b = ML.

TASK: Implement [specific feature, e.g., "mouse velocity anomaly detector"].

CONSTRAINTS:
- Input: List<Signal> for a session
- Output: RiskFactor { Name, Score, Weight }
- Pure C#, no side effects
- Unit testable

OUTPUT: Detector class with Evaluate() method.
```

### Phase 4 Prompts
```
CONTEXT: Building analysis API. Exposes GET /api/v1/sessions/{id}/analysis.
Calls Fraud.Engine and returns verdict.

TASK: Implement [specific feature, e.g., "analysis endpoint with caching"].

CONSTRAINTS:
- Cache results for 5 minutes
- Include model version in response
- Support webhook callback URL in request

OUTPUT: Endpoint code with caching logic.
```

---

## Tech Stack Summary

| Layer | Technology |
|-------|------------|
| Browser SDK | TypeScript → JavaScript (ES5 bundle) |
| iOS SDK | Swift 5.9+ |
| Android SDK | Kotlin 1.9+ |
| API | .NET 10 Minimal APIs |
| Database | PostgreSQL 16 + TimescaleDB |
| Caching | Redis |
| ML Runtime | ML.NET / ONNX Runtime |
| CI/CD | GitHub Actions |
| Containers | Docker + Kubernetes |

---

## Next Steps

1. **Scaffold the solution** — Create .NET projects and SDK folders
2. **Implement Phase 1** — Start with browser SDK
3. **Stand up mock server** — Allow SDK testing without full API
4. **Iterate through phases** — Use prompts above as context for each task

---

## Appendix: Signal Types Reference

| Type | Captured Data |
|------|---------------|
| `mouse_move` | x, y, timestamp, velocity, acceleration |
| `mouse_click` | x, y, button, timestamp |
| `keystroke` | key (hashed), dwell_time_ms, flight_time_ms |
| `scroll` | direction, delta, velocity |
| `touch` | x, y, pressure, radius, timestamp |
| `visibility` | state (visible/hidden), timestamp |
| `focus` | element_type, timestamp |
| `paste` | length (not content), timestamp |
| `device` | screen, timezone, language, webgl_hash, canvas_hash |
| `performance` | navigation_timing, resource_timing |
