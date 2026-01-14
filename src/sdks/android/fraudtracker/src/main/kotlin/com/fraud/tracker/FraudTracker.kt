package com.fraud.tracker

import android.content.Context
import android.os.Build
import android.util.DisplayMetrics
import android.view.WindowManager
import kotlinx.coroutines.*
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedOutputStream
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.util.*
import java.util.concurrent.CopyOnWriteArrayList

/**
 * Signal types captured by the FraudTracker
 */
enum class SignalType(val value: String) {
    TOUCH("touch"),
    GESTURE("gesture"),
    ACCELEROMETER("accelerometer"),
    GYROSCOPE("gyroscope"),
    DEVICE("device"),
    APP_LIFECYCLE("app_lifecycle"),
    ROOT_DETECTION("root_detection")
}

/**
 * A captured behavioral signal
 */
data class Signal(
    val type: SignalType,
    val timestamp: Long = System.currentTimeMillis(),
    val payload: Map<String, Any?>
)

/**
 * Session information
 */
data class Session(
    val id: String,
    val clientId: String,
    val startedAt: Long,
    val deviceFingerprint: String
)

/**
 * Configuration for FraudTracker
 */
data class FraudTrackerConfig(
    val endpoint: String,
    val clientId: String,
    val batchSize: Int = 50,
    val flushIntervalMs: Long = 500,
    val debug: Boolean = false
)

/**
 * Main FraudTracker class for Android
 */
class FraudTracker private constructor() {

    companion object {
        @Volatile
        private var instance: FraudTracker? = null

        fun getInstance(): FraudTracker {
            return instance ?: synchronized(this) {
                instance ?: FraudTracker().also { instance = it }
            }
        }
    }

    private var config: FraudTrackerConfig? = null
    private var session: Session? = null
    private var signalBuffer = CopyOnWriteArrayList<Signal>()
    private var isInitialized = false
    private var flushJob: Job? = null
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────

    /**
     * Initialize the tracker with configuration
     */
    fun initialize(context: Context, config: FraudTrackerConfig) {
        if (isInitialized) {
            log("Already initialized")
            return
        }

        this.config = config
        this.session = createSession(context)
        this.isInitialized = true

        startFlushTimer()
        captureDeviceInfo(context)
        checkRoot()

        log("Initialized with session: ${session?.id}")
    }

    /**
     * Capture a custom signal
     */
    fun capture(type: SignalType, payload: Map<String, Any?>) {
        if (!isInitialized) {
            println("[FraudTracker] Not initialized. Call initialize() first.")
            return
        }

        val signal = Signal(type = type, payload = payload)
        signalBuffer.add(signal)

        config?.let {
            if (signalBuffer.size >= it.batchSize) {
                flush()
            }
        }
    }

    /**
     * Complete the session and flush remaining signals
     */
    fun complete(callback: (() -> Unit)? = null) {
        if (!isInitialized || session == null) return

        stopFlushTimer()
        flush()
        sendComplete()

        log("Session completed: ${session?.id}")
        session = null
        isInitialized = false

        callback?.invoke()
    }

    /**
     * Get current session ID
     */
    fun getSessionId(): String? = session?.id

    /**
     * Handle app lifecycle - call from Activity
     */
    fun onResume() {
        capture(SignalType.APP_LIFECYCLE, mapOf("state" to "resumed"))
    }

    fun onPause() {
        capture(SignalType.APP_LIFECYCLE, mapOf("state" to "paused"))
    }

    fun onStop() {
        capture(SignalType.APP_LIFECYCLE, mapOf("state" to "stopped"))
        flush()
    }

    // ─────────────────────────────────────────────────────────────
    // Private Methods
    // ─────────────────────────────────────────────────────────────

    private fun createSession(context: Context): Session {
        return Session(
            id = UUID.randomUUID().toString(),
            clientId = config?.clientId ?: "",
            startedAt = System.currentTimeMillis(),
            deviceFingerprint = generateFingerprint(context)
        )
    }

    private fun generateFingerprint(context: Context): String {
        val components = listOf(
            Build.MODEL,
            Build.MANUFACTURER,
            Build.VERSION.SDK_INT.toString(),
            getScreenSize(context),
            TimeZone.getDefault().id
        )
        return components.joinToString("|").hashCode().toString()
    }

    private fun getScreenSize(context: Context): String {
        val windowManager = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val metrics = DisplayMetrics()
        @Suppress("DEPRECATION")
        windowManager.defaultDisplay.getMetrics(metrics)
        return "${metrics.widthPixels}x${metrics.heightPixels}"
    }

    private fun captureDeviceInfo(context: Context) {
        val windowManager = context.getSystemService(Context.WINDOW_SERVICE) as WindowManager
        val metrics = DisplayMetrics()
        @Suppress("DEPRECATION")
        windowManager.defaultDisplay.getMetrics(metrics)

        capture(SignalType.DEVICE, mapOf(
            "model" to Build.MODEL,
            "manufacturer" to Build.MANUFACTURER,
            "brand" to Build.BRAND,
            "device" to Build.DEVICE,
            "sdkVersion" to Build.VERSION.SDK_INT,
            "release" to Build.VERSION.RELEASE,
            "screenWidth" to metrics.widthPixels,
            "screenHeight" to metrics.heightPixels,
            "screenDensity" to metrics.density,
            "timezone" to TimeZone.getDefault().id,
            "locale" to Locale.getDefault().toString()
        ))
    }

    private fun checkRoot() {
        var isRooted = false

        // Check for common root paths
        val paths = listOf(
            "/system/app/Superuser.apk",
            "/sbin/su",
            "/system/bin/su",
            "/system/xbin/su",
            "/data/local/xbin/su",
            "/data/local/bin/su",
            "/system/sd/xbin/su",
            "/system/bin/failsafe/su",
            "/data/local/su",
            "/su/bin/su"
        )

        for (path in paths) {
            if (File(path).exists()) {
                isRooted = true
                break
            }
        }

        // Check build tags
        if (Build.TAGS?.contains("test-keys") == true) {
            isRooted = true
        }

        capture(SignalType.ROOT_DETECTION, mapOf("detected" to isRooted))
    }

    // ─────────────────────────────────────────────────────────────
    // Network
    // ─────────────────────────────────────────────────────────────

    private fun startFlushTimer() {
        flushJob = scope.launch {
            while (isActive) {
                delay(config?.flushIntervalMs ?: 500)
                if (signalBuffer.isNotEmpty()) {
                    flush()
                }
            }
        }
    }

    private fun stopFlushTimer() {
        flushJob?.cancel()
        flushJob = null
    }

    private fun flush() {
        val currentSession = session ?: return
        val currentConfig = config ?: return
        if (signalBuffer.isEmpty()) return

        val signals = signalBuffer.toList()
        signalBuffer.clear()

        scope.launch {
            try {
                val url = URL("${currentConfig.endpoint}/api/v1/sessions/${currentSession.id}/signals")
                val connection = url.openConnection() as HttpURLConnection
                connection.requestMethod = "POST"
                connection.setRequestProperty("Content-Type", "application/json")
                connection.doOutput = true

                val payload = JSONObject().apply {
                    put("sessionId", currentSession.id)
                    put("signals", JSONArray().apply {
                        signals.forEach { signal ->
                            put(JSONObject().apply {
                                put("type", signal.type.value)
                                put("timestamp", signal.timestamp)
                                put("payload", JSONObject(signal.payload))
                            })
                        }
                    })
                }

                BufferedOutputStream(connection.outputStream).use { out ->
                    out.write(payload.toString().toByteArray())
                    out.flush()
                }

                val responseCode = connection.responseCode
                if (responseCode in 200..299) {
                    log("Flushed ${signals.size} signals")
                } else {
                    log("Flush failed with code $responseCode")
                    signalBuffer.addAll(0, signals)
                }

                connection.disconnect()
            } catch (e: Exception) {
                log("Flush error: ${e.message}")
                signalBuffer.addAll(0, signals)
            }
        }
    }

    private fun sendComplete() {
        val currentSession = session ?: return
        val currentConfig = config ?: return

        scope.launch {
            try {
                val url = URL("${currentConfig.endpoint}/api/v1/sessions/${currentSession.id}/complete")
                val connection = url.openConnection() as HttpURLConnection
                connection.requestMethod = "POST"
                connection.setRequestProperty("Content-Type", "application/json")
                connection.doOutput = true

                val payload = JSONObject().apply {
                    put("completedAt", System.currentTimeMillis())
                }

                BufferedOutputStream(connection.outputStream).use { out ->
                    out.write(payload.toString().toByteArray())
                    out.flush()
                }

                connection.disconnect()
            } catch (e: Exception) {
                log("Complete error: ${e.message}")
            }
        }
    }

    private fun log(message: String) {
        if (config?.debug == true) {
            println("[FraudTracker] $message")
        }
    }
}
