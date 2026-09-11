package com.phantom.companion.data.remote

import android.util.Log
import com.phantom.companion.BuildConfig
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.DisplayInfo
import com.phantom.companion.domain.model.ErrorBody
import com.phantom.companion.domain.model.ProviderOption
import com.phantom.companion.domain.model.RelayBody
import com.phantom.companion.domain.model.RelayEnvelope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import retrofit2.HttpException
import java.net.URLEncoder
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID
import java.util.concurrent.TimeUnit

sealed class SocketConnectionState {
    data object Disconnected : SocketConnectionState()
    data object Connecting : SocketConnectionState()
    data object Connected : SocketConnectionState()
    data class Reconnecting(val attempt: Int, val delayMs: Long) : SocketConnectionState()
    data class Failed(val reason: String) : SocketConnectionState()
}

/**
 * Terminal relay events that mean the current pairing is no longer usable. The client stops
 * retrying, clears the local pairing, and surfaces a navigation event so the user is sent
 * back to the pair screen instead of looping forever on a dead/revoked pairing (H1/M1).
 */
sealed class TerminalRelayEvent {
    data object PairingRevoked : TerminalRelayEvent()
    data object PairingReplaced : TerminalRelayEvent()
    data object ServerShutdown : TerminalRelayEvent()
    data object NotAuthorized : TerminalRelayEvent()
}

class RelayClient(
    private val sessionStore: SessionStore,
    private val api: PhantomApi,
    private val baseHttpUrl: String = BuildConfig.PHANTOM_API_BASE_URL
) {
    private val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
    private val json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        encodeDefaults = true
    }

    private val socketHttpClient: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(0, TimeUnit.MILLISECONDS) // infinite for websocket
        .pingInterval(15, TimeUnit.SECONDS) // protocol-level ping every 15s
        .build()

    private var currentWebSocket: WebSocket? = null
    private var connectionJob: Job? = null
    private var isIntentionallyClosed = false
    private var currentPairingId: String? = null

    private val _connectionState = MutableStateFlow<SocketConnectionState>(SocketConnectionState.Disconnected)
    val connectionState: StateFlow<SocketConnectionState> = _connectionState.asStateFlow()

    private val _connectionError = MutableStateFlow<String?>(null)
    val connectionError: StateFlow<String?> = _connectionError.asStateFlow()

    private val _incomingFrames = MutableSharedFlow<RelayEnvelope>(extraBufferCapacity = 64)
    val incomingFrames: SharedFlow<RelayEnvelope> = _incomingFrames.asSharedFlow()

    private val _terminalEvents = MutableSharedFlow<TerminalRelayEvent>(extraBufferCapacity = 8)
    val terminalEvents: SharedFlow<TerminalRelayEvent> = _terminalEvents.asSharedFlow()

    private val _voiceTranscript = MutableSharedFlow<String>(extraBufferCapacity = 8)
    val voiceTranscript: SharedFlow<String> = _voiceTranscript.asSharedFlow()

    private var peerLeftJob: Job? = null
    private var appPingJob: Job? = null

    private val _desktopPresence = MutableStateFlow(DesktopPresenceState.OFFLINE)
    val desktopPresence: StateFlow<DesktopPresenceState> = _desktopPresence.asStateFlow()

    private val _currentModel = MutableStateFlow<String?>(null)
    val currentModel: StateFlow<String?> = _currentModel.asStateFlow()

    private val _currentProvider = MutableStateFlow<String?>(null)
    val currentProvider: StateFlow<String?> = _currentProvider.asStateFlow()

    private val _providers = MutableStateFlow<List<ProviderOption>>(emptyList())
    val providers: StateFlow<List<ProviderOption>> = _providers.asStateFlow()

    private val _isVisionSupported = MutableStateFlow(true)
    val isVisionSupported: StateFlow<Boolean> = _isVisionSupported.asStateFlow()

    private val _displays = MutableStateFlow<List<DisplayInfo>>(emptyList())
    val displays: StateFlow<List<DisplayInfo>> = _displays.asStateFlow()

    // Default to empty so the desktop picks its default display when the phone sends no id (M4).
    private val _selectedDisplayId = MutableStateFlow("")
    val selectedDisplayId: StateFlow<String> = _selectedDisplayId.asStateFlow()

    private val _lastInboundAt = MutableStateFlow(System.currentTimeMillis())
    private var captureInFlight = false

    fun connect(pairingId: String) {
        currentPairingId = pairingId
        isIntentionallyClosed = false
        connectionJob?.cancel()
        connectionJob = scope.launch {
            connectWithBackoff(pairingId)
        }
    }

    fun disconnect() {
        isIntentionallyClosed = true
        connectionJob?.cancel()
        connectionJob = null
        currentWebSocket?.close(1000, "Normal closure")
        currentWebSocket = null
        _connectionState.value = SocketConnectionState.Disconnected
        _desktopPresence.value = DesktopPresenceState.OFFLINE
        captureInFlight = false
    }

    private suspend fun connectWithBackoff(pairingId: String) {
        val backoffs = listOf(1000L, 2000L, 4000L, 8000L, 15000L)
        var attempt = 0

        while (scope.isActive && !isIntentionallyClosed) {
            _connectionState.value = SocketConnectionState.Connecting
            try {
                Log.d("RelayClient", "Fetching relay ticket for pairing $pairingId...")
                val ticketResponse = try {
                    api.relayTicket(
                        com.phantom.companion.domain.model.RelayTicketRequest(pairingId = pairingId, role = "phone")
                    )
                } catch (e: HttpException) {
                    handleTicketHttpException(e)
                    return
                }

                val ticket = ticketResponse.ticket
                val rawUrl = ticketResponse.relayUrl ?: run {
                    val httpBase = baseHttpUrl.trimEnd('/')
                    "$httpBase/api/companion/relay"
                }
                val wsBaseUrl = rawUrl
                    .replace("https://", "wss://")
                    .replace("http://", "ws://")
                val relayUrl = if (wsBaseUrl.contains("ticket=")) {
                    wsBaseUrl
                } else {
                    val sep = if (wsBaseUrl.contains("?")) "&" else "?"
                    "$wsBaseUrl${sep}ticket=${URLEncoder.encode(ticket, "UTF-8")}"
                }

                Log.d("RelayClient", "Connecting WebSocket to $relayUrl (with subprotocol)...")
                var socketConnected = openSocket(relayUrl, ticket, pairingId, useSubprotocol = true)
                if (!socketConnected && !isIntentionallyClosed) {
                    Log.d("RelayClient", "WebSocket subprotocol negotiation failed, retrying without subprotocol...")
                    socketConnected = openSocket(relayUrl, ticket, pairingId, useSubprotocol = false)
                }

                if (socketConnected) {
                    attempt = 0
                    _connectionError.value = null
                    monitorLiveness()
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                if (isIntentionallyClosed) break
                val errorMsg = when (e) {
                    is HttpException -> "Failed to get relay ticket (HTTP ${e.code()})"
                    else -> {
                        Log.e("RelayClient", "Error in relay connection", e)
                        e.message ?: "Relay connection failed"
                    }
                }
                _connectionError.value = errorMsg
                _connectionState.value = SocketConnectionState.Failed(errorMsg)
            }

            if (isIntentionallyClosed) break

            val delayMs = backoffs[attempt.coerceAtMost(backoffs.lastIndex)]
            attempt++
            _connectionState.value = SocketConnectionState.Reconnecting(attempt, delayMs)
            delay(delayMs)
        }
    }

    /**
     * Inspects a relay-ticket HTTP failure for terminal pairing errors. On "not authorized" or
     * "revoked" the client clears the local pairing, emits a terminal event, and stops the
     * reconnect loop (H1) — instead of looping forever on a dead pairing.
     */
    private suspend fun handleTicketHttpException(e: HttpException) {
        val body = runCatching { e.response()?.errorBody()?.string() }.getOrNull().orEmpty()
        Log.e("RelayClient", "HTTP ${e.code()} from relay ticket: $body", e)
        val parsed = runCatching { json.decodeFromString<ErrorBody>(body) }.getOrNull()
        val message = parsed?.error ?: parsed?.message ?: body

        val terminal = when {
            message.contains("not authorized", ignoreCase = true) -> TerminalRelayEvent.NotAuthorized
            message.contains("has been revoked", ignoreCase = true) -> TerminalRelayEvent.PairingRevoked
            else -> null
        }

        if (terminal != null) {
            sessionStore.savePairing(null)
            _terminalEvents.emit(terminal)
            _connectionError.value = when (terminal) {
                TerminalRelayEvent.NotAuthorized ->
                    "This device is not authorized for this pairing. Please re-pair with the desktop."
                TerminalRelayEvent.PairingRevoked ->
                    "Pairing was revoked by the desktop."
                else -> message
            }
            _connectionState.value = SocketConnectionState.Failed(_connectionError.value ?: "")
            _desktopPresence.value = DesktopPresenceState.ERROR
            // Stop the reconnect loop.
            isIntentionallyClosed = true
        } else {
            _connectionError.value = message.ifEmpty { "Failed to get relay ticket (HTTP ${e.code()})" }
            _connectionState.value = SocketConnectionState.Failed(_connectionError.value ?: "")
        }
    }

    private suspend fun openSocket(
        relayUrl: String,
        ticket: String,
        pairingId: String,
        useSubprotocol: Boolean
    ): Boolean {
        val accessToken = sessionStore.getAccessToken()
        val requestBuilder = Request.Builder()
            .url(relayUrl)
            .header("Authorization", "Bearer $accessToken")
            // CRITICAL AUTH RULE: Never send Origin or X-Phantom-CSRF
            .removeHeader("Origin")
            .removeHeader("X-Phantom-CSRF")

        if (useSubprotocol) {
            requestBuilder.header("Sec-WebSocket-Protocol", "phantom.companion.v1")
        }

        val openDeferred = CompletableDeferred<Boolean>()

        currentWebSocket?.close(1000, "Opening new socket")
        currentWebSocket = null

        val socket = socketHttpClient.newWebSocket(requestBuilder.build(), object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                Log.d("RelayClient", "WebSocket onOpen successful")
                _connectionState.value = SocketConnectionState.Connected
                _connectionError.value = null
                _lastInboundAt.value = System.currentTimeMillis()

                if (_desktopPresence.value == DesktopPresenceState.CONNECTING) {
                    _desktopPresence.value = DesktopPresenceState.OFFLINE
                }

                // Send session.hello (payload in body per spec §5.1)
                sendFrame(
                    type = "session.hello",
                    pairingId = pairingId,
                    body = RelayBody(
                        appVersion = BuildConfig.VERSION_NAME,
                        platform = "android"
                    )
                )
                openDeferred.complete(true)
            }

            override fun onMessage(webSocket: WebSocket, text: String) {
                Log.d("RelayClient", "WebSocket incoming frame: $text")
                handleIncomingMessage(text, pairingId)
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                val err = response?.message ?: t.message ?: "Connection failed"
                Log.e("RelayClient", "WebSocket onFailure: $err (HTTP ${response?.code})", t)
                _connectionState.value = SocketConnectionState.Failed(err)
                _desktopPresence.value = DesktopPresenceState.OFFLINE
                openDeferred.complete(false)
            }

            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                Log.d("RelayClient", "WebSocket onClosing: code=$code, reason=$reason")
                webSocket.close(1000, null)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                Log.d("RelayClient", "WebSocket onClosed: code=$code, reason=$reason")
                if (!isIntentionallyClosed) {
                    _connectionState.value = SocketConnectionState.Disconnected
                    _desktopPresence.value = DesktopPresenceState.OFFLINE
                }
            }
        })

        currentWebSocket = socket

        val connected = withTimeoutOrNull(15000L) {
            openDeferred.await()
        } ?: false

        if (!connected) {
            Log.w("RelayClient", "WebSocket connection timed out or rejected")
            socket.cancel()
            if (currentWebSocket === socket) {
                currentWebSocket = null
            }
        }

        return connected
    }

    private suspend fun monitorLiveness() {
        appPingJob?.cancel()
        appPingJob = scope.launch {
            while (scope.isActive && currentWebSocket != null && !isIntentionallyClosed) {
                delay(15_000)
                val pairingId = currentPairingId ?: break
                sendFrame(type = "relay.ping", pairingId = pairingId, body = null)
            }
        }
        while (scope.isActive && currentWebSocket != null && !isIntentionallyClosed) {
            delay(5000)
            val elapsedSinceInbound = System.currentTimeMillis() - _lastInboundAt.value
            if (elapsedSinceInbound > 90_000) {
                currentWebSocket?.cancel()
                currentWebSocket = null
                break
            }
        }
        appPingJob?.cancel()
        appPingJob = null
    }

    private fun handleIncomingMessage(text: String, pairingId: String) {
        _lastInboundAt.value = System.currentTimeMillis()
        val frame = try {
            json.decodeFromString<RelayEnvelope>(text)
        } catch (e: Exception) {
            // Forward-compatibility: ignore unknown JSON formats
            return
        }
        _incomingFrames.tryEmit(frame)
        val body = frame.body

        when (frame.type) {
            "relay.ping" -> {
                sendFrame(type = "relay.pong", pairingId = pairingId, body = null)
            }
            "relay.peer_joined" -> {
                if (frame.role == "desktop") {
                    peerLeftJob?.cancel()
                    peerLeftJob = null
                    _desktopPresence.value = DesktopPresenceState.READY
                }
            }
            "relay.peer_left" -> {
                if (frame.role == "desktop") {
                    // Desktop reconnects with a fresh ticket; keep the last live status
                    // briefly so a ticket refresh does not flash "Desktop offline".
                    peerLeftJob?.cancel()
                    peerLeftJob = scope.launch {
                        delay(2_000)
                        if (_connectionState.value is SocketConnectionState.Connected) {
                            _desktopPresence.value = DesktopPresenceState.OFFLINE
                        }
                    }
                }
            }
            "desktop.hello", "desktop.status" -> {
                body?.status?.takeIf { it.isNotBlank() }?.let {
                    peerLeftJob?.cancel()
                    peerLeftJob = null
                    _desktopPresence.value = parsePresenceState(it, allowCapturing = captureInFlight)
                }
                applyRuntimeFields(body)
                body?.displays?.let { displays ->
                    _displays.value = displays
                    // Adopt the desktop's default display if the user hasn't chosen one (M4).
                    if (_selectedDisplayId.value.isEmpty()) {
                        val default = displays.firstOrNull { it.isDefault }?.id
                            ?: displays.firstOrNull()?.id
                            ?: ""
                        _selectedDisplayId.value = default
                    }
                }
            }
            "session.snapshot" -> {
                // Snapshot turns are processed by SessionRepository; here we sync
                // presence/model/vision/displays/selectedDisplayId from the same body (C2).
                val snapStatus = body?.desktopStatus ?: body?.status
                if (!snapStatus.isNullOrBlank()) {
                    peerLeftJob?.cancel()
                    peerLeftJob = null
                    _desktopPresence.value = parsePresenceState(snapStatus, allowCapturing = captureInFlight)
                }
                applyRuntimeFields(body)
                body?.displays?.let { _displays.value = it }
                body?.selectedDisplayId?.let { snapSelected ->
                    if (snapSelected.isNotEmpty()) _selectedDisplayId.value = snapSelected
                }
            }
            "capture.started" -> {
                captureInFlight = true
                _desktopPresence.value = DesktopPresenceState.CAPTURING
            }
            "capture.completed" -> {
                captureInFlight = false
                _desktopPresence.value = DesktopPresenceState.READY
            }
            "capture.failed" -> {
                captureInFlight = false
                _desktopPresence.value = DesktopPresenceState.ERROR
            }
            "chat.started" -> {
                _desktopPresence.value = DesktopPresenceState.THINKING
            }
            "chat.completed", "chat.cancelled" -> {
                _desktopPresence.value = DesktopPresenceState.READY
            }
            "chat.failed" -> {
                _desktopPresence.value = DesktopPresenceState.ERROR
            }
            "voice.transcript" -> {
                body?.text?.takeIf { it.isNotBlank() }?.let {
                    _voiceTranscript.tryEmit(it)
                }
            }
            "relay.error" -> {
                val code = frame.code ?: body?.code
                when (code) {
                    "pairing_revoked" -> {
                        sessionStore.savePairing(null)
                        scope.launch { _terminalEvents.emit(TerminalRelayEvent.PairingRevoked) }
                        disconnect()
                    }
                    "replaced" -> {
                        scope.launch { _terminalEvents.emit(TerminalRelayEvent.PairingReplaced) }
                        disconnect()
                    }
                    "server_shutdown" -> {
                        scope.launch { _terminalEvents.emit(TerminalRelayEvent.ServerShutdown) }
                        disconnect()
                    }
                    else -> { /* non-terminal relay.error; SessionRepository surfaces message */ }
                }
            }
        }
    }

    private fun parsePresenceState(status: String?, allowCapturing: Boolean = true): DesktopPresenceState {
        return when (status?.lowercase()) {
            "ready" -> DesktopPresenceState.READY
            "idle" -> DesktopPresenceState.IDLE
            "connecting" -> DesktopPresenceState.CONNECTING
            // A snapshot taken while the desktop still had its capture flag set can
            // freeze the phone on Capturing. Ignore stale capturing unless we have
            // a capture in flight.
            "capturing" -> if (allowCapturing) DesktopPresenceState.CAPTURING else DesktopPresenceState.READY
            "thinking" -> DesktopPresenceState.THINKING
            "error" -> DesktopPresenceState.ERROR
            else -> DesktopPresenceState.OFFLINE
        }
    }

    fun sendCaptureAsk(displayId: String = "", prompt: String = "Please analyze this screenshot.") {
        val pairingId = currentPairingId ?: return
        captureInFlight = true
        val resolvedDisplay = displayId.ifEmpty { _selectedDisplayId.value }
        sendFrame(
            type = "capture.ask",
            pairingId = pairingId,
            body = RelayBody(displayId = resolvedDisplay, prompt = prompt)
        )
    }

    fun sendCaptureFull(displayId: String = "", attachOnly: Boolean = true) {
        val pairingId = currentPairingId ?: return
        captureInFlight = true
        val resolvedDisplay = displayId.ifEmpty { _selectedDisplayId.value }
        sendFrame(
            type = "capture.full",
            pairingId = pairingId,
            body = RelayBody(displayId = resolvedDisplay, attachOnly = attachOnly)
        )
    }

    fun sendChatText(text: String) {
        val pairingId = currentPairingId ?: return
        sendFrame(
            type = "chat.send",
            pairingId = pairingId,
            body = RelayBody(text = text)
        )
    }

    fun sendCancelChat(requestId: String? = null) {
        val pairingId = currentPairingId ?: return
        sendFrame(
            type = "chat.cancel",
            pairingId = pairingId,
            body = RelayBody(requestId = requestId)
        )
    }

    fun sendNewTopic() {
        val pairingId = currentPairingId ?: return
        sendFrame(
            type = "chat.new_topic",
            pairingId = pairingId,
            body = null
        )
    }

    fun sendSelectDisplay(displayId: String) {
        val pairingId = currentPairingId ?: return
        _selectedDisplayId.value = displayId
        sendFrame(
            type = "display.select",
            pairingId = pairingId,
            body = RelayBody(displayId = displayId)
        )
    }

    fun sendVoiceStart() {
        val pairingId = currentPairingId ?: return
        sendFrame(type = "voice.start", pairingId = pairingId, body = null)
    }

    fun sendVoiceStop() {
        val pairingId = currentPairingId ?: return
        sendFrame(type = "voice.stop", pairingId = pairingId, body = null)
    }

    fun sendRuntimeSelect(provider: String, model: String) {
        val pairingId = currentPairingId ?: return
        _currentProvider.value = provider
        _currentModel.value = model
        visionForSelection(provider, model)?.let { _isVisionSupported.value = it }
        sendFrame(
            type = "runtime.select",
            pairingId = pairingId,
            body = RelayBody(provider = provider, model = model)
        )
    }

    fun sendCaptureRemove(index: Int) {
        val pairingId = currentPairingId ?: return
        sendFrame(
            type = "capture.remove",
            pairingId = pairingId,
            body = RelayBody(index = index)
        )
    }

    fun sendCaptureClear() {
        val pairingId = currentPairingId ?: return
        sendFrame(type = "capture.clear", pairingId = pairingId, body = null)
    }

    private fun applyRuntimeFields(body: RelayBody?) {
        body?.provider?.let { _currentProvider.value = it }
        body?.model?.let { _currentModel.value = it }
        body?.vision?.let { _isVisionSupported.value = it }
        body?.providers?.let { _providers.value = it }
    }

    private fun visionForSelection(providerId: String, modelId: String): Boolean? {
        val provider = _providers.value.firstOrNull { it.id.equals(providerId, ignoreCase = true) }
            ?: return null
        return provider.models.firstOrNull { it.id.equals(modelId, ignoreCase = true) }?.vision
    }

    private fun sendFrame(
        type: String,
        pairingId: String,
        body: RelayBody?
    ) {
        val frame = RelayEnvelope(
            v = 1,
            id = UUID.randomUUID().toString(),
            type = type,
            ts = currentIsoTimestamp(),
            pairingId = pairingId,
            role = "phone",
            body = body
        )

        val frameJson = json.encodeToString(frame)
        currentWebSocket?.send(frameJson)
    }

    private fun currentIsoTimestamp(): String {
        val sdf = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply {
            timeZone = TimeZone.getTimeZone("UTC")
        }
        return sdf.format(Date())
    }
}
