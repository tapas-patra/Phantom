package com.phantom.companion.ui.signin

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.phantom.companion.data.repo.AuthRepository
import com.phantom.companion.data.repo.AuthResult
import com.phantom.companion.domain.model.StartupSnapshot
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

sealed class SignInNavigationEvent {
    data object NavigateToSession : SignInNavigationEvent()
    data object NavigateToPair : SignInNavigationEvent()
    data class NavigateToGate(val reason: String, val email: String) : SignInNavigationEvent()
}

class SignInViewModel(
    private val authRepository: AuthRepository
) : ViewModel() {

    private val _email = MutableStateFlow("")
    val email: StateFlow<String> = _email.asStateFlow()

    private val _password = MutableStateFlow("")
    val password: StateFlow<String> = _password.asStateFlow()

    private val _isLoading = MutableStateFlow(false)
    val isLoading: StateFlow<Boolean> = _isLoading.asStateFlow()

    private val _emailError = MutableStateFlow<String?>(null)
    val emailError: StateFlow<String?> = _emailError.asStateFlow()

    private val _passwordError = MutableStateFlow<String?>(null)
    val passwordError: StateFlow<String?> = _passwordError.asStateFlow()

    private val _generalError = MutableStateFlow<String?>(null)
    val generalError: StateFlow<String?> = _generalError.asStateFlow()

    private val _isColdStarting = MutableStateFlow(false)
    val isColdStarting: StateFlow<Boolean> = _isColdStarting.asStateFlow()

    private val _forgotPasswordDialogVisible = MutableStateFlow(false)
    val forgotPasswordDialogVisible: StateFlow<Boolean> = _forgotPasswordDialogVisible.asStateFlow()

    private val _forgotPasswordEmail = MutableStateFlow("")
    val forgotPasswordEmail: StateFlow<String> = _forgotPasswordEmail.asStateFlow()

    private val _forgotPasswordMessage = MutableStateFlow<String?>(null)
    val forgotPasswordMessage: StateFlow<String?> = _forgotPasswordMessage.asStateFlow()

    private val _forgotPasswordLoading = MutableStateFlow(false)
    val forgotPasswordLoading: StateFlow<Boolean> = _forgotPasswordLoading.asStateFlow()

    private val _navigationEvent = MutableSharedFlow<SignInNavigationEvent>()
    val navigationEvent: SharedFlow<SignInNavigationEvent> = _navigationEvent.asSharedFlow()

    fun onEmailChanged(value: String) {
        _email.value = value
        _emailError.value = null
        _generalError.value = null
    }

    fun onPasswordChanged(value: String) {
        _password.value = value
        _passwordError.value = null
        _generalError.value = null
    }

    fun onSignInClicked() {
        val currentEmail = _email.value.trim()
        val currentPassword = _password.value

        var hasValidationError = false
        if (currentEmail.isEmpty()) {
            _emailError.value = "Email is required."
            hasValidationError = true
        }
        if (currentPassword.isEmpty()) {
            _passwordError.value = "Password is required."
            hasValidationError = true
        }
        if (hasValidationError) return

        _isLoading.value = true
        _generalError.value = null
        _isColdStarting.value = false

        viewModelScope.launch {
            when (val result = authRepository.login(currentEmail, currentPassword)) {
                is AuthResult.Success -> {
                    _isLoading.value = false
                    routeAfterLogin(result.data)
                }
                is AuthResult.EmailNotVerified -> {
                    _isLoading.value = false
                    _navigationEvent.emit(
                        SignInNavigationEvent.NavigateToGate(
                            reason = "email_not_verified",
                            email = result.email
                        )
                    )
                }
                is AuthResult.AccountLocked -> {
                    _isLoading.value = false
                    _navigationEvent.emit(
                        SignInNavigationEvent.NavigateToGate(
                            reason = "account_locked",
                            email = currentEmail
                        )
                    )
                }
                is AuthResult.Error -> {
                    _isLoading.value = false
                    _generalError.value = result.message
                    if (result.isColdStart) {
                        _isColdStarting.value = true
                    }
                }
            }
        }
    }

    private suspend fun routeAfterLogin(snapshot: StartupSnapshot) {
        val hasPairing = authRepository.activePairing.value != null
        if (hasPairing) {
            _navigationEvent.emit(SignInNavigationEvent.NavigateToSession)
        } else {
            _navigationEvent.emit(SignInNavigationEvent.NavigateToPair)
        }
    }

    fun showForgotPasswordDialog() {
        _forgotPasswordEmail.value = _email.value
        _forgotPasswordMessage.value = null
        _forgotPasswordDialogVisible.value = true
    }

    fun dismissForgotPasswordDialog() {
        _forgotPasswordDialogVisible.value = false
        _forgotPasswordMessage.value = null
    }

    fun onForgotPasswordEmailChanged(value: String) {
        _forgotPasswordEmail.value = value
    }

    fun submitForgotPassword() {
        val email = _forgotPasswordEmail.value.trim()
        if (email.isEmpty()) return

        _forgotPasswordLoading.value = true
        viewModelScope.launch {
            val message = authRepository.forgotPassword(email)
            _forgotPasswordLoading.value = false
            _forgotPasswordMessage.value = message
        }
    }

    companion object {
        fun provideFactory(authRepository: AuthRepository): ViewModelProvider.Factory =
            object : ViewModelProvider.Factory {
                @Suppress("UNCHECKED_CAST")
                override fun <T : ViewModel> create(modelClass: Class<T>): T {
                    return SignInViewModel(authRepository) as T
                }
            }
    }
}
