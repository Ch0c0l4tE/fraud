/**
 * Fraud Tracker - Browser SDK
 * Captures user behavior signals for fraud detection
 */

export interface FraudTrackerConfig {
  /** API endpoint to send signals */
  endpoint: string;
  /** Client identifier */
  clientId: string;
  /** Batch size before flushing (default: 50) */
  batchSize?: number;
  /** Flush interval in ms (default: 500) */
  flushInterval?: number;
  /** Enable debug logging */
  debug?: boolean;
}

export interface Signal {
  type: SignalType;
  timestamp: number;
  payload: Record<string, unknown>;
}

export type SignalType =
  | 'mouse_move'
  | 'mouse_click'
  | 'keystroke'
  | 'scroll'
  | 'touch'
  | 'visibility'
  | 'focus'
  | 'paste'
  | 'device'
  | 'performance';

export interface Session {
  id: string;
  clientId: string;
  startedAt: number;
  deviceFingerprint: string;
}

class SignalBuffer {
  private signals: Signal[] = [];
  private readonly maxSize: number;

  constructor(maxSize: number = 50) {
    this.maxSize = maxSize;
  }

  push(signal: Signal): boolean {
    this.signals.push(signal);
    return this.signals.length >= this.maxSize;
  }

  flush(): Signal[] {
    const batch = [...this.signals];
    this.signals = [];
    return batch;
  }

  get length(): number {
    return this.signals.length;
  }
}

class FraudTracker {
  private config: Required<FraudTrackerConfig>;
  private session: Session | null = null;
  private buffer: SignalBuffer;
  private flushTimer: ReturnType<typeof setInterval> | null = null;
  private isInitialized = false;

  constructor() {
    this.config = {
      endpoint: '',
      clientId: '',
      batchSize: 50,
      flushInterval: 500,
      debug: false,
    };
    this.buffer = new SignalBuffer(50);
  }

  /**
   * Initialize the tracker with configuration
   */
  init(config: FraudTrackerConfig): void {
    if (this.isInitialized) {
      this.log('Already initialized');
      return;
    }

    this.config = {
      ...this.config,
      ...config,
      batchSize: config.batchSize ?? 50,
      flushInterval: config.flushInterval ?? 500,
      debug: config.debug ?? false,
    };

    this.buffer = new SignalBuffer(this.config.batchSize);
    this.session = this.createSession();
    this.isInitialized = true;

    this.attachListeners();
    this.startFlushTimer();
    this.captureDeviceInfo();
    this.capturePerformance();

    this.log('Initialized', { sessionId: this.session.id });
  }

  /**
   * Manually capture a custom signal
   */
  capture(type: SignalType, payload: Record<string, unknown>): void {
    if (!this.isInitialized) {
      console.warn('[FraudTracker] Not initialized. Call init() first.');
      return;
    }

    const signal: Signal = {
      type,
      timestamp: Date.now(),
      payload,
    };

    const shouldFlush = this.buffer.push(signal);
    if (shouldFlush) {
      this.flush();
    }
  }

  /**
   * End the session and flush remaining signals
   */
  async complete(): Promise<void> {
    if (!this.isInitialized || !this.session) return;

    this.stopFlushTimer();
    this.detachListeners();
    await this.flush();
    await this.sendComplete();

    this.log('Session completed', { sessionId: this.session.id });
    this.session = null;
    this.isInitialized = false;
  }

  /**
   * Get current session ID
   */
  getSessionId(): string | null {
    return this.session?.id ?? null;
  }

  // ─────────────────────────────────────────────────────────────
  // Private methods
  // ─────────────────────────────────────────────────────────────

  private createSession(): Session {
    return {
      id: this.generateUUID(),
      clientId: this.config.clientId,
      startedAt: Date.now(),
      deviceFingerprint: this.generateFingerprint(),
    };
  }

  private generateUUID(): string {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
      const r = (Math.random() * 16) | 0;
      const v = c === 'x' ? r : (r & 0x3) | 0x8;
      return v.toString(16);
    });
  }

  private generateFingerprint(): string {
    // Basic fingerprint - will be enhanced in Phase 1
    const components = [
      navigator.userAgent,
      navigator.language,
      screen.width,
      screen.height,
      screen.colorDepth,
      new Date().getTimezoneOffset(),
    ];
    return this.hash(components.join('|'));
  }

  private hash(str: string): string {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
      const char = str.charCodeAt(i);
      hash = (hash << 5) - hash + char;
      hash = hash & hash;
    }
    return Math.abs(hash).toString(16);
  }

  private attachListeners(): void {
    // Mouse events
    document.addEventListener('mousemove', this.handleMouseMove);
    document.addEventListener('click', this.handleClick);

    // Keyboard events
    document.addEventListener('keydown', this.handleKeyDown);
    document.addEventListener('keyup', this.handleKeyUp);

    // Scroll events
    document.addEventListener('scroll', this.handleScroll, { passive: true });

    // Touch events
    document.addEventListener('touchstart', this.handleTouch, { passive: true });
    document.addEventListener('touchmove', this.handleTouch, { passive: true });

    // Visibility
    document.addEventListener('visibilitychange', this.handleVisibility);

    // Focus
    window.addEventListener('focus', this.handleFocus);
    window.addEventListener('blur', this.handleBlur);

    // Paste
    document.addEventListener('paste', this.handlePaste);

    // Unload
    window.addEventListener('beforeunload', this.handleUnload);
  }

  private detachListeners(): void {
    document.removeEventListener('mousemove', this.handleMouseMove);
    document.removeEventListener('click', this.handleClick);
    document.removeEventListener('keydown', this.handleKeyDown);
    document.removeEventListener('keyup', this.handleKeyUp);
    document.removeEventListener('scroll', this.handleScroll);
    document.removeEventListener('touchstart', this.handleTouch);
    document.removeEventListener('touchmove', this.handleTouch);
    document.removeEventListener('visibilitychange', this.handleVisibility);
    window.removeEventListener('focus', this.handleFocus);
    window.removeEventListener('blur', this.handleBlur);
    document.removeEventListener('paste', this.handlePaste);
    window.removeEventListener('beforeunload', this.handleUnload);
  }

  // ─────────────────────────────────────────────────────────────
  // Event Handlers
  // ─────────────────────────────────────────────────────────────

  private lastMousePosition = { x: 0, y: 0, time: 0 };

  private handleMouseMove = (e: MouseEvent): void => {
    const now = Date.now();
    const dx = e.clientX - this.lastMousePosition.x;
    const dy = e.clientY - this.lastMousePosition.y;
    const dt = now - this.lastMousePosition.time || 1;
    const velocity = Math.sqrt(dx * dx + dy * dy) / dt;

    this.capture('mouse_move', {
      x: e.clientX,
      y: e.clientY,
      velocity,
      dx,
      dy,
    });

    this.lastMousePosition = { x: e.clientX, y: e.clientY, time: now };
  };

  private handleClick = (e: MouseEvent): void => {
    this.capture('mouse_click', {
      x: e.clientX,
      y: e.clientY,
      button: e.button,
      target: (e.target as HTMLElement)?.tagName,
    });
  };

  private keyTimes: Map<string, number> = new Map();

  private handleKeyDown = (e: KeyboardEvent): void => {
    // Don't capture the actual key for privacy, just timing
    const keyId = e.code;
    if (!this.keyTimes.has(keyId)) {
      this.keyTimes.set(keyId, Date.now());
    }
  };

  private handleKeyUp = (e: KeyboardEvent): void => {
    const keyId = e.code;
    const downTime = this.keyTimes.get(keyId);
    if (downTime) {
      const dwellTime = Date.now() - downTime;
      this.capture('keystroke', {
        keyCode: e.code, // Not the actual character
        dwellTimeMs: dwellTime,
        shiftKey: e.shiftKey,
        ctrlKey: e.ctrlKey,
      });
      this.keyTimes.delete(keyId);
    }
  };

  private lastScrollTime = 0;

  private handleScroll = (): void => {
    const now = Date.now();
    if (now - this.lastScrollTime < 100) return; // Throttle

    this.capture('scroll', {
      scrollX: window.scrollX,
      scrollY: window.scrollY,
      direction: window.scrollY > 0 ? 'down' : 'up',
    });

    this.lastScrollTime = now;
  };

  private handleTouch = (e: TouchEvent): void => {
    const touch = e.touches[0];
    if (!touch) return;

    this.capture('touch', {
      x: touch.clientX,
      y: touch.clientY,
      type: e.type,
      touchCount: e.touches.length,
    });
  };

  private handleVisibility = (): void => {
    this.capture('visibility', {
      state: document.visibilityState,
    });
  };

  private handleFocus = (): void => {
    this.capture('focus', { focused: true });
  };

  private handleBlur = (): void => {
    this.capture('focus', { focused: false });
  };

  private handlePaste = (e: ClipboardEvent): void => {
    // Don't capture content, just the fact that paste occurred
    const text = e.clipboardData?.getData('text') ?? '';
    this.capture('paste', {
      length: text.length,
    });
  };

  private handleUnload = (): void => {
    // Use sendBeacon for reliable delivery
    this.flush(true);
  };

  // ─────────────────────────────────────────────────────────────
  // Device & Performance
  // ─────────────────────────────────────────────────────────────

  private captureDeviceInfo(): void {
    this.capture('device', {
      userAgent: navigator.userAgent,
      language: navigator.language,
      languages: navigator.languages,
      platform: navigator.platform,
      cookieEnabled: navigator.cookieEnabled,
      doNotTrack: navigator.doNotTrack,
      screenWidth: screen.width,
      screenHeight: screen.height,
      screenColorDepth: screen.colorDepth,
      screenPixelRatio: window.devicePixelRatio,
      timezoneOffset: new Date().getTimezoneOffset(),
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    });
  }

  private capturePerformance(): void {
    if (!window.performance) return;

    const timing = performance.timing;
    this.capture('performance', {
      navigationStart: timing.navigationStart,
      domContentLoaded: timing.domContentLoadedEventEnd - timing.navigationStart,
      loadComplete: timing.loadEventEnd - timing.navigationStart,
      redirectCount: performance.navigation?.redirectCount ?? 0,
    });
  }

  // ─────────────────────────────────────────────────────────────
  // Network
  // ─────────────────────────────────────────────────────────────

  private startFlushTimer(): void {
    this.flushTimer = setInterval(() => {
      if (this.buffer.length > 0) {
        this.flush();
      }
    }, this.config.flushInterval);
  }

  private stopFlushTimer(): void {
    if (this.flushTimer) {
      clearInterval(this.flushTimer);
      this.flushTimer = null;
    }
  }

  private async flush(useBeacon = false): Promise<void> {
    if (!this.session || this.buffer.length === 0) return;

    const signals = this.buffer.flush();
    const payload = JSON.stringify({
      sessionId: this.session.id,
      signals,
    });

    const url = `${this.config.endpoint}/api/v1/sessions/${this.session.id}/signals`;

    if (useBeacon && navigator.sendBeacon) {
      navigator.sendBeacon(url, payload);
      this.log('Flushed (beacon)', { count: signals.length });
    } else {
      try {
        await fetch(url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: payload,
          keepalive: true,
        });
        this.log('Flushed (fetch)', { count: signals.length });
      } catch (err) {
        this.log('Flush failed', { error: err });
        // Re-add signals to buffer for retry
        signals.forEach((s) => this.buffer.push(s));
      }
    }
  }

  private async sendComplete(): Promise<void> {
    if (!this.session) return;

    const url = `${this.config.endpoint}/api/v1/sessions/${this.session.id}/complete`;

    try {
      await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ completedAt: Date.now() }),
      });
    } catch (err) {
      this.log('Complete failed', { error: err });
    }
  }

  private log(message: string, data?: Record<string, unknown>): void {
    if (this.config.debug) {
      console.log(`[FraudTracker] ${message}`, data ?? '');
    }
  }
}

// Export singleton instance
const tracker = new FraudTracker();

export { FraudTracker };
export default tracker;
