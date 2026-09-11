package com.phantom.companion.data.local

import android.content.Context
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.phantom.companion.domain.model.AuthSession
import com.phantom.companion.domain.model.Pairing
import com.phantom.companion.domain.model.StartupSnapshot
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone

class SessionStore(private val context: Context) {

    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    private val prefs by lazy {
        try {
            val masterKey = MasterKey.Builder(context)
                .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
                .build()
            EncryptedSharedPreferences.create(
                context,
                PREFS_NAME,
                masterKey,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
            )
        } catch (e: Throwable) {
            context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        }
    }

    private val _sessionState = MutableStateFlow<AuthSession?>(null)
    val sessionState: StateFlow<AuthSession?> = _sessionState.asStateFlow()

    private val _activePairing = MutableStateFlow<Pairing?>(null)
    val activePairing: StateFlow<Pairing?> = _activePairing.asStateFlow()

    private val _companionApiReady = MutableStateFlow<Boolean?>(null)
    val companionApiReady: StateFlow<Boolean?> = _companionApiReady.asStateFlow()

    private val _startupSnapshot = MutableStateFlow<StartupSnapshot?>(null)
    val startupSnapshot: StateFlow<StartupSnapshot?> = _startupSnapshot.asStateFlow()

    init {
        loadFromPrefs()
    }

    @Synchronized
    private fun loadFromPrefs() {
        val sessionJson = prefs.getString(KEY_AUTH_SESSION, null)
        if (!sessionJson.isNullOrEmpty()) {
            try {
                val session = json.decodeFromString<AuthSession>(sessionJson)
                if (session.accessToken.isNotEmpty()) {
                    _sessionState.value = session
                }
            } catch (e: Exception) {
                // Ignore corrupted state
            }
        }

        val pairingJson = prefs.getString(KEY_PAIRING, null)
        if (!pairingJson.isNullOrEmpty()) {
            try {
                _activePairing.value = json.decodeFromString<Pairing>(pairingJson)
            } catch (e: Exception) {
                // Ignore
            }
        }

        val startupJson = prefs.getString(KEY_STARTUP, null)
        if (!startupJson.isNullOrEmpty()) {
            try {
                _startupSnapshot.value = json.decodeFromString<StartupSnapshot>(startupJson)
            } catch (e: Exception) {
                // Ignore
            }
        }
    }

    @Synchronized
    fun getSession(): AuthSession? = _sessionState.value

    @Synchronized
    fun getAccessToken(): String = _sessionState.value?.accessToken.orEmpty()

    @Synchronized
    fun getRefreshToken(): String = _sessionState.value?.refreshToken.orEmpty()

    @Synchronized
    fun saveSession(session: AuthSession) {
        _sessionState.value = session
        try {
            prefs.edit()
                .putString(KEY_AUTH_SESSION, json.encodeToString(session))
                .apply()
        } catch (e: Exception) {
            // Ignore
        }
    }

    @Synchronized
    fun updateTokens(accessToken: String, refreshToken: String) {
        val current = _sessionState.value ?: return
        val updated = current.copy(
            accessToken = accessToken,
            refreshToken = refreshToken,
            isAuthenticated = true
        )
        saveSession(updated)
    }

    @Synchronized
    fun saveStartupSnapshot(snapshot: StartupSnapshot) {
        _startupSnapshot.value = snapshot
        try {
            prefs.edit()
                .putString(KEY_STARTUP, json.encodeToString(snapshot))
                .apply()
        } catch (e: Exception) {
            // Ignore
        }
    }

    @Synchronized
    fun savePairing(pairing: Pairing?) {
        _activePairing.value = pairing
        try {
            val editor = prefs.edit()
            if (pairing != null) {
                editor.putString(KEY_PAIRING, json.encodeToString(pairing))
            } else {
                editor.remove(KEY_PAIRING)
            }
            editor.apply()
        } catch (e: Exception) {
            // Ignore
        }
    }

    @Synchronized
    fun setCompanionApiReady(ready: Boolean) {
        _companionApiReady.value = ready
    }

    @Synchronized
    fun clearSession() {
        _sessionState.value = null
        _activePairing.value = null
        _startupSnapshot.value = null
        _companionApiReady.value = null
        try {
            prefs.edit().clear().apply()
        } catch (e: Exception) {
            // Ignore
        }
    }

    /**
     * Checks if token should be proactively refreshed (at 80% remaining TTL).
     */
    fun shouldRefreshToken(): Boolean {
        val session = _sessionState.value ?: return false
        val expiresAtUtc = session.expiresAtUtc ?: return false
        val authenticatedAtUtc = session.authenticatedAtUtc ?: return false

        val authTime = parseIsoDate(authenticatedAtUtc) ?: return false
        val expireTime = parseIsoDate(expiresAtUtc) ?: return false
        val totalLifetime = expireTime - authTime
        if (totalLifetime <= 0) return false

        val now = System.currentTimeMillis()
        val elapsed = now - authTime
        // Refresh when elapsed >= 80% of total lifetime
        return elapsed >= (totalLifetime * 0.8)
    }

    companion object {
        private const val PREFS_NAME = "phantom_session_store"
        private const val KEY_AUTH_SESSION = "auth_session"
        private const val KEY_PAIRING = "current_pairing"
        private const val KEY_STARTUP = "startup_snapshot"

        fun parseIsoDate(isoString: String): Long? {
            val formats = arrayOf(
                "yyyy-MM-dd'T'HH:mm:ss.SSSSSSS'Z'",
                "yyyy-MM-dd'T'HH:mm:ss.SSSSSSS",
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                "yyyy-MM-dd'T'HH:mm:ss"
            )
            for (pattern in formats) {
                try {
                    val sdf = SimpleDateFormat(pattern, Locale.US).apply {
                        timeZone = TimeZone.getTimeZone("UTC")
                    }
                    val date = sdf.parse(isoString)
                    if (date != null) return date.time
                } catch (e: Exception) {
                    // Try next pattern
                }
            }
            return null
        }
    }
}
