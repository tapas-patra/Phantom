package com.phantom.companion.data.remote

import com.phantom.companion.BuildConfig
import com.phantom.companion.data.local.DeviceIdentityStore
import com.phantom.companion.data.local.SessionStore
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.util.concurrent.TimeUnit

class NetworkClient(
    private val sessionStore: SessionStore,
    private val identityStore: DeviceIdentityStore,
    val baseUrl: String = BuildConfig.PHANTOM_API_BASE_URL
) {

    val json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        encodeDefaults = true
    }

    private val baseLoggingInterceptor = HttpLoggingInterceptor().apply {
        level = if (BuildConfig.DEBUG) {
            // HEADERS only: BODY would dump login passwords and access/refresh tokens into
            // logcat (H2). Even in DEBUG, redact the Authorization header so bearer tokens
            // never reach the log stream.
            HttpLoggingInterceptor.Level.HEADERS
        } else {
            HttpLoggingInterceptor.Level.NONE
        }
        redactHeader("Authorization")
        redactHeader("X-Phantom-Correlation-Id")
        redactHeader("X-Phantom-Operation-Id")
    }

    // Refresh-specific client without the authenticator to avoid recursion
    private val refreshHttpClient: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .addInterceptor(baseLoggingInterceptor)
        .build()

    private val authInterceptor = AuthInterceptor(sessionStore)
    private val tokenAuthenticator = TokenAuthenticator(
        sessionStore = sessionStore,
        identityStore = identityStore,
        baseUrl = baseUrl,
        refreshHttpClient = refreshHttpClient
    )

    val okHttpClient: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .writeTimeout(30, TimeUnit.SECONDS)
        .addInterceptor(authInterceptor)
        .authenticator(tokenAuthenticator)
        .addInterceptor(baseLoggingInterceptor)
        .build()

    private val normalizedBaseUrl = if (baseUrl.endsWith("/")) baseUrl else "$baseUrl/"

    val retrofit: Retrofit = Retrofit.Builder()
        .baseUrl(normalizedBaseUrl)
        .client(okHttpClient)
        .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
        .build()

    val api: PhantomApi = retrofit.create(PhantomApi::class.java)
}
