# Fraud Detection Playground

Interactive testing environment for the Fraud Tracker Browser SDK connected to the real .NET Ingestion API.

## Quick Start

### Prerequisites

- **Node.js** (v18+) - for static file serving
- **.NET 10** - for the Ingestion API
- **Browser** - Chrome/Firefox/Edge recommended

### 1. Start the .NET Ingestion API

```bash
cd src/Fraud.Ingestion.Api
dotnet run --urls "http://localhost:4000"
```

You should see:
```
Now listening on: http://localhost:4000
Application started.
```

Verify it's running:
```bash
curl http://localhost:4000/api/v1/health
# → {"status":"healthy","timestamp":"...","version":"1.0.0-dev"}
```

### 2. Build the Browser SDK

```bash
cd src/sdks/browser
npm install
npm run build
```

### 3. Start the Static File Server

From the project root:
```bash
npx serve -p 3000
```

### 4. Open the Playground

Navigate to: **http://localhost:3000/tools/playground/**

---

## Using the Playground

### Initialize Tracking

1. Click **"Initialize Tracker"**
2. The SDK will:
   - Register a session with the API (`POST /api/v1/sessions`)
   - Start capturing behavioral signals
   - Show "Tracking active" status

### Generate Signals

Interact with the test form to generate signals:

| Action | Signal Type |
|--------|-------------|
| Move mouse | `mouse_move` (with velocity, acceleration) |
| Click | `mouse_click` |
| Type in fields | `keystroke`, `keystroke_dynamics` |
| Tab between fields | `focus` |
| Scroll page | `scroll` |
| Paste text | `paste` |

### View Real-Time Data

The playground displays:
- **Signal count** - Total signals captured
- **Flush count** - Number of batches sent to API
- **Typing stats** - WPM, dwell time, flight time, error rate
- **Mouse trail** - Visual representation of mouse movement
- **Signal breakdown** - Count by signal type
- **Event log** - Detailed signal data

### Complete Session

Click **"Complete Session"** to:
- Flush remaining signals
- Trigger fraud analysis on the server
- End the tracking session

---

## API Endpoints Used

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/v1/sessions` | Create session (on init) |
| `POST` | `/api/v1/sessions/{id}/signals` | Send signal batches |
| `POST` | `/api/v1/sessions/{id}/complete` | Complete & analyze |
| `GET` | `/api/v1/sessions/{id}/analysis` | Get fraud analysis |
| `GET` | `/api/v1/health` | Health check |

---

## Testing with curl

### Create a Session
```bash
curl -X POST http://localhost:4000/api/v1/sessions \
  -H "Content-Type: application/json" \
  -d '{"clientId": "test-user", "deviceFingerprint": "fp-123"}'
```

### Send Signals
```bash
curl -X POST "http://localhost:4000/api/v1/sessions/{SESSION_ID}/signals" \
  -H "Content-Type: application/json" \
  -d '{
    "sessionId": "{SESSION_ID}",
    "signals": [
      {"type": "mouse_move", "timestamp": 1737236001000, "payload": {"x": 100, "y": 200}},
      {"type": "keystroke_dynamics", "timestamp": 1737236002000, "payload": {"dwellTime": 80}}
    ]
  }'
```

### Complete Session
```bash
curl -X POST "http://localhost:4000/api/v1/sessions/{SESSION_ID}/complete" \
  -H "Content-Type: application/json"
```

### Get Analysis
```bash
curl "http://localhost:4000/api/v1/sessions/{SESSION_ID}/analysis"
```

---

## Signal Types Captured

### Device & Fingerprinting
- `device` - Screen size, user agent, language, hardware info
- `performance` - Navigation timing, load metrics
- `fingerprint` - Canvas, WebGL, audio, fonts, timezone

### Behavioral
- `mouse_move` - Position, velocity, acceleration, distance
- `mouse_click` - Button, position, target element
- `keystroke` - Key codes (privacy-preserving)
- `keystroke_dynamics` - Dwell time, flight time, digraphs
- `scroll` - Direction, delta values
- `touch` - Touch position, multi-touch
- `focus` - Focus/blur events
- `paste` - Content length (not content)
- `form_interaction` - Field focus order, time-to-fill, corrections

---

## Configuration Options

The SDK can be configured when calling `tracker.init()`:

```javascript
tracker.init({
  endpoint: 'http://localhost:4000',  // API endpoint
  clientId: 'your-app-id',            // Your application ID
  batchSize: 50,                      // Signals per batch
  flushInterval: 500,                 // Flush interval (ms)
  debug: true,                        // Enable console logging
  
  // Callbacks
  onSignal: (signal) => {},           // Called per signal
  onFlush: (signals) => {},           // Called on flush
  onTypingStats: (stats) => {},       // Called with typing stats
  onSessionStart: (session) => {},    // Called when session starts
});
```

---

## Troubleshooting

### "Connection failed" on init
- Ensure the .NET API is running on port 4000
- Check CORS is enabled (it is by default in development)

### No signals appearing
- Check browser console for errors
- Ensure `debug: true` is set in config
- Verify the SDK is built (`npm run build` in browser SDK directory)

### Analysis shows "MOCK" scores
- This is expected! The ML scorer is a mock implementation
- Real fraud detection rules will be added in Phase 3

---

## Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Browser SDK   │────▶│  Ingestion API  │────▶│  Fraud Engine   │
│                 │     │   (.NET 10)     │     │                 │
│ - Mouse tracking│     │ - Validation    │     │ - Rule Engine   │
│ - Keystrokes    │     │ - Rate Limiting │     │ - ML Scorer     │
│ - Fingerprints  │     │ - Storage       │     │ - Evaluator     │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

---

## Development

### Run All Services (Quick)

Terminal 1 - API:
```bash
cd src/Fraud.Ingestion.Api && dotnet run --urls "http://localhost:4000"
```

Terminal 2 - Static Server:
```bash
npx serve -p 3000
```

Then open: http://localhost:3000/tools/playground/
