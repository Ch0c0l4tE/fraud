# Architecture Documentation

## System Overview

```mermaid
graph TB
    subgraph Clients
        WEB[Web Browser]
        IOS[iOS App]
        ANDROID[Android App]
    end

    subgraph SDKs
        JS[fraud-tracker.js]
        SWIFT[FraudTracker.swift]
        KOTLIN[FraudTracker.kt]
    end

    subgraph Backend ["Backend (.NET 10)"]
        API[Ingestion API]
        ENGINE[Fraud Engine]
        ANALYSIS[Analysis API]
    end

    subgraph Storage
        PG[(PostgreSQL + TimescaleDB)]
        REDIS[(Redis Cache)]
    end

    subgraph ML
        ONNX[ONNX Runtime]
        MODELS[Trained Models]
    end

    WEB --> JS
    IOS --> SWIFT
    ANDROID --> KOTLIN

    JS -->|HTTP POST| API
    SWIFT -->|HTTP POST| API
    KOTLIN -->|HTTP POST| API

    API --> PG
    API --> ENGINE
    ENGINE --> ONNX
    ONNX --> MODELS
    ENGINE --> ANALYSIS
    ANALYSIS --> REDIS
    ANALYSIS -->|GET /analysis| WEB
```

---

## Data Flow Diagram

```mermaid
sequenceDiagram
    participant Client as Browser/Mobile
    participant SDK as Fraud SDK
    participant API as Ingestion API
    participant DB as PostgreSQL
    participant Engine as Fraud Engine
    participant ML as ML Model
    participant Cache as Redis

    Note over Client,SDK: Phase 1: Capture
    Client->>SDK: User interaction (mouse, keyboard, touch)
    SDK->>SDK: Buffer signals (500ms / 50 events)
    SDK->>API: POST /sessions/{id}/signals

    Note over API,DB: Phase 2: Ingest
    API->>API: Validate & rate limit
    API->>DB: Store signals
    API-->>SDK: 202 Accepted

    Note over Client,API: Session Complete
    Client->>SDK: Page unload / session end
    SDK->>API: POST /sessions/{id}/complete

    Note over API,Engine: Phase 3: Evaluate
    API->>Engine: Evaluate session
    Engine->>DB: Fetch all signals
    Engine->>Engine: Extract features
    Engine->>ML: Inference
    ML-->>Engine: Risk scores
    Engine->>DB: Store verdict
    Engine-->>API: FraudResult

    Note over Client,Cache: Phase 4: Query
    Client->>API: GET /sessions/{id}/analysis
    API->>Cache: Check cache
    alt Cache hit
        Cache-->>API: Cached result
    else Cache miss
        API->>DB: Fetch verdict
        API->>Cache: Store in cache
    end
    API-->>Client: FraudAnalysis response
```

---

## Component Diagram

```mermaid
graph LR
    subgraph "Fraud.Sdk.Browser"
        TRACKER[FraudTracker]
        MOUSE[MouseCapture]
        KEYBOARD[KeyboardCapture]
        TOUCH[TouchCapture]
        DEVICE[DeviceFingerprint]
        TRANSPORT[HttpTransport]

        TRACKER --> MOUSE
        TRACKER --> KEYBOARD
        TRACKER --> TOUCH
        TRACKER --> DEVICE
        TRACKER --> TRANSPORT
    end

    subgraph "Fraud.Ingestion.Api"
        CTRL[SessionController]
        VALID[SignalValidator]
        RATE[RateLimiter]
        REPO[SignalRepository]

        CTRL --> VALID
        CTRL --> RATE
        CTRL --> REPO
    end

    subgraph "Fraud.Engine"
        EVAL[SessionEvaluator]
        FEAT[FeatureExtractor]
        
        subgraph "Fraud.Engine.Rules"
            VELOCITY[VelocityRule]
            PATTERN[PatternRule]
            DEVICE_RULE[DeviceRule]
        end

        subgraph "Fraud.Engine.ML"
            ONNX_INFER[OnnxInference]
            ENSEMBLE[EnsembleScorer]
        end

        EVAL --> FEAT
        EVAL --> VELOCITY
        EVAL --> PATTERN
        EVAL --> DEVICE_RULE
        EVAL --> ONNX_INFER
        EVAL --> ENSEMBLE
    end

    TRANSPORT -->|HTTP| CTRL
    REPO -->|Query| EVAL
```

---

## Signal Processing Pipeline

```mermaid
flowchart LR
    subgraph Capture
        RAW[Raw Events]
        ENRICH[Enrichment]
        BATCH[Batching]
    end

    subgraph Ingest
        VALIDATE[Schema Validation]
        DEDUPE[Deduplication]
        STORE[Time-Series Storage]
    end

    subgraph Analyze
        EXTRACT[Feature Extraction]
        RULES[Rule Engine]
        ML_SCORE[ML Scoring]
        COMBINE[Score Combination]
    end

    subgraph Output
        VERDICT[Verdict]
        FACTORS[Risk Factors]
        WEBHOOK[Webhook Notify]
    end

    RAW --> ENRICH --> BATCH
    BATCH --> VALIDATE --> DEDUPE --> STORE
    STORE --> EXTRACT --> RULES --> COMBINE
    EXTRACT --> ML_SCORE --> COMBINE
    COMBINE --> VERDICT
    COMBINE --> FACTORS
    VERDICT --> WEBHOOK
```

---

## Fraud Scoring Flow

```mermaid
flowchart TB
    START([Session Signals]) --> FEAT[Feature Extraction]
    
    FEAT --> RULE_CHECK{Rule Engine}
    RULE_CHECK -->|Triggers| RULE_SCORE[Rule Scores]
    
    FEAT --> ML_CHECK{ML Model}
    ML_CHECK -->|Inference| ML_SCORE[ML Score]
    
    RULE_SCORE --> ENSEMBLE[Ensemble Combiner]
    ML_SCORE --> ENSEMBLE
    
    ENSEMBLE --> THRESHOLD{Score Threshold}
    
    THRESHOLD -->|< 0.3| ALLOW[✅ ALLOW]
    THRESHOLD -->|0.3 - 0.7| REVIEW[⚠️ REVIEW]
    THRESHOLD -->|> 0.7| BLOCK[🚫 BLOCK]
    
    ALLOW --> OUTPUT([FraudResult])
    REVIEW --> OUTPUT
    BLOCK --> OUTPUT
```

---

## Deployment Architecture

```mermaid
graph TB
    subgraph Internet
        USERS[Users]
    end

    subgraph CDN
        JSSDK[fraud-tracker.js]
    end

    subgraph "Kubernetes Cluster"
        subgraph "Ingress"
            LB[Load Balancer]
        end

        subgraph "API Pods"
            API1[Ingestion API #1]
            API2[Ingestion API #2]
            API3[Ingestion API #3]
        end

        subgraph "Engine Pods"
            ENG1[Fraud Engine #1]
            ENG2[Fraud Engine #2]
        end

        subgraph "Data Layer"
            PG_PRIMARY[(PostgreSQL Primary)]
            PG_REPLICA[(PostgreSQL Replica)]
            REDIS[(Redis Cluster)]
        end
    end

    USERS --> CDN
    USERS --> LB
    CDN --> JSSDK
    LB --> API1 & API2 & API3
    API1 & API2 & API3 --> ENG1 & ENG2
    API1 & API2 & API3 --> PG_PRIMARY
    ENG1 & ENG2 --> PG_REPLICA
    API1 & API2 & API3 --> REDIS
```

---

## Entity Relationship Diagram

```mermaid
erDiagram
    SESSION ||--o{ SIGNAL : contains
    SESSION ||--o| ANALYSIS : has
    ANALYSIS ||--o{ RISK_FACTOR : includes

    SESSION {
        uuid id PK
        string client_id
        string device_fingerprint
        jsonb metadata
        timestamp created_at
        timestamp completed_at
    }

    SIGNAL {
        uuid id PK
        uuid session_id FK
        string type
        timestamp captured_at
        jsonb payload
    }

    ANALYSIS {
        uuid id PK
        uuid session_id FK
        string verdict
        float confidence_score
        string model_version
        timestamp evaluated_at
    }

    RISK_FACTOR {
        uuid id PK
        uuid analysis_id FK
        string name
        float score
        float weight
    }
```

---

## Phase Development Flow

```mermaid
gantt
    title Fraud Detection Development Phases
    dateFormat  YYYY-MM-DD
    section Phase 1
    Browser SDK           :p1a, 2026-01-15, 14d
    iOS SDK               :p1b, after p1a, 10d
    Android SDK           :p1c, after p1a, 10d
    Mock Server           :p1d, 2026-01-15, 7d
    
    section Phase 2
    Ingestion API         :p2a, after p1d, 14d
    Database Schema       :p2b, after p1d, 5d
    Rate Limiting         :p2c, after p2a, 5d
    
    section Phase 3
    Rule Engine           :p3a, after p2a, 14d
    Feature Extraction    :p3b, after p2a, 10d
    ML Model Training     :p3c, after p3b, 21d
    ML Integration        :p3d, after p3c, 7d
    
    section Phase 4
    Analysis API          :p4a, after p3a, 7d
    Caching Layer         :p4b, after p4a, 5d
    Webhooks              :p4c, after p4b, 5d
    Dashboard             :p4d, after p4c, 14d
```

---

## SDK Event Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Initialized: FraudTracker.init()
    
    Initialized --> Capturing: Session started
    
    Capturing --> Buffering: Event captured
    Buffering --> Capturing: Continue capturing
    
    Buffering --> Flushing: Buffer full OR timeout
    Flushing --> Capturing: Flush complete
    
    Capturing --> Completing: Page unload
    Completing --> [*]: Session complete
    
    Capturing --> Paused: Tab hidden
    Paused --> Capturing: Tab visible
    
    Flushing --> Retrying: Network error
    Retrying --> Flushing: Retry attempt
    Retrying --> Failed: Max retries exceeded
    Failed --> Capturing: Continue capturing (offline mode)
```
