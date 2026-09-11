package com.phantom.companion.data.repo

import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.remote.PhantomApi
import com.phantom.companion.data.remote.RelayClient
import com.phantom.companion.data.remote.SocketConnectionState
import com.phantom.companion.data.remote.SpeechTranscriptionClient
import com.phantom.companion.data.remote.TerminalRelayEvent
import com.phantom.companion.domain.model.ChatTurn
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.DisplayInfo
import com.phantom.companion.domain.model.PendingAttachment
import com.phantom.companion.domain.model.ProviderOption
import com.phantom.companion.domain.model.RelayEnvelope
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import retrofit2.HttpException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone

data class DesktopVoiceUpdate(
    val text: String,
    val sent: Boolean = false
)

class SessionRepository(
    private val relayClient: RelayClient,
    private val sessionStore: SessionStore,
    private val api: PhantomApi,
    private val speechTranscriptionClient: SpeechTranscriptionClient
) {
    private val scope = CoroutineScope(Dispatchers.Main + SupervisorJob())

    private companion object {
        // How long to wait for a capture.started / chat.started before surfacing a
        // "desktop didn't respond" error (step 1 fix for silent follow-up hangs).
        const val RESPONSE_TIMEOUT_MS = 30_000L
        const val MAX_ATTACHMENTS = 3
        const val NOTICE_DISMISS_MS = 2_000L
    }

    val connectionState: StateFlow<SocketConnectionState> = relayClient.connectionState
    val connectionError: StateFlow<String?> = relayClient.connectionError
    val desktopPresence: StateFlow<DesktopPresenceState> = relayClient.desktopPresence
    val currentModel: StateFlow<String?> = relayClient.currentModel
    val currentProvider: StateFlow<String?> = relayClient.currentProvider
    val providers: StateFlow<List<ProviderOption>> = relayClient.providers
    val isVisionSupported: StateFlow<Boolean> = relayClient.isVisionSupported
    val displays: StateFlow<List<DisplayInfo>> = relayClient.displays
    val selectedDisplayId: StateFlow<String> = relayClient.selectedDisplayId
    val terminalEvents: SharedFlow<TerminalRelayEvent> = relayClient.terminalEvents
    private val _desktopVoice = MutableSharedFlow<DesktopVoiceUpdate>(
        replay = 1,
        extraBufferCapacity = 32,
        onBufferOverflow = BufferOverflow.DROP_OLDEST
    )
    val desktopVoice: SharedFlow<DesktopVoiceUpdate> = _desktopVoice.asSharedFlow()

    private val _transcript = MutableStateFlow<List<ChatTurn>>(emptyList())
    val transcript: StateFlow<List<ChatTurn>> = _transcript.asStateFlow()

    private val _pendingAttachments = MutableStateFlow<List<PendingAttachment>>(emptyList())
    val pendingAttachments: StateFlow<List<PendingAttachment>> = _pendingAttachments.asStateFlow()

    private val _sessionError = MutableStateFlow<String?>(null)
    val sessionError: StateFlow<String?> = _sessionError.asStateFlow()
    private var errorDismissJob: Job? = null

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
                relayClient.markRequestIdle()
                setSessionError(
                    "Desktop didn't respond. Make sure Phantom is running on your computer with Companion Mode enabled, then try again."
                )
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

    fun startSession(pairingId: String) {
        // Always start from a blank transcript. Desktop is authoritative; a reconnect
        // after the desktop quit/reopened must not resurrect the previous phone chat.
        clearTranscript()
        relayClient.connect(pairingId)
    }

    fun stopSession() {
        clearResponseWatchdog()
        relayClient.disconnect()
        _pendingAttachments.value = emptyList()
    }

    fun clearTranscript() {
        clearResponseWatchdog()
        _transcript.value = emptyList()
        _pendingAttachments.value = emptyList()
        clearError()
    }

    fun clearError() {
        errorDismissJob?.cancel()
        _sessionError.value = null
    }

    fun reportSessionError(message: String) {
        setSessionError(message)
    }

    private fun setSessionError(message: String) {
        _sessionError.value = message
        errorDismissJob?.cancel()
        errorDismissJob = scope.launch {
            delay(NOTICE_DISMISS_MS)
            if (_sessionError.value == message) {
                _sessionError.value = null
            }
        }
    }

    suspend fun transcribeSpeech(pcm16: ByteArray): String =
        speechTranscriptionClient.transcribePcm16(pcm16)

    private fun processIncomingFrame(frame: RelayEnvelope) {
        val body = frame.body
        when (frame.type) {
            "session.snapshot", "desktop.hello" -> {
                applySnapshotTurns(body?.turns)
                applyPendingAttachments(body?.attachmentCount, body?.attachments)
            }
            "capture.started" -> {
                clearResponseWatchdog()
                clearError()
            }
            "capture.completed" -> {
                clearResponseWatchdog()
                body?.thumbnailJpegBase64?.takeIf { it.isNotBlank() }?.let { thumb ->
                    val current = _pendingAttachments.value
                    if (current.size < MAX_ATTACHMENTS) {
                        _pendingAttachments.value = current + PendingAttachment(
                            index = current.size,
                            thumbnailJpegBase64 = thumb
                        )
                    }
                }
            }
            "capture.failed" -> {
                clearResponseWatchdog()
                val message = mapErrorCode(body?.code, body?.message)
                setSessionError(message)
            }
            "chat.started" -> {
                clearResponseWatchdog()
                clearError()
                val current = _transcript.value.toMutableList()
                val last = current.lastOrNull()
                if (last == null || last.role != "assistant" || !last.isStreaming) {
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
            }
            "chat.delta" -> {
                val delta = body?.text.orEmpty()
                if (delta.isEmpty()) return
                val current = _transcript.value.toMutableList()
                if (current.isEmpty() || current.last().role != "assistant") {
                    current.add(
                        ChatTurn(
                            role = "assistant",
                            text = delta,
                            atUtc = currentIsoTimestamp(),
                            isStreaming = true
                        )
                    )
                } else {
                    val last = current.last()
                    current[current.lastIndex] = last.copy(
                        text = last.text + delta,
                        isStreaming = true
                    )
                }
                _transcript.value = current
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
                    setSessionError(errorMsg)
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
            "voice.transcript" -> {
                _desktopVoice.tryEmit(
                    DesktopVoiceUpdate(
                        text = body?.text.orEmpty(),
                        sent = body?.sent == true
                    )
                )
            }
            "relay.error" -> {
                val mapped = mapErrorCode(body?.code, body?.message)
                setSessionError(mapped)
            }
        }
    }

    fun captureAndAsk(displayId: String = "", prompt: String = "Please analyze this screenshot.") {
        if (!isVisionSupported.value) {
            setSessionError("This model cannot read screens.")
            return
        }
        if (_pendingAttachments.value.size >= MAX_ATTACHMENTS) {
            setSessionError("You can attach up to 3 screenshots.")
            return
        }

        val userTurn = ChatTurn(
            role = "user",
            text = prompt,
            atUtc = currentIsoTimestamp()
        )
        _transcript.value = _transcript.value + userTurn

        startResponseWatchdog()
        relayClient.sendCaptureAsk(displayId = displayId, prompt = prompt)
    }

    fun captureOnly(displayId: String = "") {
        if (!isVisionSupported.value) {
            setSessionError("This model cannot read screens.")
            return
        }
        if (_pendingAttachments.value.size >= MAX_ATTACHMENTS) {
            setSessionError("You can attach up to 3 screenshots.")
            return
        }
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
        _pendingAttachments.value = emptyList()
        clearError()
    }

    fun selectDisplay(displayId: String) {
        relayClient.sendSelectDisplay(displayId)
    }

    fun selectRuntime(provider: String, model: String) {
        relayClient.sendRuntimeSelect(provider, model)
    }

    fun removePendingAttachment(index: Int) {
        val current = _pendingAttachments.value
        if (index !in current.indices) return
        _pendingAttachments.value = current.filterIndexed { i, _ -> i != index }
            .mapIndexed { i, item -> item.copy(index = i) }
        relayClient.sendCaptureRemove(index)
    }

    fun clearPendingAttachments() {
        if (_pendingAttachments.value.isEmpty()) return
        _pendingAttachments.value = emptyList()
        relayClient.sendCaptureClear()
    }

    fun syncComposer(text: String) {
        relayClient.sendVoiceTranscript(text = text, isFinal = true, sent = false)
    }

    fun startDesktopVoice() {
        relayClient.sendVoiceStart()
    }

    fun stopDesktopVoice() {
        relayClient.sendVoiceStop()
    }

    private fun applySnapshotTurns(turns: List<ChatTurn>?) {
        if (turns == null) return
        val current = _transcript.value
        if (current.any { it.isStreaming }) return

        val normalized = turns.filter { it.role == "user" || it.role == "assistant" }
        if (normalized.isEmpty()) {
            // Keep a just-sent local user turn so an empty announce cannot hide the query.
            if (current.lastOrNull()?.role == "user") return
            _transcript.value = emptyList()
            return
        }

        val lastLocal = current.lastOrNull()
        val merged = if (lastLocal != null && lastLocal.role == "user" &&
            normalized.none { it.role == "user" && it.text == lastLocal.text }
        ) {
            normalized + lastLocal
        } else {
            normalized
        }
        _transcript.value = merged
    }

    private fun applyPendingAttachments(count: Int?, attachments: List<PendingAttachment>?) {
        if (attachments != null) {
            val current = _pendingAttachments.value
            _pendingAttachments.value = attachments.take(MAX_ATTACHMENTS).mapIndexed { i, item ->
                val existing = current.getOrNull(i)?.thumbnailJpegBase64
                val incoming = item.thumbnailJpegBase64
                val thumb = when {
                    incoming.isNullOrBlank() -> existing
                    existing.isNullOrBlank() -> incoming
                    incoming.length >= existing.length -> incoming
                    else -> existing
                }
                item.copy(index = i, thumbnailJpegBase64 = thumb)
            }
            return
        }
        if (count == 0) {
            _pendingAttachments.value = emptyList()
        }
    }

    private fun mapErrorCode(code: String?, fallbackMessage: String?): String {
        return when (code) {
            "desktop_offline" -> "Waiting for desktop…"
            "not_paired" -> "Pairing required."
            "lock_missing" -> "Could not start the interview from the phone. Check Phantom on your computer and try again."
            "vision_unsupported" -> "This model cannot read screens."
            "attachment_limit" -> "You can attach up to 3 screenshots."
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
