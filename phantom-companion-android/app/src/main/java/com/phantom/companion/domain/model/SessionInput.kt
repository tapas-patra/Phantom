package com.phantom.companion.domain.model

const val DEFAULT_SCREEN_PROMPT = "Please analyze this screenshot."

fun shouldPublishComposer(current: String, lastPublished: String): Boolean =
    current != lastPublished

fun shouldApplyRemoteComposer(current: String, incoming: String, sent: Boolean): Boolean {
    if (sent) return current.isNotEmpty()
    return current != incoming
}

fun resolveFollowUpText(
    input: String,
    attachmentCount: Int,
    defaultPrompt: String = DEFAULT_SCREEN_PROMPT
): String? {
    val trimmed = input.trim()
    if (trimmed.isNotEmpty()) return trimmed
    if (attachmentCount > 0) return defaultPrompt
    return null
}

fun exposesProviderModelPickers(snapshot: StartupSnapshot?): Boolean {
    val tier = snapshot?.accessTier.orEmpty()
    if (tier.isBlank() || tier.equals("free", ignoreCase = true)) return false
    if (tier.equals("premium", ignoreCase = true)) return false
    val hasByo = (snapshot?.wallet?.proAvailableCredits ?: 0.0) > 0.0 ||
        tier.equals("pro_byo", ignoreCase = true) ||
        tier.equals("pro", ignoreCase = true)
    return hasByo
}

fun sessionRuntimeLabel(
    snapshot: StartupSnapshot?,
    provider: String?,
    model: String?
): String {
    if (!exposesProviderModelPickers(snapshot)) return "Phantom AI"
    return listOfNotNull(provider?.ifBlank { null }, model?.ifBlank { null })
        .joinToString(" / ")
        .ifBlank { "Phantom AI" }
}

fun mapDesktopPresence(
    status: String?,
    allowCapturing: Boolean = true,
    allowThinking: Boolean = true
): DesktopPresenceState {
    return when (status?.lowercase()) {
        "ready" -> DesktopPresenceState.READY
        "idle" -> DesktopPresenceState.IDLE
        "connecting" -> DesktopPresenceState.CONNECTING
        "capturing" -> if (allowCapturing) DesktopPresenceState.CAPTURING else DesktopPresenceState.READY
        "thinking" -> if (allowThinking) DesktopPresenceState.THINKING else DesktopPresenceState.READY
        "error" -> DesktopPresenceState.ERROR
        else -> DesktopPresenceState.OFFLINE
    }
}
