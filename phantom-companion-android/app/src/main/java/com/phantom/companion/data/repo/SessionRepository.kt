package com.phantom.companion.data.repo

import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.remote.PhantomApi
import com.phantom.companion.data.remote.RelayClient
import com.phantom.companion.data.remote.SocketConnectionState
import com.phantom.companion.data.remote.TerminalRelayEvent
import com.phantom.companion.domain.model.ChatTurn
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.DisplayInfo
import com.phantom.companion.domain.model.RelayEnvelope
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import retrofit2.HttpException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone

class SessionRepository(
    private val relayClient: RelayClient,
    private val sessionStore: SessionStore,
    private val api: PhantomApi
) {
    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())

    private companion object {
        // How long to wait for a capture.started / chat.started before surfacing a
        // "desktop didn't respond" error (step 1 fix for silent follow-up hangs).
        const val RESPONSE_TIMEOUT_MS = 30_000L
    }

    val connectionState: StateFlow<SocketConnectionState> = relayClient.connectionState
    val connectionError: StateFlow<String?> = relayClient.connectionError
    val desktopPresence: StateFlow<DesktopPresenceState> = relayClient.desktopPresence
    val currentModel: StateFlow<String?> = relayClient.currentModel
    val isVisionSupported: StateFlow<Boolean> = relayClient.isVisionSupported
    val displays: StateFlow<List<DisplayInfo>> = relayClient.displays
    val selectedDisplayId: StateFlow<String> = relayClient.selectedDisplayId
    val terminalEvents: SharedFlow<TerminalRelayEvent> = relayClient.terminalEvents

    private val _transcript = MutableStateFlow<List<ChatTurn>>(emptyList())
    val transcript: StateFlow<List<ChatTurn>> = _transcript.asStateFlow()

    private val _currentPendingThumbnail = MutableStateFlow<String?>(null)
    val currentPendingThumbnail: StateFlow<String?> = _currentPendingThumbnail.asStateFlow()

    private val _sessionError = MutableStateFlow<String?>(null)
    val sessionError: StateFlow<String?> = _sessionError.asStateFlow()

    // Response-timeout watchdog (step 1 fix for "I send a follow-up and see no answer").
    // When the phone sends a capture.ask / chat.send, we record the time and start a
    // watchdog. If no capture.started / chat.started arrives within RESPONSE_TIMEOUT,
    // we surface a visible, actionable error instead of leaving the user staring at a
    // silent "Thinking…" state with no feedback. Cleared on any started frame.
    private var pendingRequestAt: Long = 0L
    private var watchdogJob: Job? = null
    private fun startResponseWatchdog() {
        watchdogJob?.cancel()
        pendingRequestAt = System.currentTimeMillis()
        watchdogJob = scope.launch {
            delay(RESPONSE_TIMEOUT_MS)
            if (pendingRequestAt != 0L) {
                _sessionError.value =
                    "Desktop didn't respond. Make sure Phantom is running on your computer with Companion Mode enabled, then try again."
            }
        }
    }
    private fun clearResponseWatchdog() {
        watchdogJob?.cancel()
        watchdogJob = null
        pendingRequestAt = 0L
    }

    init {
        scope.launch {
            relayClient.incomingFrames.collect { frame ->
                processIncomingFrame(frame)
            }
        }
    }

    private var hydrated = false

    fun startSession(pairingId: String) {
        relayClient.connect(pairingId)
        // Hydrate the last published snapshot once per session start (L2). Reconnects inside
        // RelayClient do not re-fetch; the desktop republishes session.snapshot on hello anyway.
        if (!hydrated) {
            hydrated = true
            scope.launch { hydrateCurrentSession() }
        }
    }

    fun stopSession() {
        clearResponseWatchdog()
        relayClient.disconnect()
        _currentPendingThumbnail.value = null
        hydrated = false
    }

    fun clearTranscript() {
        clearResponseWatchdog()
        _transcript.value = emptyList()
        _currentPendingThumbnail.value = null
        _sessionError.value = null
    }

    fun clearError() {
        _sessionError.value = null
    }

    private suspend fun hydrateCurrentSession() {
        try {
            val response = api.currentSessionRaw()
            if (response.isSuccessful) {
                val snapshot = response.body()
                if (snapshot != null && snapshot.turns.isNotEmpty()) {
                    _transcript.value = snapshot.turns
                }
            } else if (response.code() != 404) {
                // 404 just means "no active desktop session" — not an error. Anything else
                // (401/5xx) is surfaced so the user knows the backend is unhealthy (M2).
                _sessionError.value = "Could not reach Phantom backend (HTTP ${response.code()})."
            }
        } catch (e: HttpException) {
            _sessionError.value = "Could not reach Phantom backend."
        } catch (e: Exception) {
            // Network blip: leave existing transcript intact; relay will republish snapshot.
        }
    }

    private fun processIncomingFrame(frame: RelayEnvelope) {
        val body = frame.body
        when (frame.type) {
            "session.snapshot" -> {
                // Hydrate the transcript from the desktop's last published snapshot (C2).
                // Only adopt when the snapshot carries turns, so we never wipe a live stream
                // with an empty in-flight snapshot.
                val turns = body?.turns
                if (turns != null && turns.isNotEmpty()) {
                    _transcript.value = turns
                }
            }
            "capture.started" -> {
                clearResponseWatchdog()
                _sessionError.value = null
            }
            "capture.completed" -> {
                body?.thumbnailJpegBase64?.let {
                    _currentPendingThumbnail.value = it
                }
            }
            "capture.failed" -> {
                clearResponseWatchdog()
                val message = mapErrorCode(body?.code, body?.message)
                _sessionError.value = message
            }
            "chat.started" -> {
                clearResponseWatchdog()
                _sessionError.value = null
                // Create an initial streaming assistant turn
                val current = _transcript.value.toMutableList()
                current.add(
                    ChatTurn(
                        role = "assistant",
                        text = "",
                        atUtc = currentIsoTimestamp(),
                        isStreaming = true
                    )
                )
                _transcript.value = current
            }
            "chat.delta" -> {
                val delta = body?.text.orEmpty()
                val current = _transcript.value.toMutableList()
                if (current.isNotEmpty() && current.last().role == "assistant") {
                    val last = current.last()
                    current[current.lastIndex] = last.copy(
                        text = last.text + delta,
                        isStreaming = true
                    )
                    _transcript.value = current
                }
            }
            "chat.completed" -> {
                clearResponseWatchdog()
                val current = _transcript.value.toMutableList()
                if (current.isNotEmpty() && current.last().role == "assistant") {
                    val last = current.last()
                    // Graceful empty-turn fix: if the assistant turn ended with no text
                    // (deltas lost / empty response), show a placeholder instead of an
                    // invisible empty bubble (step 1 fix for "I don't see any answer").
                    val finalText = if (last.text.isEmpty()) "(no response received)" else last.text
                    current[current.lastIndex] = last.copy(text = finalText, isStreaming = false)
                    _transcript.value = current
                }
            }
            "chat.failed" -> {
                clearResponseWatchdog()
                val current = _transcript.value.toMutableList()
                val errorMsg = mapErrorCode(body?.code, body?.message)
                if (current.isNotEmpty() && current.last().role == "assistant") {
                    val last = current.last()
                    current[current.lastIndex] = last.copy(
                        text = if (last.text.isEmpty()) "Error: $errorMsg" else "${last.text}\n\n[Error: $errorMsg]",
                        isStreaming = false,
                        isError = true
                    )
                    _transcript.value = current
                } else {
                    _sessionError.value = errorMsg
                }
            }
            "chat.cancelled" -> {
                clearResponseWatchdog()
                val current = _transcript.value.toMutableList()
                if (current.isNotEmpty() && current.last().role == "assistant") {
                    val last = current.last()
                    current[current.lastIndex] = last.copy(
                        isStreaming = false,
                        isCancelled = true
                    )
                    _transcript.value = current
                }
            }
            "relay.error" -> {
                val mapped = mapErrorCode(body?.code, body?.message)
                _sessionError.value = mapped
            }
        }
    }

    fun captureAndAsk(displayId: String = "", prompt: String = "Please analyze this screenshot.") {
        if (!isVisionSupported.value) {
            _sessionError.value = "This model cannot read screens."
            return
        }

        // Add user turn to transcript with optional thumbnail
        val userTurn = ChatTurn(
            role = "user",
            text = prompt,
            atUtc = currentIsoTimestamp(),
            thumbnailBase64 = _currentPendingThumbnail.value
        )
        _transcript.value = _transcript.value + userTurn
        _currentPendingThumbnail.value = null

        startResponseWatchdog()
        relayClient.sendCaptureAsk(displayId = displayId, prompt = prompt)
    }

    fun captureOnly(displayId: String = "") {
        startResponseWatchdog()
        relayClient.sendCaptureFull(displayId = displayId, attachOnly = true)
    }

    fun sendFollowUp(text: String) {
        val trimmed = text.trim()
        if (trimmed.isEmpty()) return

        val userTurn = ChatTurn(
            role = "user",
            text = trimmed,
            atUtc = currentIsoTimestamp()
        )
        _transcript.value = _transcript.value + userTurn

        startResponseWatchdog()
        relayClient.sendChatText(trimmed)
    }

    fun stopGeneration() {
        relayClient.sendCancelChat()
    }

    fun newTopic() {
        relayClient.sendNewTopic()
        _transcript.value = emptyList()
        _currentPendingThumbnail.value = null
        _sessionError.value = null
    }

    fun selectDisplay(displayId: String) {
        relayClient.sendSelectDisplay(displayId)
    }

    private fun mapErrorCode(code: String?, fallbackMessage: String?): String {
        return when (code) {
            "desktop_offline" -> "Waiting for desktop…"
            "not_paired" -> "Pairing required."
            "lock_missing" -> "Start the interview in Phantom on your computer first."
            "vision_unsupported" -> "This model cannot read screens."
            "capture_permission_missing" -> "Capture failed. Check desktop screen-recording permission."
            "rate_limited" -> "Too many attempts. Wait a few seconds."
            "pairing_revoked" -> "Pairing was revoked by desktop."
            "account_locked" -> "This account is temporarily locked."
            else -> fallbackMessage ?: "Operation failed."
        }
    }

    private fun currentIsoTimestamp(): String {
        val sdf = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply {
            timeZone = TimeZone.getTimeZone("UTC")
        }
        return sdf.format(Date())
    }
}
