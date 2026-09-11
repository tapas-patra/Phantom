package com.phantom.companion.data.remote

import com.phantom.companion.data.local.SessionStore
import okhttp3.Interceptor
import okhttp3.Response
import java.util.UUID

class AuthInterceptor(
    private val sessionStore: SessionStore
) : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val originalRequest = chain.request()
        val path = originalRequest.url.encodedPath

        val isPublicRoute = path.endsWith("health") ||
                path.endsWith("api/desktop/auth/login") ||
                path.endsWith("api/desktop/auth/forgot-password")

        val isMultipart = originalRequest.body?.contentType()?.type.equals("multipart", ignoreCase = true)
        val builder = originalRequest.newBuilder()
            .header("Accept", "application/json")
            .header("X-Phantom-Correlation-Id", UUID.randomUUID().toString())
            .header("X-Phantom-Operation-Id", UUID.randomUUID().toString())
            // CRITICAL AUTH RULE: Never send Origin or X-Phantom-CSRF
            .removeHeader("Origin")
            .removeHeader("X-Phantom-CSRF")

        if (!isMultipart) {
            builder.header("Content-Type", "application/json")
        }

        if (!isPublicRoute) {
            val token = sessionStore.getAccessToken()
            if (token.isNotEmpty()) {
                builder.header("Authorization", "Bearer $token")
            }
        }

        return chain.proceed(builder.build())
    }
}
