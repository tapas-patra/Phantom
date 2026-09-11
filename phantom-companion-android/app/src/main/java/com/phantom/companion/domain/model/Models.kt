package com.phantom.companion.domain.model

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

@Serializable
data class ErrorBody(
    val error: String? = null,
    val message: String? = null
)

@Serializable
data class HealthResponse(
    val status: String = "",
    val service: String? = null,
    val utc: String? = null
)

@Serializable
data class AuthSession(
    val userId: String = "",
    val email: String = "",
    val accessToken: String = "",
    val refreshToken: String = "",
    val authMethod: String = "",
    val deviceInstallId: String = "",
    val deviceFingerprintHash: String = "",
    val authenticatedAtUtc: String? = null,
    val expiresAtUtc: String? = null,
    val isAuthenticated: Boolean = false
)

@Serializable
data class LoginRequest(
    val email: String,
    val password: String,
    val appVersion: String,
    val installId: String,
    val deviceLabel: String,
    val deviceFingerprintHash: String,
    val secretFingerprintHint: String
)

@Serializable
data class RefreshRequest(
    val refreshToken: String,
    val installId: String,
    val deviceFingerprintHash: String
)

@Serializable
data class LogoutRequest(
    val refreshToken: String
)

@Serializable
data class RevokedResponse(
    val revoked: Boolean = true
)

@Serializable
data class ForgotPasswordRequest(
    val email: String
)

@Serializable
data class MessageResponse(
    val message: String = ""
)

@Serializable
data class SpeechTranscriptionResponse(
    val text: String = "",
    val providerId: String = "",
    val modelId: String = ""
)

@Serializable
data class WalletSnapshot(
    val proAvailableCredits: Double = 0.0,
    val premiumAvailableCredits: Double = 0.0,
    val premiumNegativeCredits: Double = 0.0
)

@Serializable
data class HostedKnowledgeBase(
    val knowledgeBaseId: String = "",
    val name: String = "",
    val status: String = "not_created",
    val canUseInInterview: Boolean = false,
    val blockedReason: String = ""
)

@Serializable
data class StartupSnapshot(
    val userId: String = "",
    val email: String = "",
    val emailVerified: Boolean = false,
    val accessTier: String = "free",
    val phoneVerified: Boolean = false,
    val wallet: WalletSnapshot = WalletSnapshot(),
    val leaseExpiresAtUtc: String? = null,
    val hasResumableLockedSession: Boolean = false,
    val lastLockTokenHash: String = "",
    val lastLockedSessionId: String = "",
    val offlineModeEnabled: Boolean = false,
    val canUseDesktopPowerFeatures: Boolean = false,
    val lastValidatedAtUtc: String? = null,
    val hostedKnowledgeBase: HostedKnowledgeBase? = null,
    val source: String = ""
)

@Serializable
data class Pairing(
    val pairingId: String = "",
    val desktopDeviceLabel: String = "",
    val desktopPlatform: String = "",
    val companionDeviceLabel: String = "",
    val createdAtUtc: String = "",
    val relayRequired: Boolean = true,
    val desktopOnline: Boolean = false,
    val phoneOnline: Boolean = false
)

@Serializable
data class PairingsResponse(
    val pairings: List<Pairing> = emptyList()
)

@Serializable
data class PairingCompleteRequest(
    val code: String,
    val companionDeviceId: String,
    val companionDeviceLabel: String,
    val platform: String = "android",
    val appVersion: String
)

@Serializable
data class RelayTicketRequest(
    val pairingId: String,
    val role: String = "phone"
)

@Serializable
data class RelayTicket(
    val ticket: String,
    val expiresAtUtc: String? = null,
    val relayUrl: String? = null
)

@Serializable
data class DisplayInfo(
    val id: String = "0",
    val name: String = "Default Display",
    val isDefault: Boolean = true
)

@Serializable
data class ChatTurn(
    val role: String, // "user" | "assistant"
    val text: String,
    val atUtc: String? = null,
    val thumbnailBase64: String? = null,
    val isStreaming: Boolean = false,
    val isCancelled: Boolean = false,
    val isError: Boolean = false
)

@Serializable
data class ProviderOption(
    val id: String = "",
    val name: String = "",
    val models: List<ModelOption> = emptyList()
)

@Serializable
data class ModelOption(
    val id: String = "",
    val name: String = "",
    val vision: Boolean = false
)

@Serializable
data class PendingAttachment(
    val index: Int = 0,
    val thumbnailJpegBase64: String? = null
)

@Serializable
data class CompanionSessionSnapshot(
    val pairingId: String = "",
    val desktopStatus: String = "ready",
    val provider: String = "",
    val model: String = "",
    val vision: Boolean = true,
    val displays: List<DisplayInfo> = emptyList(),
    val selectedDisplayId: String = "0",
    val turns: List<ChatTurn> = emptyList()
)

/**
 * Typed body carried inside a [RelayEnvelope]. Per the frozen companion WebSocket contract
 * (docs/companion-backend-and-desktop.md §5), all payload fields live under `body`, not at
 * the envelope top level. The envelope itself only carries `v`, `id`, `type`, `ts`,
 * `pairingId`, `role`. This object aggregates every field the phone sends or receives so a
 * single decoder covers all frame types (unknown keys are ignored).
 */
@Serializable
data class RelayBody(
    // session.hello (phone → desktop)
    val appVersion: String? = null,
    val platform: String? = null,
    // capture.full / capture.ask / display.select (phone → desktop)
    val displayId: String? = null,
    val attachOnly: Boolean? = null,
    // capture.ask (phone → desktop)
    val prompt: String? = null,
    // chat.send / chat.delta / voice.transcript (phone ↔ desktop)
    val text: String? = null,
    val isFinal: Boolean? = null,
    val sent: Boolean? = null,
    // chat.cancel / capture.* / chat.* (request correlation)
    val requestId: String? = null,
    // chat.started
    val turnId: String? = null,
    // desktop.hello / desktop.status (desktop → phone)
    val status: String? = null,
    // session.snapshot (desktop → phone) — same shape as /sessions/current minus pairingId
    val desktopStatus: String? = null,
    val model: String? = null,
    val provider: String? = null,
    val vision: Boolean? = null,
    val displays: List<DisplayInfo>? = null,
    val lockExpiresAtUtc: String? = null,
    val selectedDisplayId: String? = null,
    val turns: List<ChatTurn>? = null,
    val attachmentCount: Int? = null,
    val attachments: List<PendingAttachment>? = null,
    val providers: List<ProviderOption>? = null,
    val index: Int? = null,
    // capture.completed
    val width: Int? = null,
    val height: Int? = null,
    val thumbnailJpegBase64: String? = null,
    // relay.error / capture.failed / chat.failed
    val code: String? = null,
    val message: String? = null
)

@Serializable
data class RelayEnvelope(
    val v: Int = 1,
    val id: String? = null,
    val type: String = "",
    val ts: String? = null,
    val pairingId: String? = null,
    val role: String? = null,
    val body: RelayBody? = null,
    // relay.error from the backend puts code/message at the envelope top level
    val code: String? = null,
    val message: String? = null
)

enum class DesktopPresenceState(val label: String) {
    OFFLINE("Desktop offline"),
    CONNECTING("Connecting"),
    IDLE("Idle"),
    READY("Ready"),
    CAPTURING("Capturing"),
    THINKING("Thinking"),
    ERROR("Error")
}
