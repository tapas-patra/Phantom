package com.phantom.companion.data.speech

/** Matches android.speech.SpeechRecognizer error codes (kept here for JVM tests). */
object NativeSpeechErrors {
    const val CLIENT = 5
    const val SPEECH_TIMEOUT = 6
    const val NO_MATCH = 7
    const val RECOGNIZER_BUSY = 8
}

fun shouldRestartNativeSpeech(error: Int, stillListening: Boolean): Boolean {
    if (!stillListening) return false
    return error == NativeSpeechErrors.CLIENT ||
        error == NativeSpeechErrors.SPEECH_TIMEOUT ||
        error == NativeSpeechErrors.NO_MATCH ||
        error == NativeSpeechErrors.RECOGNIZER_BUSY
}

fun nativeSpeechRestartDelayMs(error: Int): Long =
    if (error == NativeSpeechErrors.RECOGNIZER_BUSY) 250L else 100L
