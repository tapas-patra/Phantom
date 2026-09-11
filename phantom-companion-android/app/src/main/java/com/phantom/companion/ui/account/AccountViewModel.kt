package com.phantom.companion.ui.account

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.repo.AuthRepository
import com.phantom.companion.data.repo.PairingRepository
import com.phantom.companion.data.repo.SessionRepository
import com.phantom.companion.domain.model.Pairing
import com.phantom.companion.domain.model.ProviderOption
import com.phantom.companion.domain.model.StartupSnapshot
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

sealed class AccountNavigationEvent {
    data object NavigateToSignIn : AccountNavigationEvent()
    data object NavigateToPair : AccountNavigationEvent()
}

class AccountViewModel(
    private val authRepository: AuthRepository,
    private val pairingRepository: PairingRepository,
    private val sessionStore: SessionStore,
    private val sessionRepository: SessionRepository
) : ViewModel() {

    val startupSnapshot: StateFlow<StartupSnapshot?> = sessionStore.startupSnapshot
    val activePairing: StateFlow<Pairing?> = sessionStore.activePairing
    val companionApiReady: StateFlow<Boolean?> = sessionStore.companionApiReady
    val usePhoneMicrophone: StateFlow<Boolean> = sessionStore.usePhoneMicrophone
    val currentModel = sessionRepository.currentModel
    val currentProvider = sessionRepository.currentProvider
    val providers: StateFlow<List<ProviderOption>> = sessionRepository.providers

    private val _isUnpairing = MutableStateFlow(false)
    val isUnpairing: StateFlow<Boolean> = _isUnpairing.asStateFlow()

    private val _isSigningOut = MutableStateFlow(false)
    val isSigningOut: StateFlow<Boolean> = _isSigningOut.asStateFlow()

    private val _showUnpairConfirmDialog = MutableStateFlow(false)
    val showUnpairConfirmDialog: StateFlow<Boolean> = _showUnpairConfirmDialog.asStateFlow()

    private val _navigationEvent = MutableSharedFlow<AccountNavigationEvent>()
    val navigationEvent: SharedFlow<AccountNavigationEvent> = _navigationEvent.asSharedFlow()

    fun showUnpairDialog() {
        _showUnpairConfirmDialog.value = true
    }

    fun dismissUnpairDialog() {
        _showUnpairConfirmDialog.value = false
    }

    fun confirmUnpair() {
        val current = activePairing.value ?: return
        _showUnpairConfirmDialog.value = false
        _isUnpairing.value = true

        viewModelScope.launch {
            pairingRepository.unpair(current.pairingId)
            _isUnpairing.value = false
            _navigationEvent.emit(AccountNavigationEvent.NavigateToPair)
        }
    }

    fun signOut() {
        _isSigningOut.value = true
        viewModelScope.launch {
            authRepository.logout()
            _isSigningOut.value = false
            _navigationEvent.emit(AccountNavigationEvent.NavigateToSignIn)
        }
    }

    fun navigateToPair() {
        viewModelScope.launch {
            _navigationEvent.emit(AccountNavigationEvent.NavigateToPair)
        }
    }

    fun setUsePhoneMicrophone(enabled: Boolean) {
        sessionStore.setUsePhoneMicrophone(enabled)
    }

    fun selectRuntime(provider: String, model: String) {
        sessionRepository.selectRuntime(provider, model)
    }

    companion object {
        fun provideFactory(
            authRepository: AuthRepository,
            pairingRepository: PairingRepository,
            sessionStore: SessionStore,
            sessionRepository: SessionRepository
        ): ViewModelProvider.Factory =
            object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    return AccountViewModel(authRepository, pairingRepository, sessionStore, sessionRepository) as T
                }
            }
    }
}
