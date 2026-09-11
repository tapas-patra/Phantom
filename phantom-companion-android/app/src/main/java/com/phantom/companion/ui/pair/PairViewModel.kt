package com.phantom.companion.ui.pair

import android.net.Uri
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.phantom.companion.BuildConfig
import com.phantom.companion.data.repo.PairingRepository
import com.phantom.companion.data.repo.PairingResult
import com.phantom.companion.domain.model.Pairing
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

sealed class PairNavigationEvent {
    data class NavigateToSession(val pairing: Pairing) : PairNavigationEvent()
    data object NavigateToAccount : PairNavigationEvent()
}

class PairViewModel(
    private val pairingRepository: PairingRepository
) : ViewModel() {

    private val _code = MutableStateFlow("")
    val code: StateFlow<String> = _code.asStateFlow()

    private val _isLoading = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading.asStateFlow()

    private val _errorMessage = MutableStateFlow<String?>(null)
    val errorMessage: StateFlow<String?> = _errorMessage.asStateFlow()

    private val _isBackendNotReady = MutableStateFlow(false)
    val isBackendNotReady: StateFlow<Boolean> = _isBackendNotReady.asStateFlow()

    private val _navigationEvent = MutableSharedFlow<PairNavigationEvent>()
    val navigationEvent: SharedFlow<PairNavigationEvent> = _navigationEvent.asSharedFlow()

    init {
        // Observe if active pairing already exists
        viewModelScope.launch {
            pairingRepository.activePairing.collect { active ->
                if (active != null) {
                    _navigationEvent.emit(PairNavigationEvent.NavigateToSession(active))
                }
            }
        }
        viewModelScope.launch {
            pairingRepository.companionApiReady.collect { ready ->
                _isBackendNotReady.value = (ready == false)
            }
        }
    }

    fun onCodeChanged(input: String) {
        // Normalize: uppercase and keep only A-Z and digits 2-9 (no 0, O, I, 1)
        val sanitized = input.uppercase()
            .replace(Regex("[^2-9A-HJ-NP-Z]"), "")
            .take(6)
        _code.value = sanitized
        _errorMessage.value = null

        if (sanitized.length == 6) {
            submitPairing(sanitized)
        }
    }

    fun onQrScanned(rawPayload: String) {
        val extractedCode = parseQrPayload(rawPayload)
        if (!extractedCode.isNullOrEmpty()) {
            _code.value = extractedCode
            submitPairing(extractedCode)
        } else {
            _errorMessage.value = "Unrecognized QR code format."
        }
    }

    fun submitPairing(pairingCode: String = _code.value) {
        val sanitized = pairingCode.trim().uppercase()
        if (sanitized.length != 6) {
            _errorMessage.value = "Pairing code must be 6 characters."
            return
        }

        _isLoading.value = true
        _errorMessage.value = null

        viewModelScope.launch {
            when (val result = pairingRepository.completePairing(sanitized)) {
                is PairingResult.Success -> {
                    _isLoading.value = false
                    _navigationEvent.emit(PairNavigationEvent.NavigateToSession(result.pairing))
                }
                is PairingResult.BackendNotReady -> {
                    _isLoading.value = false
                    _isBackendNotReady.value = true
                    _errorMessage.value = "Desktop companion service is not enabled on this backend yet."
                }
                is PairingResult.Error -> {
                    _isLoading.value = false
                    _errorMessage.value = result.message
                }
            }
        }
    }

    private fun parseQrPayload(raw: String): String? {
        val trimmed = raw.trim()
        if (trimmed.startsWith("phantom-companion://pair", ignoreCase = true)) {
            try {
                val uri = Uri.parse(trimmed)
                val codeParam = uri.getQueryParameter("code")
                val relayParam = uri.getQueryParameter("relay")

                // Host check rule: ignore relay unless it matches BuildConfig base URL
                val expectedHost = BuildConfig.PHANTOM_API_BASE_URL.trimEnd('/')
                if (!relayParam.isNullOrEmpty() && !relayParam.startsWith(expectedHost)) {
                    // Mismatched host from untrusted QR
                    return null
                }

                if (!codeParam.isNullOrEmpty()) {
                    return codeParam.uppercase().replace(Regex("[^2-9A-HJ-NP-Z]"), "").take(6)
                }
            } catch (e: Exception) {
                return null
            }
        }

        // Direct 6-character code in QR
        val direct = trimmed.uppercase().replace(Regex("[^2-9A-HJ-NP-Z]"), "")
        if (direct.length == 6) {
            return direct
        }

        return null
    }

    companion object {
        fun provideFactory(pairingRepository: PairingRepository): ViewModelProvider.Factory =
            object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    return PairViewModel(pairingRepository) as T
                }
            }
    }
}
