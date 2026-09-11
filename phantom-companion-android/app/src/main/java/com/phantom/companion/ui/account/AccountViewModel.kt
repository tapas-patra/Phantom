package com.phantom.companion.ui.account

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.repo.AuthRepository
import com.phantom.companion.data.repo.PairingRepository
import com.phantom.companion.domain.model.Pairing
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
    private val sessionStore: SessionStore
) : ViewModel() {

    val startupSnapshot: StateFlow<StartupSnapshot?> = sessionStore.startupSnapshot
    val activePairing: StateFlow<Pairing?> = sessionStore.activePairing
    val companionApiReady: StateFlow<Boolean?> = sessionStore.companionApiReady

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

    companion object {
        fun provideFactory(
            authRepository: AuthRepository,
            pairingRepository: PairingRepository,
            sessionStore: SessionStore
        ): ViewModelProvider.Factory =
            object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    return AccountViewModel(authRepository, pairingRepository, sessionStore) as T
                }
            }
    }
}
