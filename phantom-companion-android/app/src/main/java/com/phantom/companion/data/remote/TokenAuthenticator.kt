package com.phantom.companion.data.remote

import com.phantom.companion.data.local.DeviceIdentityStore
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.domain.model.AuthSession
import com.phantom.companion.domain.model.RefreshRequest
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.Authenticator
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.Route
import java.util.UUID

class TokenAuthenticator(
    private val sessionStore: SessionStore,
    private val identityStore: DeviceIdentityStore,
    private val baseUrl: String,
    private val refreshHttpClient: OkHttpClient
) : Authenticator {

    private val json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
    }

    @Synchronized
    override fun authenticate(route: Route?, response: Response): Request? {
        val path = response.request.url.encodedPath
        if (path.contains("api/desktop/auth/login") || path.contains("api/desktop/auth/refresh")) {
            // Do not retry refresh or login to avoid loop
            return null
        }

        // Avoid infinite loop if we already retried with the same token
        val currentAccessToken = sessionStore.getAccessToken()
        val requestAuthHeader = response.request.header("Authorization")
        if (requestAuthHeader != null && requestAuthHeader != "Bearer $currentAccessToken") {
            // Already refreshed by another thread, retry with current token
            return response.request.newBuilder()
                .header("Authorization", "Bearer $currentAccessToken")
                .build()
        }

        val refreshToken = sessionStore.getRefreshToken()
        if (refreshToken.isEmpty()) {
            sessionStore.clearSession()
            return null
        }

        val identity = identityStore.getIdentity()
        val refreshPayload = RefreshRequest(
            refreshToken = refreshToken,
            installId = identity.installId,
            deviceFingerprintHash = identity.deviceFingerprintHash
        )

        val refreshUrl = baseUrl.trimEnd('/') + "/api/desktop/auth/refresh"
        val requestBody = json.encodeToString(refreshPayload)
            .toRequestBody("application/json".toMediaType())

        val refreshRequest = Request.Builder()
            .url(refreshUrl)
            .post(requestBody)
            .header("Content-Type", "application/json")
            .header("Accept", "application/json")
            .header("X-Phantom-Correlation-Id", UUID.randomUUID().toString())
            .header("X-Phantom-Operation-Id", UUID.randomUUID().toString())
            .removeHeader("Origin")
            .removeHeader("X-Phantom-CSRF")
            .build()

        try {
            val refreshResponse = refreshHttpClient.newCall(refreshRequest).execute()
            if (refreshResponse.isSuccessful) {
                val bodyString = refreshResponse.body?.string() ?: ""
                val newSession = json.decodeFromString<AuthSession>(bodyString)
                if (newSession.accessToken.isNotEmpty()) {
                    sessionStore.saveSession(newSession)
                    return response.request.newBuilder()
                        .header("Authorization", "Bearer ${newSession.accessToken}")
                        .build()
                }
            }
        } catch (e: Exception) {
            // Network failure during refresh
        }

        // Refresh failed -> clear session and return to Sign In
        sessionStore.clearSession()
        return null
    }
}
