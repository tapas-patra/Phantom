package com.phantom.companion.data.repo

import com.phantom.companion.data.local.DeviceIdentityStore
import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.data.remote.PhantomApi
import com.phantom.companion.domain.model.ErrorBody
import com.phantom.companion.domain.model.Pairing
import com.phantom.companion.domain.model.PairingCompleteRequest
import kotlinx.coroutines.flow.StateFlow
import kotlinx.serialization.json.Json
import retrofit2.HttpException

sealed class PairingResult {
    data class Success(val pairing: Pairing) : PairingResult()
    data class Error(val message: String) : PairingResult()
    data object BackendNotReady : PairingResult()
}

class PairingRepository(
    private val api: PhantomApi,
    private val sessionStore: SessionStore,
    private val identityStore: DeviceIdentityStore
) {
    val activePairing: StateFlow<Pairing?> = sessionStore.activePairing
    val companionApiReady: StateFlow<Boolean?> = sessionStore.companionApiReady

    private val json = Json { ignoreUnknownKeys = true }

    suspend fun completePairing(rawCode: String): PairingResult {
        // Clean and uppercase the 6-character code (A-Z2-9, excluding 0, O, 1, I)
        val sanitizedCode = rawCode.trim().uppercase().replace(Regex("[^2-9A-HJ-NP-Z]"), "")
        if (sanitizedCode.length != 6) {
            return PairingResult.Error("Pairing code must be 6 characters.")
        }

        val identity = identityStore.getIdentity()
        val sessionDeviceId = sessionStore.getSession()?.deviceInstallId?.ifEmpty { null }
        val companionDeviceId = sessionDeviceId ?: identity.installId

        val request = PairingCompleteRequest(
            code = sanitizedCode,
            companionDeviceId = companionDeviceId,
            companionDeviceLabel = identity.deviceLabel,
            platform = "android",
            appVersion = identity.appVersion
        )

        return try {
            val pairing = api.completePairing(request)
            sessionStore.savePairing(pairing)
            sessionStore.setCompanionApiReady(true)
            PairingResult.Success(pairing)
        } catch (e: HttpException) {
            if (e.code() == 404) {
                sessionStore.setCompanionApiReady(false)
                PairingResult.BackendNotReady
            } else {
                val errorMsg = parseErrorMessage(e)
                PairingResult.Error(errorMsg)
            }
        } catch (e: Exception) {
            PairingResult.Error(e.message ?: "Failed to complete pairing.")
        }
    }

    suspend fun checkPairings(): List<Pairing> {
        return try {
            val response = api.pairingsRaw()
            if (response.code() == 404) {
                sessionStore.setCompanionApiReady(false)
                emptyList()
            } else {
                sessionStore.setCompanionApiReady(true)
                if (response.isSuccessful) {
                    val list = response.body()?.pairings.orEmpty()
                    if (list.isNotEmpty()) {
                        sessionStore.savePairing(list.first())
                    }
                    list
                } else {
                    emptyList()
                }
            }
        } catch (e: Exception) {
            emptyList()
        }
    }

    suspend fun unpair(pairingId: String) {
        try {
            api.revokePairing(pairingId)
        } catch (e: Exception) {
            // Ignore failure and still clear locally
        } finally {
            sessionStore.savePairing(null)
        }
    }

    private fun parseErrorMessage(e: HttpException): String {
        val body = e.response()?.errorBody()?.string()
        if (!body.isNullOrEmpty()) {
            try {
                val errorBody = json.decodeFromString<ErrorBody>(body)
                val msg = errorBody.error ?: errorBody.message
                if (!msg.isNullOrEmpty()) {
                    return msg
                }
            } catch (ex: Exception) {
                // Ignore
            }
        }
        return when (e.code()) {
            400 -> "Pairing code is invalid or expired."
            409 -> "This desktop already has an active companion. Revoke it first."
            429 -> "Too many attempts. Wait a few seconds."
            else -> "Failed to pair with desktop (HTTP ${e.code()})."
        }
    }
}
