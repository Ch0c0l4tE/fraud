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
  /** Callback when a signal is captured */
  onSignal?: (signal: Signal) => void;
  /** Callback when signals are flushed to API */
  onFlush?: (signals: Signal[]) => void;
  /** Callback when typing statistics are updated */
  onTypingStats?: (stats: TypingStatistics) => void;
  /** Callback when session is initialized */
  onSessionStart?: (session: Session) => void;
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
  | 'keystroke_dynamics'
  | 'scroll'
  | 'touch'
  | 'visibility'
  | 'focus'
  | 'paste'
  | 'device'
  | 'performance'
  | 'fingerprint'
  | 'form_interaction';

export interface Session {
  id: string;
  clientId: string;
  startedAt: number;
  deviceFingerprint: string;
}

// ─────────────────────────────────────────────────────────────
// Keystroke Dynamics Types
// ─────────────────────────────────────────────────────────────

/** Individual keystroke timing data */
export interface KeystrokeEvent {
  /** Key code (not the character for privacy) */
  keyCode: string;
  /** Time key was pressed down */
  downTime: number;
  /** Time key was released */
  upTime: number;
  /** Dwell time: how long key was held (upTime - downTime) */
  dwellTimeMs: number;
  /** Flight time: time since last key release (downTime - prevUpTime) */
  flightTimeMs: number;
  /** Modifier keys state */
  modifiers: {
    shift: boolean;
    ctrl: boolean;
    alt: boolean;
    meta: boolean;
  };
}

/** Digraph timing (two consecutive keys) */
export interface DigraphTiming {
  /** First key code */
  key1: string;
  /** Second key code */
  key2: string;
  /** Time between key1 down and key2 down */
  keyToKeyMs: number;
  /** Combined dwell time */
  totalDwellMs: number;
}

/** Aggregate typing statistics for a session */
export interface TypingStatistics {
  /** Total keystrokes captured */
  totalKeystrokes: number;
  /** Average dwell time */
  avgDwellTimeMs: number;
  /** Standard deviation of dwell time */
  stdDwellTimeMs: number;
  /** Average flight time */
  avgFlightTimeMs: number;
  /** Standard deviation of flight time */
  stdFlightTimeMs: number;
  /** Words per minute estimate */
  estimatedWPM: number;
  /** Error rate (backspace/delete ratio) */
  errorRate: number;
  /** Typing burst count (pauses > 2s) */
  burstCount: number;
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
  private config: FraudTrackerConfig & Required<Pick<FraudTrackerConfig, 'endpoint' | 'clientId' | 'batchSize' | 'flushInterval' | 'debug'>>;
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
    this.startStatsFlushTimer();
    this.captureDeviceInfo();
    this.capturePerformance();
    this.captureFingerprint();

    // Fire onSessionStart callback if provided
    this.config.onSessionStart?.(this.session);

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

    // Fire onSignal callback if provided
    this.config.onSignal?.(signal);

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

    // Emit final typing statistics before completing
    this.emitTypingStatistics();
    
    this.stopFlushTimer();
    this.stopStatsFlushTimer();
    this.detachListeners();
    await this.flush();
    await this.sendComplete();

    this.log('Session completed', { sessionId: this.session.id });
    
    // Clean up keystroke dynamics state
    this.resetKeystrokeDynamics();
    
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

  private lastMousePosition = { x: 0, y: 0, time: 0, velocity: 0 };

  private handleMouseMove = (e: MouseEvent): void => {
    const now = Date.now();
    const dx = e.clientX - this.lastMousePosition.x;
    const dy = e.clientY - this.lastMousePosition.y;
    const dt = now - this.lastMousePosition.time || 1;
    const distance = Math.sqrt(dx * dx + dy * dy);
    const velocity = distance / dt;
    
    // Calculate acceleration (change in velocity over time)
    const dv = velocity - this.lastMousePosition.velocity;
    const acceleration = dt > 0 ? dv / dt : 0;

    this.capture('mouse_move', {
      x: e.clientX,
      y: e.clientY,
      velocity,
      acceleration,
      dx,
      dy,
      distance,
      dt,
    });

    this.lastMousePosition = { x: e.clientX, y: e.clientY, time: now, velocity };
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

  // ─────────────────────────────────────────────────────────────
  // Enhanced Keystroke Dynamics State
  // ─────────────────────────────────────────────────────────────
  
  /** Last key release timestamp for flight time calculation */
  private lastKeyUpTime = 0;
  /** Last key code for digraph analysis */
  private lastKeyCode = '';
  /** All dwell times for statistics */
  private dwellTimes: number[] = [];
  /** All flight times for statistics */
  private flightTimes: number[] = [];
  /** Key-to-key timings for digraph patterns */
  private digraphTimings: DigraphTiming[] = [];
  /** Backspace/delete count for error rate */
  private errorKeyCount = 0;
  /** Total character keys pressed */
  private charKeyCount = 0;
  /** Typing burst tracking */
  private burstStartTime = 0;
  private burstCount = 0;
  /** Flush stats interval (emit aggregate stats periodically) */
  private statsFlushTimer: ReturnType<typeof setInterval> | null = null;
  private readonly STATS_FLUSH_INTERVAL = 5000; // 5 seconds
  private readonly BURST_PAUSE_THRESHOLD = 2000; // 2 seconds = new burst

  private handleKeyDown = (e: KeyboardEvent): void => {
    const now = Date.now();
    const keyId = e.code;
    
    // Avoid repeat events from holding key down
    if (this.keyTimes.has(keyId)) {
      return;
    }
    
    this.keyTimes.set(keyId, now);
    
    // Track digraph timing (key-to-key interval)
    if (this.lastKeyCode && this.lastKeyUpTime > 0) {
      const keyToKeyMs = now - (this.keyTimes.get(this.lastKeyCode) || now);
      
      // Only track reasonable intervals (< 2 seconds)
      if (keyToKeyMs > 0 && keyToKeyMs < 2000) {
        this.digraphTimings.push({
          key1: this.lastKeyCode,
          key2: keyId,
          keyToKeyMs,
          totalDwellMs: 0, // Will be updated on key up
        });
        
        // Keep only last 100 digraphs to limit memory
        if (this.digraphTimings.length > 100) {
          this.digraphTimings.shift();
        }
      }
    }
    
    // Detect typing bursts (pauses > 2s indicate new burst)
    if (this.lastKeyUpTime > 0 && (now - this.lastKeyUpTime) > this.BURST_PAUSE_THRESHOLD) {
      this.burstCount++;
      this.burstStartTime = now;
    } else if (this.burstStartTime === 0) {
      this.burstStartTime = now;
      this.burstCount = 1;
    }
  };

  private handleKeyUp = (e: KeyboardEvent): void => {
    const now = Date.now();
    const keyId = e.code;
    const downTime = this.keyTimes.get(keyId);
    
    if (!downTime) {
      return;
    }
    
    // Calculate dwell time (key held duration)
    const dwellTimeMs = now - downTime;
    
    // Calculate flight time (time since last key release)
    const flightTimeMs = this.lastKeyUpTime > 0 ? downTime - this.lastKeyUpTime : 0;
    
    // Track error keys
    if (keyId === 'Backspace' || keyId === 'Delete') {
      this.errorKeyCount++;
    }
    
    // Track character keys (letters, numbers, punctuation)
    if (this.isCharacterKey(keyId)) {
      this.charKeyCount++;
    }
    
    // Store for aggregate statistics
    this.dwellTimes.push(dwellTimeMs);
    if (flightTimeMs > 0 && flightTimeMs < 2000) {
      this.flightTimes.push(flightTimeMs);
    }
    
    // Limit stored timings to prevent memory bloat
    if (this.dwellTimes.length > 1000) this.dwellTimes.shift();
    if (this.flightTimes.length > 1000) this.flightTimes.shift();
    
    // Update last digraph with total dwell time
    if (this.digraphTimings.length > 0) {
      const lastDigraph = this.digraphTimings[this.digraphTimings.length - 1];
      if (lastDigraph.key2 === keyId) {
        lastDigraph.totalDwellMs = dwellTimeMs;
      }
    }
    
    // Build keystroke event
    const keystrokeEvent: KeystrokeEvent = {
      keyCode: keyId,
      downTime,
      upTime: now,
      dwellTimeMs,
      flightTimeMs,
      modifiers: {
        shift: e.shiftKey,
        ctrl: e.ctrlKey,
        alt: e.altKey,
        meta: e.metaKey,
      },
    };
    
    // Capture individual keystroke signal
    this.capture('keystroke', {
      keyCode: keystrokeEvent.keyCode,
      dwellTimeMs: keystrokeEvent.dwellTimeMs,
      flightTimeMs: keystrokeEvent.flightTimeMs,
      shiftKey: keystrokeEvent.modifiers.shift,
      ctrlKey: keystrokeEvent.modifiers.ctrl,
      altKey: keystrokeEvent.modifiers.alt,
      metaKey: keystrokeEvent.modifiers.meta,
    });
    
    // Update state for next keystroke
    this.lastKeyUpTime = now;
    this.lastKeyCode = keyId;
    this.keyTimes.delete(keyId);
  };

  /**
   * Check if a key code represents a character input
   */
  private isCharacterKey(keyCode: string): boolean {
    return (
      keyCode.startsWith('Key') ||
      keyCode.startsWith('Digit') ||
      keyCode.startsWith('Numpad') ||
      ['Space', 'Period', 'Comma', 'Semicolon', 'Quote', 'Slash', 
       'Backslash', 'BracketLeft', 'BracketRight', 'Minus', 'Equal'].includes(keyCode)
    );
  }

  /**
   * Calculate and emit aggregate typing statistics
   */
  private emitTypingStatistics(): void {
    if (this.dwellTimes.length < 5) {
      return; // Not enough data
    }
    
    const stats = this.calculateTypingStatistics();
    
    // Fire onTypingStats callback if provided
    this.config.onTypingStats?.(stats);
    
    this.capture('keystroke_dynamics', {
      ...stats,
      // Include recent digraph patterns (anonymized)
      recentDigraphCount: this.digraphTimings.length,
      avgDigraphKeyToKeyMs: this.calculateMean(
        this.digraphTimings.slice(-20).map(d => d.keyToKeyMs)
      ),
    });
  }

  /**
   * Calculate aggregate typing statistics
   */
  private calculateTypingStatistics(): TypingStatistics {
    const avgDwellTimeMs = this.calculateMean(this.dwellTimes);
    const stdDwellTimeMs = this.calculateStdDev(this.dwellTimes, avgDwellTimeMs);
    const avgFlightTimeMs = this.calculateMean(this.flightTimes);
    const stdFlightTimeMs = this.calculateStdDev(this.flightTimes, avgFlightTimeMs);
    
    // Estimate WPM: average word = 5 characters
    // Time per character = avgFlightTimeMs + avgDwellTimeMs
    const msPerChar = avgFlightTimeMs + avgDwellTimeMs;
    const charsPerMinute = msPerChar > 0 ? 60000 / msPerChar : 0;
    const estimatedWPM = Math.round(charsPerMinute / 5);
    
    // Error rate: (backspace + delete) / total chars
    const errorRate = this.charKeyCount > 0 
      ? this.errorKeyCount / this.charKeyCount 
      : 0;
    
    return {
      totalKeystrokes: this.dwellTimes.length,
      avgDwellTimeMs: Math.round(avgDwellTimeMs * 100) / 100,
      stdDwellTimeMs: Math.round(stdDwellTimeMs * 100) / 100,
      avgFlightTimeMs: Math.round(avgFlightTimeMs * 100) / 100,
      stdFlightTimeMs: Math.round(stdFlightTimeMs * 100) / 100,
      estimatedWPM,
      errorRate: Math.round(errorRate * 1000) / 1000,
      burstCount: this.burstCount,
    };
  }

  /**
   * Calculate mean of an array
   */
  private calculateMean(values: number[]): number {
    if (values.length === 0) return 0;
    return values.reduce((sum, v) => sum + v, 0) / values.length;
  }

  /**
   * Calculate standard deviation
   */
  private calculateStdDev(values: number[], mean?: number): number {
    if (values.length < 2) return 0;
    const m = mean ?? this.calculateMean(values);
    const squaredDiffs = values.map(v => Math.pow(v - m, 2));
    return Math.sqrt(this.calculateMean(squaredDiffs));
  }

  /**
   * Start periodic emission of typing statistics
   */
  private startStatsFlushTimer(): void {
    this.statsFlushTimer = setInterval(() => {
      this.emitTypingStatistics();
    }, this.STATS_FLUSH_INTERVAL);
  }

  /**
   * Stop stats flush timer
   */
  private stopStatsFlushTimer(): void {
    if (this.statsFlushTimer) {
      clearInterval(this.statsFlushTimer);
      this.statsFlushTimer = null;
    }
  }

  /**
   * Reset keystroke dynamics state
   */
  private resetKeystrokeDynamics(): void {
    this.keyTimes.clear();
    this.lastKeyUpTime = 0;
    this.lastKeyCode = '';
    this.dwellTimes = [];
    this.flightTimes = [];
    this.digraphTimings = [];
    this.errorKeyCount = 0;
    this.charKeyCount = 0;
    this.burstStartTime = 0;
    this.burstCount = 0;
  }

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
  // Form Interaction Tracking
  // ─────────────────────────────────────────────────────────────

  /** Track form field interactions */
  private formFieldData: Map<string, {
    fieldId: string;
    fieldType: string;
    focusTime: number;
    blurTime: number;
    keystrokeCount: number;
    pasteCount: number;
    focusOrder: number;
    corrections: number;
  }> = new Map();

  /** Current field focus order counter */
  private formFocusOrder = 0;

  /** Currently focused field */
  private currentFormField: string | null = null;

  /**
   * Start tracking a form for interaction patterns
   * Call this to enable form-level analytics
   */
  trackForm(formOrSelector: HTMLFormElement | string): void {
    const form = typeof formOrSelector === 'string'
      ? document.querySelector<HTMLFormElement>(formOrSelector)
      : formOrSelector;

    if (!form) {
      this.log('Form not found', { selector: formOrSelector });
      return;
    }

    // Track all input fields
    const fields = form.querySelectorAll('input, textarea, select');
    fields.forEach((field) => {
      this.attachFormFieldListeners(field as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement);
    });

    // Track form submission
    form.addEventListener('submit', this.handleFormSubmit);

    this.log('Form tracking started', { fieldCount: fields.length });
  }

  private attachFormFieldListeners(field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement): void {
    const fieldId = field.id || field.name || `field_${Math.random().toString(36).slice(2, 8)}`;

    field.addEventListener('focus', () => this.handleFormFieldFocus(fieldId, field));
    field.addEventListener('blur', () => this.handleFormFieldBlur(fieldId, field));
    field.addEventListener('input', () => this.handleFormFieldInput(fieldId, field));
    field.addEventListener('paste', () => this.handleFormFieldPaste(fieldId));
  }

  private handleFormFieldFocus = (fieldId: string, field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement): void => {
    const now = Date.now();
    this.formFocusOrder++;
    this.currentFormField = fieldId;

    if (!this.formFieldData.has(fieldId)) {
      this.formFieldData.set(fieldId, {
        fieldId,
        fieldType: field.type || field.tagName.toLowerCase(),
        focusTime: now,
        blurTime: 0,
        keystrokeCount: 0,
        pasteCount: 0,
        focusOrder: this.formFocusOrder,
        corrections: 0,
      });
    } else {
      const data = this.formFieldData.get(fieldId)!;
      data.focusTime = now;
      // Track re-focus as potential correction behavior
      if (data.blurTime > 0) {
        data.corrections++;
      }
    }
  };

  private handleFormFieldBlur = (fieldId: string, field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement): void => {
    const now = Date.now();
    const data = this.formFieldData.get(fieldId);
    
    if (data) {
      data.blurTime = now;
      const timeToFill = now - data.focusTime;

      this.capture('form_interaction', {
        event: 'field_complete',
        fieldId,
        fieldType: data.fieldType,
        timeToFillMs: timeToFill,
        keystrokeCount: data.keystrokeCount,
        pasteCount: data.pasteCount,
        focusOrder: data.focusOrder,
        corrections: data.corrections,
        valueLength: (field as HTMLInputElement).value?.length ?? 0,
        // Typing speed for this field
        typingSpeed: data.keystrokeCount > 0 ? (data.keystrokeCount / (timeToFill / 1000)) : 0,
      });
    }

    this.currentFormField = null;
  };

  private handleFormFieldInput = (fieldId: string, field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement): void => {
    const data = this.formFieldData.get(fieldId);
    if (data) {
      data.keystrokeCount++;
    }
  };

  private handleFormFieldPaste = (fieldId: string): void => {
    const data = this.formFieldData.get(fieldId);
    if (data) {
      data.pasteCount++;
    }
  };

  private handleFormSubmit = (e: Event): void => {
    const form = e.target as HTMLFormElement;
    const formFields = Array.from(this.formFieldData.values());

    // Calculate form-level statistics
    const totalFields = formFields.length;
    const filledFields = formFields.filter(f => f.keystrokeCount > 0 || f.pasteCount > 0).length;
    const totalTimeMs = formFields.reduce((sum, f) => sum + (f.blurTime - f.focusTime), 0);
    const totalKeystrokes = formFields.reduce((sum, f) => sum + f.keystrokeCount, 0);
    const totalPastes = formFields.reduce((sum, f) => sum + f.pasteCount, 0);
    const totalCorrections = formFields.reduce((sum, f) => sum + f.corrections, 0);

    // Check focus order - was it sequential or jumpy?
    const focusOrders = formFields.map(f => f.focusOrder).sort((a, b) => a - b);
    let sequentialFocusCount = 0;
    for (let i = 1; i < focusOrders.length; i++) {
      if (focusOrders[i] === focusOrders[i - 1] + 1) {
        sequentialFocusCount++;
      }
    }
    const focusSequentialRatio = focusOrders.length > 1 
      ? sequentialFocusCount / (focusOrders.length - 1) 
      : 1;

    this.capture('form_interaction', {
      event: 'form_submit',
      formId: form.id || form.name || 'unknown',
      totalFields,
      filledFields,
      totalTimeMs,
      totalKeystrokes,
      totalPastes,
      totalCorrections,
      focusSequentialRatio,
      avgTimePerFieldMs: totalFields > 0 ? totalTimeMs / totalFields : 0,
      pasteRatio: totalFields > 0 ? totalPastes / totalFields : 0,
      // Field-by-field summary
      fieldSummary: formFields.map(f => ({
        fieldType: f.fieldType,
        timeToFillMs: f.blurTime - f.focusTime,
        keystrokeCount: f.keystrokeCount,
        wasPasted: f.pasteCount > 0,
      })),
    });

    // Reset form tracking data
    this.formFieldData.clear();
    this.formFocusOrder = 0;
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
  // Device Fingerprinting
  // ─────────────────────────────────────────────────────────────

  /**
   * Capture comprehensive device fingerprint
   * Called once during initialization
   */
  private captureFingerprint(): void {
    const fingerprint = {
      canvas: this.getCanvasFingerprint(),
      webgl: this.getWebGLFingerprint(),
      audio: this.getAudioFingerprint(),
      fonts: this.getInstalledFonts(),
      hardware: this.getHardwareInfo(),
      timezone: this.getTimezoneInfo(),
    };

    this.capture('fingerprint', {
      ...fingerprint,
      hash: this.hashFingerprint(fingerprint),
    });
  }

  /**
   * Canvas fingerprinting - draws various elements and hashes the result
   * Different browsers/GPUs produce unique outputs
   */
  private getCanvasFingerprint(): string {
    try {
      const canvas = document.createElement('canvas');
      canvas.width = 280;
      canvas.height = 60;
      const ctx = canvas.getContext('2d');
      if (!ctx) return 'unsupported';

      // Background
      ctx.fillStyle = '#f0f0f0';
      ctx.fillRect(0, 0, canvas.width, canvas.height);

      // Text with specific font rendering
      ctx.textBaseline = 'alphabetic';
      ctx.fillStyle = '#069';
      ctx.font = '14px Arial, sans-serif';
      ctx.fillText('Fraud Detection Canvas 🔍', 4, 17);

      // Styled text
      ctx.fillStyle = 'rgba(102, 204, 0, 0.7)';
      ctx.font = '18px Georgia, serif';
      ctx.fillText('Canvas Fingerprint', 4, 40);

      // Geometric shapes
      ctx.beginPath();
      ctx.arc(230, 30, 20, 0, Math.PI * 2);
      ctx.fillStyle = 'rgba(255, 0, 128, 0.5)';
      ctx.fill();

      // Gradient
      const gradient = ctx.createLinearGradient(0, 50, 280, 50);
      gradient.addColorStop(0, '#ff0000');
      gradient.addColorStop(0.5, '#00ff00');
      gradient.addColorStop(1, '#0000ff');
      ctx.fillStyle = gradient;
      ctx.fillRect(0, 52, 280, 8);

      // Get data URL and hash it
      const dataURL = canvas.toDataURL('image/png');
      return this.simpleHash(dataURL);
    } catch (e) {
      return 'error';
    }
  }

  /**
   * WebGL fingerprinting - extracts GPU/driver information
   * Critical for detecting VMs and headless browsers
   */
  private getWebGLFingerprint(): Record<string, unknown> {
    try {
      const canvas = document.createElement('canvas');
      const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
      if (!gl || !(gl instanceof WebGLRenderingContext)) {
        return { supported: false };
      }

      const debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
      const result: Record<string, unknown> = {
        supported: true,
        vendor: gl.getParameter(gl.VENDOR),
        renderer: gl.getParameter(gl.RENDERER),
        version: gl.getParameter(gl.VERSION),
        shadingLanguageVersion: gl.getParameter(gl.SHADING_LANGUAGE_VERSION),
      };

      if (debugInfo) {
        result.unmaskedVendor = gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL);
        result.unmaskedRenderer = gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL);
      }

      // Get supported extensions
      result.extensions = gl.getSupportedExtensions()?.slice(0, 20) ?? [];
      result.extensionCount = gl.getSupportedExtensions()?.length ?? 0;

      // Max texture size (differs by GPU)
      result.maxTextureSize = gl.getParameter(gl.MAX_TEXTURE_SIZE);
      result.maxViewportDims = gl.getParameter(gl.MAX_VIEWPORT_DIMS);
      result.maxRenderbufferSize = gl.getParameter(gl.MAX_RENDERBUFFER_SIZE);

      // Antialiasing
      result.antialias = gl.getContextAttributes()?.antialias;

      return result;
    } catch (e) {
      return { supported: false, error: true };
    }
  }

  /**
   * Audio fingerprinting - uses AudioContext to generate unique signature
   * Based on oscillator and compressor processing differences
   */
  private getAudioFingerprint(): string {
    try {
      // @ts-expect-error - webkitAudioContext for Safari
      const AudioContext = window.AudioContext || window.webkitAudioContext;
      if (!AudioContext) return 'unsupported';

      const context = new AudioContext();
      const oscillator = context.createOscillator();
      const analyser = context.createAnalyser();
      const gain = context.createGain();
      const compressor = context.createDynamicsCompressor();

      // Configure oscillator
      oscillator.type = 'triangle';
      oscillator.frequency.setValueAtTime(10000, context.currentTime);

      // Configure compressor
      compressor.threshold.setValueAtTime(-50, context.currentTime);
      compressor.knee.setValueAtTime(40, context.currentTime);
      compressor.ratio.setValueAtTime(12, context.currentTime);
      compressor.attack.setValueAtTime(0, context.currentTime);
      compressor.release.setValueAtTime(0.25, context.currentTime);

      // Connect nodes
      oscillator.connect(compressor);
      compressor.connect(analyser);
      analyser.connect(gain);
      gain.connect(context.destination);
      gain.gain.setValueAtTime(0, context.currentTime); // Mute output

      oscillator.start(0);

      // Get frequency data
      const freqData = new Float32Array(analyser.frequencyBinCount);
      analyser.getFloatFrequencyData(freqData);

      // Create fingerprint from frequency data
      let fingerprint = 0;
      for (let i = 0; i < freqData.length; i++) {
        fingerprint += Math.abs(freqData[i]);
      }

      oscillator.stop();
      context.close();

      return fingerprint.toString();
    } catch (e) {
      return 'error';
    }
  }

  /**
   * Font detection - checks for installed fonts
   * Different OS/users have different font sets
   */
  private getInstalledFonts(): string[] {
    const baseFonts = ['monospace', 'sans-serif', 'serif'];
    const testFonts = [
      'Arial', 'Arial Black', 'Arial Narrow', 'Calibri', 'Cambria',
      'Comic Sans MS', 'Consolas', 'Courier New', 'Georgia', 'Helvetica',
      'Impact', 'Lucida Console', 'Lucida Sans', 'Microsoft Sans Serif',
      'Palatino Linotype', 'Segoe UI', 'Tahoma', 'Times New Roman',
      'Trebuchet MS', 'Verdana', 'Monaco', 'Menlo', 'Ubuntu', 'Roboto',
      'Open Sans', 'Source Sans Pro', 'Fira Code', 'SF Pro',
    ];

    const detectedFonts: string[] = [];

    try {
      const canvas = document.createElement('canvas');
      const ctx = canvas.getContext('2d');
      if (!ctx) return [];

      const testString = 'mmmmmmmmmmlli';
      const testSize = '72px';

      // Get baseline widths
      const baseWidths: Record<string, number> = {};
      for (const baseFont of baseFonts) {
        ctx.font = `${testSize} ${baseFont}`;
        baseWidths[baseFont] = ctx.measureText(testString).width;
      }

      // Test each font
      for (const font of testFonts) {
        let detected = false;
        for (const baseFont of baseFonts) {
          ctx.font = `${testSize} '${font}', ${baseFont}`;
          const width = ctx.measureText(testString).width;
          if (width !== baseWidths[baseFont]) {
            detected = true;
            break;
          }
        }
        if (detected) {
          detectedFonts.push(font);
        }
      }
    } catch (e) {
      // Ignore errors
    }

    return detectedFonts;
  }

  /**
   * Hardware info - additional device characteristics
   */
  private getHardwareInfo(): Record<string, unknown> {
    const nav = navigator as Navigator & {
      deviceMemory?: number;
      hardwareConcurrency?: number;
      connection?: {
        effectiveType?: string;
        downlink?: number;
        rtt?: number;
        saveData?: boolean;
      };
      getBattery?: () => Promise<{
        charging: boolean;
        chargingTime: number;
        dischargingTime: number;
        level: number;
      }>;
    };

    return {
      deviceMemory: nav.deviceMemory ?? 'unknown',
      hardwareConcurrency: nav.hardwareConcurrency ?? 'unknown',
      maxTouchPoints: navigator.maxTouchPoints ?? 0,
      connection: nav.connection ? {
        effectiveType: nav.connection.effectiveType,
        downlink: nav.connection.downlink,
        rtt: nav.connection.rtt,
        saveData: nav.connection.saveData,
      } : 'unsupported',
    };
  }

  /**
   * Timezone information - detect mismatches for proxy/VPN detection
   */
  private getTimezoneInfo(): Record<string, unknown> {
    const date = new Date();
    const jan = new Date(date.getFullYear(), 0, 1);
    const jul = new Date(date.getFullYear(), 6, 1);

    return {
      timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      timezoneOffset: date.getTimezoneOffset(),
      // DST detection
      stdOffset: Math.max(jan.getTimezoneOffset(), jul.getTimezoneOffset()),
      dstOffset: Math.min(jan.getTimezoneOffset(), jul.getTimezoneOffset()),
      hasDST: jan.getTimezoneOffset() !== jul.getTimezoneOffset(),
      // Locale info
      locale: Intl.DateTimeFormat().resolvedOptions().locale,
      dateFormat: new Intl.DateTimeFormat().format(date),
      // Time format preference
      hour12: new Intl.DateTimeFormat(undefined, { hour: 'numeric' })
        .resolvedOptions().hour12,
    };
  }

  /**
   * Simple hash function for fingerprint strings
   */
  private simpleHash(str: string): string {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
      const char = str.charCodeAt(i);
      hash = ((hash << 5) - hash) + char;
      hash = hash & hash; // Convert to 32-bit integer
    }
    return Math.abs(hash).toString(16);
  }

  /**
   * Create overall fingerprint hash from all components
   */
  private hashFingerprint(fp: Record<string, unknown>): string {
    const str = JSON.stringify(fp);
    return this.simpleHash(str);
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

    // Fire onFlush callback if provided
    this.config.onFlush?.(signals);

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
