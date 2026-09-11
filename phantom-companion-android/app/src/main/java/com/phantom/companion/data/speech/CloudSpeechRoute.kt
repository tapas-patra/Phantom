package com.phantom.companion.data.speech

enum class SpeechCaptureMode {
    Cloud,
    Native
}

/**
 * Desktop-parity routing: try cloud first, native on failure, retry cloud on the
 * next mic press, then 5/10/15 minute native cooldowns for later failures.
 */
class CloudSpeechRoute(
    private val cooldownMinutes: IntArray = intArrayOf(5, 10, 15),
    private val nowMs: () -> Long = { System.currentTimeMillis() }
) {
    private var forceNative = false
    private var immediateProbePending = false
    private var immediateProbeConsumed = false
    private var pendingRecoveryAfterNative = false
    private var cooldownUntilMs = 0L
    private var cooldownStep = 0

    fun startMode(preferCloud: Boolean): SpeechCaptureMode {
        if (!preferCloud) {
            reset()
            return SpeechCaptureMode.Native
        }
        if (!forceNative) return SpeechCaptureMode.Cloud
        if (immediateProbePending) {
            immediateProbePending = false
            forceNative = false
            return SpeechCaptureMode.Cloud
        }
        if (cooldownUntilMs > 0 && nowMs() >= cooldownUntilMs) {
            pendingRecoveryAfterNative = true
            return SpeechCaptureMode.Native
        }
        return SpeechCaptureMode.Native
    }

    fun onCloudSuccess() {
        reset()
    }

    fun onCloudFailure() {
        forceNative = true
        if (!immediateProbeConsumed) {
            immediateProbePending = true
            immediateProbeConsumed = true
            cooldownUntilMs = 0L
        } else {
            immediateProbePending = false
            val last = cooldownMinutes.lastIndex.coerceAtLeast(0)
            val minutes = cooldownMinutes[cooldownStep.coerceAtMost(last)]
            cooldownUntilMs = nowMs() + minutes * 60_000L
            if (cooldownStep < last) cooldownStep++
        }
    }

    fun onNativeUtteranceFinished() {
        if (pendingRecoveryAfterNative) reset()
    }

    private fun reset() {
        forceNative = false
        immediateProbePending = false
        immediateProbeConsumed = false
        pendingRecoveryAfterNative = false
        cooldownUntilMs = 0L
        cooldownStep = 0
    }
}
