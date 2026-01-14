import Foundation

/// Signal types captured by the FraudTracker
public enum SignalType: String, Codable {
    case touch = "touch"
    case gesture = "gesture"
    case accelerometer = "accelerometer"
    case gyroscope = "gyroscope"
    case device = "device"
    case appLifecycle = "app_lifecycle"
    case jailbreak = "jailbreak"
}

/// A captured behavioral signal
public struct Signal: Codable {
    public let type: SignalType
    public let timestamp: TimeInterval
    public let payload: [String: AnyCodable]
    
    public init(type: SignalType, timestamp: TimeInterval = Date().timeIntervalSince1970 * 1000, payload: [String: AnyCodable]) {
        self.type = type
        self.timestamp = timestamp
        self.payload = payload
    }
}

/// Session information
public struct Session {
    public let id: String
    public let clientId: String
    public let startedAt: TimeInterval
    public let deviceFingerprint: String
}

/// Configuration for FraudTracker
public struct FraudTrackerConfig {
    public let endpoint: String
    public let clientId: String
    public var batchSize: Int = 50
    public var flushInterval: TimeInterval = 0.5
    public var debug: Bool = false
    
    public init(endpoint: String, clientId: String) {
        self.endpoint = endpoint
        self.clientId = clientId
    }
}

/// Main FraudTracker class for iOS
public class FraudTracker {
    
    public static let shared = FraudTracker()
    
    private var config: FraudTrackerConfig?
    private var session: Session?
    private var signalBuffer: [Signal] = []
    private var flushTimer: Timer?
    private var isInitialized = false
    
    private init() {}
    
    // MARK: - Public API
    
    /// Initialize the tracker with configuration
    public func initialize(config: FraudTrackerConfig) {
        guard !isInitialized else {
            log("Already initialized")
            return
        }
        
        self.config = config
        self.session = createSession()
        self.isInitialized = true
        
        attachObservers()
        startFlushTimer()
        captureDeviceInfo()
        checkJailbreak()
        
        log("Initialized with session: \(session?.id ?? "unknown")")
    }
    
    /// Capture a custom signal
    public func capture(type: SignalType, payload: [String: AnyCodable]) {
        guard isInitialized else {
            print("[FraudTracker] Not initialized. Call initialize() first.")
            return
        }
        
        let signal = Signal(type: type, payload: payload)
        signalBuffer.append(signal)
        
        if let config = config, signalBuffer.count >= config.batchSize {
            flush()
        }
    }
    
    /// Complete the session and flush remaining signals
    public func complete(completion: (() -> Void)? = nil) {
        guard isInitialized, session != nil else { return }
        
        stopFlushTimer()
        detachObservers()
        flush()
        sendComplete()
        
        log("Session completed: \(session?.id ?? "unknown")")
        session = nil
        isInitialized = false
        
        completion?()
    }
    
    /// Get current session ID
    public func getSessionId() -> String? {
        return session?.id
    }
    
    // MARK: - Private Methods
    
    private func createSession() -> Session {
        return Session(
            id: UUID().uuidString,
            clientId: config?.clientId ?? "",
            startedAt: Date().timeIntervalSince1970 * 1000,
            deviceFingerprint: generateFingerprint()
        )
    }
    
    private func generateFingerprint() -> String {
        // Basic fingerprint - will be enhanced
        let device = UIDevice.current
        let components = [
            device.model,
            device.systemName,
            device.systemVersion,
            UIScreen.main.bounds.width.description,
            UIScreen.main.bounds.height.description,
            TimeZone.current.identifier
        ]
        return components.joined(separator: "|").hash.description
    }
    
    private func attachObservers() {
        // App lifecycle
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(appDidBecomeActive),
            name: UIApplication.didBecomeActiveNotification,
            object: nil
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(appWillResignActive),
            name: UIApplication.willResignActiveNotification,
            object: nil
        )
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(appDidEnterBackground),
            name: UIApplication.didEnterBackgroundNotification,
            object: nil
        )
        
        // TODO: Add motion/accelerometer observers in Phase 1
    }
    
    private func detachObservers() {
        NotificationCenter.default.removeObserver(self)
    }
    
    @objc private func appDidBecomeActive() {
        capture(type: .appLifecycle, payload: ["state": AnyCodable("active")])
    }
    
    @objc private func appWillResignActive() {
        capture(type: .appLifecycle, payload: ["state": AnyCodable("inactive")])
    }
    
    @objc private func appDidEnterBackground() {
        capture(type: .appLifecycle, payload: ["state": AnyCodable("background")])
        flush()
    }
    
    private func captureDeviceInfo() {
        let device = UIDevice.current
        let screen = UIScreen.main
        
        capture(type: .device, payload: [
            "model": AnyCodable(device.model),
            "systemName": AnyCodable(device.systemName),
            "systemVersion": AnyCodable(device.systemVersion),
            "identifierForVendor": AnyCodable(device.identifierForVendor?.uuidString ?? "unknown"),
            "screenWidth": AnyCodable(screen.bounds.width),
            "screenHeight": AnyCodable(screen.bounds.height),
            "screenScale": AnyCodable(screen.scale),
            "timezone": AnyCodable(TimeZone.current.identifier),
            "locale": AnyCodable(Locale.current.identifier)
        ])
    }
    
    private func checkJailbreak() {
        var isJailbroken = false
        
        #if !targetEnvironment(simulator)
        // Check for common jailbreak paths
        let paths = [
            "/Applications/Cydia.app",
            "/Library/MobileSubstrate/MobileSubstrate.dylib",
            "/bin/bash",
            "/usr/sbin/sshd",
            "/etc/apt",
            "/private/var/lib/apt/"
        ]
        
        for path in paths {
            if FileManager.default.fileExists(atPath: path) {
                isJailbroken = true
                break
            }
        }
        
        // Check if app can write to private directory
        let testPath = "/private/jailbreak_test.txt"
        do {
            try "test".write(toFile: testPath, atomically: true, encoding: .utf8)
            try FileManager.default.removeItem(atPath: testPath)
            isJailbroken = true
        } catch {
            // Expected on non-jailbroken device
        }
        #endif
        
        capture(type: .jailbreak, payload: ["detected": AnyCodable(isJailbroken)])
    }
    
    // MARK: - Network
    
    private func startFlushTimer() {
        guard let config = config else { return }
        
        flushTimer = Timer.scheduledTimer(withTimeInterval: config.flushInterval, repeats: true) { [weak self] _ in
            if let count = self?.signalBuffer.count, count > 0 {
                self?.flush()
            }
        }
    }
    
    private func stopFlushTimer() {
        flushTimer?.invalidate()
        flushTimer = nil
    }
    
    private func flush() {
        guard let session = session, let config = config, !signalBuffer.isEmpty else { return }
        
        let signals = signalBuffer
        signalBuffer = []
        
        let url = URL(string: "\(config.endpoint)/api/v1/sessions/\(session.id)/signals")!
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        
        let payload: [String: Any] = [
            "sessionId": session.id,
            "signals": signals.map { signal in
                [
                    "type": signal.type.rawValue,
                    "timestamp": signal.timestamp,
                    "payload": signal.payload
                ] as [String : Any]
            }
        ]
        
        request.httpBody = try? JSONSerialization.data(withJSONObject: payload)
        
        URLSession.shared.dataTask(with: request) { [weak self] _, response, error in
            if let error = error {
                self?.log("Flush failed: \(error)")
                // Re-add signals for retry
                self?.signalBuffer.insert(contentsOf: signals, at: 0)
            } else {
                self?.log("Flushed \(signals.count) signals")
            }
        }.resume()
    }
    
    private func sendComplete() {
        guard let session = session, let config = config else { return }
        
        let url = URL(string: "\(config.endpoint)/api/v1/sessions/\(session.id)/complete")!
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONSerialization.data(withJSONObject: ["completedAt": Date().timeIntervalSince1970 * 1000])
        
        URLSession.shared.dataTask(with: request).resume()
    }
    
    private func log(_ message: String) {
        if config?.debug == true {
            print("[FraudTracker] \(message)")
        }
    }
}

// MARK: - AnyCodable Helper

public struct AnyCodable: Codable {
    public let value: Any
    
    public init(_ value: Any) {
        self.value = value
    }
    
    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        
        if let bool = try? container.decode(Bool.self) {
            value = bool
        } else if let int = try? container.decode(Int.self) {
            value = int
        } else if let double = try? container.decode(Double.self) {
            value = double
        } else if let string = try? container.decode(String.self) {
            value = string
        } else if let array = try? container.decode([AnyCodable].self) {
            value = array.map { $0.value }
        } else if let dict = try? container.decode([String: AnyCodable].self) {
            value = dict.mapValues { $0.value }
        } else {
            value = NSNull()
        }
    }
    
    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        
        switch value {
        case let bool as Bool:
            try container.encode(bool)
        case let int as Int:
            try container.encode(int)
        case let double as Double:
            try container.encode(double)
        case let string as String:
            try container.encode(string)
        case let array as [Any]:
            try container.encode(array.map { AnyCodable($0) })
        case let dict as [String: Any]:
            try container.encode(dict.mapValues { AnyCodable($0) })
        default:
            try container.encodeNil()
        }
    }
}
