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
