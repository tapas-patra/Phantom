package com.phantom.companion.di

import android.content.Context
import com.phantom.companion.data.local.DeviceIdentityStore
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.remote.NetworkClient
import com.phantom.companion.data.remote.PhantomApi
import com.phantom.companion.data.remote.RelayClient
import com.phantom.companion.data.repo.AuthRepository
import com.phantom.companion.data.repo.PairingRepository
import com.phantom.companion.data.repo.SessionRepository

class AppContainer(private val context: Context) {

    val deviceIdentityStore: DeviceIdentityStore by lazy {
        DeviceIdentityStore(context)
    }

    val sessionStore: SessionStore by lazy {
        SessionStore(context)
    }

    val networkClient: NetworkClient by lazy {
        NetworkClient(sessionStore, deviceIdentityStore)
    }

    val api: PhantomApi by lazy {
        networkClient.api
    }

    val relayClient: RelayClient by lazy {
        RelayClient(sessionStore, api, networkClient.baseUrl)
    }

    val authRepository: AuthRepository by lazy {
        AuthRepository(api, sessionStore, deviceIdentityStore)
    }

    val pairingRepository: PairingRepository by lazy {
        PairingRepository(api, sessionStore, deviceIdentityStore)
    }

    val sessionRepository: SessionRepository by lazy {
        SessionRepository(relayClient, sessionStore, api)
    }
}
