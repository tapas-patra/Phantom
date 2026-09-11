package com.phantom.companion.ui.session

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.local.nextPhoneDictation
import com.phantom.companion.data.remote.SocketConnectionState
import com.phantom.companion.data.repo.DesktopVoiceUpdate
import com.phantom.companion.data.repo.PairingRepository
import com.phantom.companion.data.repo.SessionRepository
import com.phantom.companion.data.speech.prefersManagedCloudSpeech
import com.phantom.companion.domain.model.ChatTurn
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.DisplayInfo
import com.phantom.companion.domain.model.StartupSnapshot
import com.phantom.companion.domain.model.resolveFollowUpText
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

sealed class SessionNavigationEvent {
    data object NavigateToAccount : SessionNavigationEvent()
    data object NavigateToPair : SessionNavigationEvent()
}

class SessionViewModel(
    private val sessionRepository: SessionRepository,
    private val sessionStore: SessionStore,
    private val pairingRepository: PairingRepository
) : ViewModel() {

    val connectionState: StateFlow<SocketConnectionState> = sessionRepository.connectionState
    val connectionError: StateFlow<String?> = sessionRepository.connectionError
    val desktopPresence: StateFlow<DesktopPresenceState> = sessionRepository.desktopPresence
    val currentModel: StateFlow<String?> = sessionRepository.currentModel
    val currentProvider: StateFlow<String?> = sessionRepository.currentProvider
    val providers = sessionRepository.providers
    val isVisionSupported: StateFlow<Boolean> = sessionRepository.isVisionSupported
    val displays: StateFlow<List<DisplayInfo>> = sessionRepository.displays
    val selectedDisplayId: StateFlow<String> = sessionRepository.selectedDisplayId
    val transcript: StateFlow<List<ChatTurn>> = sessionRepository.transcript
    val pendingAttachments = sessionRepository.pendingAttachments
    val sessionError: StateFlow<String?> = sessionRepository.sessionError

    val activePairing = sessionStore.activePairing
    val companionApiReady = sessionStore.companionApiReady
    val startupSnapshot: StateFlow<StartupSnapshot?> = sessionStore.startupSnapshot
    val usePhoneMicrophone: StateFlow<Boolean> = sessionStore.usePhoneMicrophone

    private val _inputText = MutableStateFlow("")
    val inputText: StateFlow<String> = _inputText.asStateFlow()

    private val _showNewTopicConfirmDialog = MutableStateFlow(false)
    val showNewTopicConfirmDialog: StateFlow<Boolean> = _showNewTopicConfirmDialog.asStateFlow()

    private val _showVoiceSettings = MutableStateFlow(false)
    val showVoiceSettings: StateFlow<Boolean> = _showVoiceSettings.asStateFlow()

    private val _isMicListening = MutableStateFlow(false)
    val isMicListening: StateFlow<Boolean> = _isMicListening.asStateFlow()

    // Text already in the composer when phone STT started. Partial/final hypotheses
    // replace the live tail instead of appending, which is how Android reports them.
    private var phoneDictationPrefix: String? = null

    private val _navigationEvent = MutableSharedFlow<SessionNavigationEvent>()
    val navigationEvent: SharedFlow<SessionNavigationEvent> = _navigationEvent.asSharedFlow()

    init {
        // Start live session when active pairing is present
        val pairing = sessionStore.activePairing.value
        if (pairing != null) {
            sessionRepository.startSession(pairing.pairingId)
        }

        // Terminal relay errors (pairing revoked / replaced / server shutdown / not authorized)
        // mean the current pairing is dead — send the user back to the pair screen (H1).
        viewModelScope.launch {
            sessionRepository.terminalEvents.collect {
                sessionRepository.stopSession()
                _navigationEvent.emit(SessionNavigationEvent.NavigateToPair)
            }
        }
        viewModelScope.launch {
            sessionRepository.desktopVoice.collect { update ->
                if (sessionStore.usePhoneMicrophone.value) return@collect
                applyDesktopVoiceComposer(update)
            }
        }
    }

    fun onInputTextChanged(text: String) {
        _inputText.value = text
    }

    fun onCaptureClicked() {
        sessionRepository.captureOnly(displayId = selectedDisplayId.value)
    }

    fun onSendFollowUpClicked() {
        val text = resolveFollowUpText(_inputText.value, pendingAttachments.value.size) ?: return
        sessionRepository.sendFollowUp(text)
        phoneDictationPrefix = null
        _inputText.value = ""
    }

    fun setMicListening(listening: Boolean) {
        _isMicListening.value = listening
    }

    fun beginPhoneDictation() {
        phoneDictationPrefix = _inputText.value.trimEnd()
        _isMicListening.value = true
    }

    fun onNativeSpeechFallback() {
        phoneDictationPrefix = _inputText.value.trimEnd()
    }

    fun prefersCloudSpeech(): Boolean =
        prefersManagedCloudSpeech(startupSnapshot.value?.accessTier)

    suspend fun transcribeCloudSpeech(pcm16: ByteArray): String =
        sessionRepository.transcribeSpeech(pcm16)

    fun surfaceSpeechError(message: String) {
        _isMicListening.value = false
        sessionRepository.reportSessionError(message)
    }

    fun applyPhoneDictation(text: String, isFinal: Boolean, keepListening: Boolean = false) {
        val next = nextPhoneDictation(
            currentText = _inputText.value,
            prefix = phoneDictationPrefix,
            isListening = _isMicListening.value,
            hypothesis = text,
            isFinal = isFinal,
            keepListening = keepListening
        )
        phoneDictationPrefix = next.prefix
        _inputText.value = next.text
        _isMicListening.value = next.listening
    }

    fun startDesktopVoice() {
        phoneDictationPrefix = _inputText.value.trimEnd()
        sessionRepository.startDesktopVoice()
    }

    fun stopDesktopVoice() = sessionRepository.stopDesktopVoice()

    private fun applyDesktopVoiceComposer(update: DesktopVoiceUpdate) {
        if (update.sent) {
            phoneDictationPrefix = null
            _inputText.value = ""
            _isMicListening.value = false
            return
        }
        _inputText.value = update.text
    }

    fun showVoiceSettings() {
        _showVoiceSettings.value = true
    }

    fun dismissVoiceSettings() {
        _showVoiceSettings.value = false
    }

    fun setUsePhoneMicrophone(enabled: Boolean) {
        sessionStore.setUsePhoneMicrophone(enabled)
        sessionRepository.stopDesktopVoice()
        if (!enabled) {
            phoneDictationPrefix = null
            _isMicListening.value = false
        }
    }

    fun onSelectDisplay(displayId: String) {
        sessionRepository.selectDisplay(displayId)
    }

    fun onSelectRuntime(provider: String, model: String) {
        sessionRepository.selectRuntime(provider, model)
    }

    fun onRemoveAttachment(index: Int) {
        sessionRepository.removePendingAttachment(index)
    }

    fun onClearAttachments() {
        sessionRepository.clearPendingAttachments()
    }

    fun showNewTopicDialog() {
        _showNewTopicConfirmDialog.value = true
    }

    fun dismissNewTopicDialog() {
        _showNewTopicConfirmDialog.value = false
    }

    fun confirmNewTopic() {
        _showNewTopicConfirmDialog.value = false
        sessionRepository.newTopic()
    }

    fun clearError() {
        sessionRepository.clearError()
    }

    fun navigateToAccount() {
        viewModelScope.launch {
            _navigationEvent.emit(SessionNavigationEvent.NavigateToAccount)
        }
    }

    fun unpairAndNavigate() {
        viewModelScope.launch {
            val pairingId = sessionStore.activePairing.value?.pairingId
            sessionRepository.stopSession()
            if (!pairingId.isNullOrEmpty()) {
                pairingRepository.unpair(pairingId)
            } else {
                sessionStore.savePairing(null)
            }
            _navigationEvent.emit(SessionNavigationEvent.NavigateToPair)
        }
    }

    fun retryConnection() {
        val pairing = sessionStore.activePairing.value
        if (pairing != null) {
            sessionRepository.startSession(pairing.pairingId)
        }
    }

    override fun onCleared() {
        super.onCleared()
        sessionRepository.stopSession()
    }

    companion object {
        fun provideFactory(
            sessionRepository: SessionRepository,
            sessionStore: SessionStore,
            pairingRepository: PairingRepository
        ): ViewModelProvider.Factory =
            object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    return SessionViewModel(sessionRepository, sessionStore, pairingRepository) as T
                }
            }
    }
}
