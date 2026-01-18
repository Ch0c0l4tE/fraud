/**
 * Mock API Server for testing the Fraud Tracker SDK
 * Simulates the Fraud.Ingestion.Api endpoints
 */

const http = require('http');

const PORT = 4000;

// In-memory storage
const sessions = new Map();
const signals = new Map();

// CORS headers
const corsHeaders = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

// Parse JSON body
function parseBody(req) {
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', chunk => body += chunk);
    req.on('end', () => {
      try {
        resolve(body ? JSON.parse(body) : {});
      } catch (e) {
        reject(e);
      }
    });
    req.on('error', reject);
  });
}

// Generate UUID
function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
    const r = Math.random() * 16 | 0;
    const v = c === 'x' ? r : (r & 0x3 | 0x8);
    return v.toString(16);
  });
}

// Mock fraud analysis
function generateMockAnalysis(sessionId, signalCount) {
  const score = Math.random();
  return {
    sessionId,
    verdict: score < 0.3 ? 'ALLOW' : score < 0.7 ? 'REVIEW' : 'BLOCK',
    confidenceScore: Math.round(score * 100) / 100,
    signalCount,
    riskFactors: [
      { name: 'keystroke_variance', score: Math.random(), weight: 0.3 },
      { name: 'mouse_velocity', score: Math.random(), weight: 0.25 },
      { name: 'device_fingerprint', score: Math.random(), weight: 0.2 },
    ],
    modelVersion: '1.0.0-mock',
    evaluatedAt: new Date().toISOString(),
  };
}

// Request handler
async function handler(req, res) {
  const url = new URL(req.url, `http://localhost:${PORT}`);
  const path = url.pathname;
  const method = req.method;

  // CORS preflight
  if (method === 'OPTIONS') {
    res.writeHead(204, corsHeaders);
    res.end();
    return;
  }

  // Set CORS headers
  Object.entries(corsHeaders).forEach(([k, v]) => res.setHeader(k, v));
  res.setHeader('Content-Type', 'application/json');

  try {
    // Health check
    if (path === '/api/v1/health' && method === 'GET') {
      res.writeHead(200);
      res.end(JSON.stringify({ status: 'healthy', timestamp: new Date().toISOString() }));
      return;
    }

    // Create session
    if (path === '/api/v1/sessions' && method === 'POST') {
      const body = await parseBody(req);
      const sessionId = uuid();
      const session = {
        id: sessionId,
        clientId: body.clientId || 'anonymous',
        deviceFingerprint: body.deviceFingerprint || '',
        createdAt: new Date().toISOString(),
        metadata: body.metadata || {},
      };
      sessions.set(sessionId, session);
      signals.set(sessionId, []);
      
      console.log(`[SESSION] Created: ${sessionId}`);
      
      res.writeHead(201);
      res.end(JSON.stringify({ sessionId }));
      return;
    }

    // Append signals
    const signalsMatch = path.match(/^\/api\/v1\/sessions\/([^/]+)\/signals$/);
    if (signalsMatch && method === 'POST') {
      const sessionId = signalsMatch[1];
      const body = await parseBody(req);
      
      const sessionSignals = signals.get(sessionId) || [];
      const newSignals = body.signals || [];
      sessionSignals.push(...newSignals);
      signals.set(sessionId, sessionSignals);
      
      console.log(`[SIGNALS] Session ${sessionId}: +${newSignals.length} signals (total: ${sessionSignals.length})`);
      
      // Log signal types
      const types = {};
      newSignals.forEach(s => types[s.type] = (types[s.type] || 0) + 1);
      console.log(`         Types:`, types);
      
      res.writeHead(202);
      res.end(JSON.stringify({ accepted: newSignals.length }));
      return;
    }

    // Complete session
    const completeMatch = path.match(/^\/api\/v1\/sessions\/([^/]+)\/complete$/);
    if (completeMatch && method === 'POST') {
      const sessionId = completeMatch[1];
      const session = sessions.get(sessionId);
      
      if (!session) {
        res.writeHead(404);
        res.end(JSON.stringify({ error: 'Session not found' }));
        return;
      }
      
      session.completedAt = new Date().toISOString();
      sessions.set(sessionId, session);
      
      const sessionSignals = signals.get(sessionId) || [];
      const analysis = generateMockAnalysis(sessionId, sessionSignals.length);
      
      console.log(`[COMPLETE] Session ${sessionId}: ${sessionSignals.length} signals, verdict: ${analysis.verdict}`);
      
      res.writeHead(200);
      res.end(JSON.stringify({ sessionId, status: 'completed', analysis }));
      return;
    }

    // Get analysis
    const analysisMatch = path.match(/^\/api\/v1\/sessions\/([^/]+)\/analysis$/);
    if (analysisMatch && method === 'GET') {
      const sessionId = analysisMatch[1];
      const sessionSignals = signals.get(sessionId) || [];
      
      if (!sessions.has(sessionId)) {
        res.writeHead(404);
        res.end(JSON.stringify({ error: 'Session not found' }));
        return;
      }
      
      const analysis = generateMockAnalysis(sessionId, sessionSignals.length);
      res.writeHead(200);
      res.end(JSON.stringify(analysis));
      return;
    }

    // Get all sessions (for debugging)
    if (path === '/api/v1/sessions' && method === 'GET') {
      const allSessions = Array.from(sessions.values()).map(s => ({
        ...s,
        signalCount: (signals.get(s.id) || []).length,
      }));
      res.writeHead(200);
      res.end(JSON.stringify(allSessions));
      return;
    }

    // Get session signals (for debugging)
    const getSignalsMatch = path.match(/^\/api\/v1\/sessions\/([^/]+)\/signals$/);
    if (getSignalsMatch && method === 'GET') {
      const sessionId = getSignalsMatch[1];
      const sessionSignals = signals.get(sessionId) || [];
      res.writeHead(200);
      res.end(JSON.stringify(sessionSignals));
      return;
    }

    // 404
    res.writeHead(404);
    res.end(JSON.stringify({ error: 'Not found' }));

  } catch (err) {
    console.error('Error:', err);
    res.writeHead(500);
    res.end(JSON.stringify({ error: err.message }));
  }
}

const server = http.createServer(handler);

server.listen(PORT, () => {
  console.log(`
╔═══════════════════════════════════════════════════════════╗
║           Fraud Detection Mock API Server                 ║
╠═══════════════════════════════════════════════════════════╣
║  Listening on: http://localhost:${PORT}                      ║
║                                                           ║
║  Endpoints:                                               ║
║    GET  /api/v1/health                                    ║
║    POST /api/v1/sessions                                  ║
║    POST /api/v1/sessions/:id/signals                      ║
║    POST /api/v1/sessions/:id/complete                     ║
║    GET  /api/v1/sessions/:id/analysis                     ║
║    GET  /api/v1/sessions (debug: list all)                ║
║    GET  /api/v1/sessions/:id/signals (debug: list)        ║
╚═══════════════════════════════════════════════════════════╝
  `);
});
