package com.phantom.companion.data.repo

import com.phantom.companion.data.local.DeviceIdentityStore
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.remote.PhantomApi
import com.phantom.companion.domain.model.AuthSession
import com.phantom.companion.domain.model.ErrorBody
import com.phantom.companion.domain.model.ForgotPasswordRequest
import com.phantom.companion.domain.model.LoginRequest
import com.phantom.companion.domain.model.LogoutRequest
import com.phantom.companion.domain.model.Pairing
import com.phantom.companion.domain.model.StartupSnapshot
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.StateFlow
import kotlinx.serialization.json.Json
import retrofit2.HttpException
import java.io.IOException
import java.net.SocketTimeoutException

sealed class AuthResult<out T> {
    data class Success<T>(val data: T) : AuthResult<T>()
    data class Error(val message: String, val isColdStart: Boolean = false) : AuthResult<Nothing>()
    data class EmailNotVerified(val email: String) : AuthResult<Nothing>()
    data class AccountLocked(val message: String) : AuthResult<Nothing>()
}

class AuthRepository(
    private val api: PhantomApi,
    private val sessionStore: SessionStore,
    private val identityStore: DeviceIdentityStore
) {
    val sessionState: StateFlow<AuthSession?> = sessionStore.sessionState
    val startupSnapshot: StateFlow<StartupSnapshot?> = sessionStore.startupSnapshot
    val companionApiReady: StateFlow<Boolean?> = sessionStore.companionApiReady
    val activePairing: StateFlow<Pairing?> = sessionStore.activePairing

    private val json = Json { ignoreUnknownKeys = true }

    suspend fun login(email: String, password: String): AuthResult<StartupSnapshot> {
        val identity = identityStore.getIdentity()
        val request = LoginRequest(
            email = email.trim(),
            password = password,
            appVersion = identity.appVersion,
            installId = identity.installId,
            deviceLabel = identity.deviceLabel,
            deviceFingerprintHash = identity.deviceFingerprintHash,
            secretFingerprintHint = identity.secretFingerprintHint
        )

        return try {
            executeLoginWithColdStartRetry(request)
        } catch (e: Exception) {
            handleAuthException(e, request.email)
        }
    }

    private suspend fun executeLoginWithColdStartRetry(request: LoginRequest): AuthResult<StartupSnapshot> {
        try {
            val session = api.login(request)
            return finishSuccessfulLogin(session)
        } catch (e: Exception) {
            val isTimeout = e is SocketTimeoutException || e is IOException
            if (isTimeout) {
                // Cold-start retry logic: Ping health endpoint with 60s timeout
                try {
                    val health = api.health()
                    if (health.status.isNotEmpty()) {
                        delay(1000)
                        val retrySession = api.login(request)
                        return finishSuccessfulLogin(retrySession)
                    }
                } catch (healthErr: Exception) {
                    return AuthResult.Error("Waking Phantom… Please try again in a few seconds.", isColdStart = true)
                }
            }
            throw e
        }
    }

    private suspend fun finishSuccessfulLogin(session: AuthSession): AuthResult<StartupSnapshot> {
        sessionStore.saveSession(session)

        val startupSnapshot = try {
            api.startupCheck(session)
        } catch (e: Exception) {
            StartupSnapshot(
                userId = session.userId,
                email = session.email,
                emailVerified = true,
                accessTier = "free"
            )
        }
        sessionStore.saveStartupSnapshot(startupSnapshot)

        // Probe companion API capability
        probeCompanionApi()

        if (!startupSnapshot.emailVerified) {
            return AuthResult.EmailNotVerified(session.email)
        }

        return AuthResult.Success(startupSnapshot)
    }

    suspend fun probeCompanionApi(): Boolean {
        return try {
            val response = api.pairingsRaw()
            if (response.code() == 404) {
                sessionStore.setCompanionApiReady(false)
                false
            } else {
                // Route is mounted on the backend (e.g. 200, 400, 401)
                sessionStore.setCompanionApiReady(true)
                if (response.isSuccessful) {
                    val body = response.body()
                    if (body != null && body.pairings.isNotEmpty()) {
                        sessionStore.savePairing(body.pairings.first())
                    }
                }
                true
            }
        } catch (e: Exception) {
            // Keep existing state or default to true so users are not blocked on network blips
            if (sessionStore.companionApiReady.value == null) {
                sessionStore.setCompanionApiReady(true)
            }
            false
        }
    }

    suspend fun checkSessionOnStart(): AuthResult<StartupSnapshot>? {
        val currentSession = sessionStore.getSession() ?: return null

        return try {
            // Check health first
            api.health()

            // Verify session with me
            api.me()

            // Run full startup check
            val snapshot = api.startupCheck(currentSession)
            sessionStore.saveStartupSnapshot(snapshot)

            // Capability probe
            probeCompanionApi()

            if (!snapshot.emailVerified) {
                AuthResult.EmailNotVerified(currentSession.email)
            } else {
                AuthResult.Success(snapshot)
            }
        } catch (e: HttpException) {
            if (e.code() == 401) {
                sessionStore.clearSession()
                null
            } else {
                handleAuthException(e)
            }
        } catch (e: Exception) {
            AuthResult.Error("Waking Phantom…", isColdStart = true)
        }
    }

    suspend fun forgotPassword(email: String): String {
        return try {
            val response = api.forgotPassword(ForgotPasswordRequest(email.trim()))
            response.message.ifEmpty { "If that account exists, a password reset link has been sent." }
        } catch (e: Exception) {
            "If that account exists, a password reset link has been sent."
        }
    }

    suspend fun logout() {
        val refreshToken = sessionStore.getRefreshToken()
        try {
            if (refreshToken.isNotEmpty()) {
                api.logout(LogoutRequest(refreshToken))
            }
        } catch (e: Exception) {
            // Always clear session locally
        } finally {
            sessionStore.clearSession()
        }
    }

    private fun handleAuthException(e: Exception, email: String = ""): AuthResult<StartupSnapshot> {
        if (e is HttpException) {
            val errorJson = e.response()?.errorBody()?.string()
            if (!errorJson.isNullOrEmpty()) {
                try {
                    val parsed = json.decodeFromString<ErrorBody>(errorJson)
                    val msg = parsed.error ?: parsed.message
                    if (!msg.isNullOrEmpty()) {
                        if (msg.contains("temporarily locked", ignoreCase = true)) {
                            return AuthResult.AccountLocked(msg)
                        }
                        if (msg.contains("verify your email", ignoreCase = true)) {
                            val resolvedEmail = email.ifEmpty { sessionStore.getSession()?.email.orEmpty() }
                            return AuthResult.EmailNotVerified(resolvedEmail)
                        }
                        return AuthResult.Error(msg)
                    }
                } catch (pe: Exception) {
                    // Ignore parse error
                }
            }
            if (e.code() == 429) {
                return AuthResult.Error("Too many attempts. Wait a few seconds.")
            }
            if (e.code() == 400) {
                return AuthResult.Error("Invalid email or password.")
            }
        }
        return AuthResult.Error(e.message ?: "Invalid email or password.")
    }
}
