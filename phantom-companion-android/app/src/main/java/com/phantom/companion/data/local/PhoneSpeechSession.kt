package com.phantom.companion.data.local

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer

/**
 * Native Android speech-to-text for companion voice input when the phone-mic
 * setting is enabled. Desktop cloud/native capture is used when it is off.
 */
class PhoneSpeechSession(
    context: Context,
    private val onPartial: (String) -> Unit,
    private val onFinal: (String) -> Unit,
    private val onError: (String) -> Unit
) {
    private val appContext = context.applicationContext
    private var recognizer: SpeechRecognizer? = null
    var isListening: Boolean = false
        private set

    fun start() {
        if (!SpeechRecognizer.isRecognitionAvailable(appContext)) {
            onError("Speech recognition is not available on this device.")
            return
        }
        stop()
        val rec = SpeechRecognizer.createSpeechRecognizer(appContext)
        rec.setRecognitionListener(object : RecognitionListener {
            override fun onReadyForSpeech(params: Bundle?) {}
            override fun onBeginningOfSpeech() {}
            override fun onRmsChanged(rmsdB: Float) {}
            override fun onBufferReceived(buffer: ByteArray?) {}
            override fun onEndOfSpeech() {}
            override fun onError(error: Int) {
                isListening = false
                if (error != SpeechRecognizer.ERROR_CLIENT && error != SpeechRecognizer.ERROR_NO_MATCH) {
                    onError("Could not capture speech. Try again.")
                }
            }
            override fun onResults(results: Bundle?) {
                isListening = false
                val text = results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
                    ?.firstOrNull()
                    .orEmpty()
                onFinal(text)
            }
            override fun onPartialResults(partialResults: Bundle?) {
                val text = partialResults?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
                    ?.firstOrNull()
                    .orEmpty()
                if (text.isNotBlank()) onPartial(text)
            }
            override fun onEvent(eventType: Int, params: Bundle?) {}
        })
        recognizer = rec
        val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true)
            putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
        }
        isListening = true
        rec.startListening(intent)
    }

    fun stop() {
        isListening = false
        try {
            recognizer?.stopListening()
        } catch (_: Exception) {
        }
        try {
            recognizer?.destroy()
        } catch (_: Exception) {
        }
        recognizer = null
    }
}

/**
 * Android speech partials are cumulative hypotheses for the current utterance, not
 * incremental deltas. The committed field prefix stays fixed for one listen session;
 * each hypothesis replaces the live tail so "hello" then "hello world" does not become
 * "hello hello world".
 */
fun mergePhoneDictation(prefix: String, hypothesis: String): String {
    val head = prefix.trimEnd()
    val spoken = hypothesis.trim()
    if (spoken.isEmpty()) return head
    if (head.isEmpty()) return spoken
    return "$head $spoken"
}
