package com.phantom.companion.ui.session

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.remote.SocketConnectionState
import com.phantom.companion.data.repo.PairingRepository
import com.phantom.companion.data.repo.SessionRepository
import com.phantom.companion.domain.model.ChatTurn
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.DisplayInfo
import com.phantom.companion.domain.model.StartupSnapshot
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
    val isVisionSupported: StateFlow<Boolean> = sessionRepository.isVisionSupported
    val displays: StateFlow<List<DisplayInfo>> = sessionRepository.displays
    val selectedDisplayId: StateFlow<String> = sessionRepository.selectedDisplayId
    val transcript: StateFlow<List<ChatTurn>> = sessionRepository.transcript
    val currentPendingThumbnail: StateFlow<String?> = sessionRepository.currentPendingThumbnail
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
            sessionRepository.voiceTranscript.collect { text ->
                _inputText.value = if (_inputText.value.isBlank()) text else "${_inputText.value} $text"
            }
        }
    }

    fun onInputTextChanged(text: String) {
        _inputText.value = text
    }

    fun onCaptureAndAskClicked() {
        val displayId = selectedDisplayId.value
        sessionRepository.captureAndAsk(displayId = displayId)
    }

    fun onCaptureOnlyClicked() {
        val displayId = selectedDisplayId.value
        sessionRepository.captureOnly(displayId = displayId)
    }

    fun onSendFollowUpClicked() {
        val text = _inputText.value.trim()
        if (text.isNotEmpty()) {
            sessionRepository.sendFollowUp(text)
            _inputText.value = ""
        }
    }

    fun setMicListening(listening: Boolean) {
        _isMicListening.value = listening
    }

    fun appendDictatedText(text: String) {
        val trimmed = text.trim()
        if (trimmed.isEmpty()) return
        _inputText.value = if (_inputText.value.isBlank()) trimmed else "${_inputText.value.trim()} $trimmed"
    }

    fun startDesktopVoice() = sessionRepository.startDesktopVoice()
    fun stopDesktopVoice() = sessionRepository.stopDesktopVoice()

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
            _isMicListening.value = false
        }
    }

    fun onSelectDisplay(displayId: String) {
        sessionRepository.selectDisplay(displayId)
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
