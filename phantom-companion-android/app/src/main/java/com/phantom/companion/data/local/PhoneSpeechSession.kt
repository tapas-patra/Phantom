package com.phantom.companion.data.local

import android.content.Context
import android.content.Intent
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.speech.RecognitionListener
import android.speech.RecognizerIntent
import android.speech.SpeechRecognizer
import android.util.Log
import com.phantom.companion.data.speech.CloudSpeechRoute
import com.phantom.companion.data.speech.SPEECH_CHUNK_BYTES
import com.phantom.companion.data.speech.SPEECH_SAMPLE_RATE
import com.phantom.companion.data.speech.SpeechCaptureMode
import com.phantom.companion.data.speech.nativeSpeechRestartDelayMs
import com.phantom.companion.data.speech.resampleTo16kPcm
import com.phantom.companion.data.speech.shouldRestartNativeSpeech
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.ByteArrayOutputStream
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CancellationException
import kotlin.math.max

/**
 * Phone-mic speech: Premium tries Phantom cloud transcription first (same
 * `/api/desktop/speech/transcribe` path as desktop), then falls back to Android
 * SpeechRecognizer. Non-premium and cooldown windows use native immediately.
 */
class PhoneSpeechSession(
    context: Context,
    private val cloud: CloudSpeech? = null,
    private val onPartial: (String) -> Unit,
    private val onFinal: (String) -> Unit,
    private val onError: (String) -> Unit,
    private val onNativeFallback: () -> Unit = {},
    private val onUtterance: (String) -> Unit = {}
) {
    class CloudSpeech(
        val isPreferred: () -> Boolean,
        val transcribe: suspend (ByteArray) -> String
    )

    private val appContext = context.applicationContext
    private val route = CloudSpeechRoute()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val mainHandler = Handler(Looper.getMainLooper())
    private val shouldListen = AtomicBoolean(false)
    private val recorderLock = Any()

    private var recognizer: SpeechRecognizer? = null
    private var recorder: AudioRecord? = null
    private var recordJob: Job? = null
    private var drainJob: Job? = null
    private var mode: SpeechCaptureMode = SpeechCaptureMode.Native
    private var cloudTranscript = ""
    private var flushingCloud = false
    private var lastNativePartial = ""

    var isListening: Boolean = false
        private set

    fun start() {
        cancelCapture(flushCloud = false)
        shouldListen.set(true)
        isListening = true
        cloudTranscript = ""
        flushingCloud = false
        lastNativePartial = ""
        val preferCloud = cloud?.isPreferred() == true
        when (route.startMode(preferCloud)) {
            SpeechCaptureMode.Cloud -> startCloud()
            SpeechCaptureMode.Native -> startNative()
        }
    }

    /**
     * @return true if cloud audio is still being transcribed — keep the listening UI.
     */
    fun stop(): Boolean {
        shouldListen.set(false)
        mainHandler.removeCallbacksAndMessages(null)
        synchronized(recorderLock) {
            try {
                recorder?.stop()
            } catch (_: Exception) {
            }
        }
        if (mode == SpeechCaptureMode.Cloud && (recordJob?.isActive == true || drainJob?.isActive == true)) {
            flushingCloud = true
            return true
        }
        try {
            recognizer?.stopListening()
        } catch (_: Exception) {
            stopNativeRecognizer()
        }
        flushingCloud = false
        return false
    }

    fun release() {
        shouldListen.set(false)
        flushingCloud = false
        mainHandler.removeCallbacksAndMessages(null)
        cancelCapture(flushCloud = false)
        scope.coroutineContext[Job]?.cancel()
    }

    private fun cancelCapture(flushCloud: Boolean) {
        if (!flushCloud) {
            recordJob?.cancel()
            drainJob?.cancel()
        }
        recordJob = null
        drainJob = null
        stopNativeRecognizer()
        synchronized(recorderLock) {
            try {
                recorder?.release()
            } catch (_: Exception) {
            }
            recorder = null
        }
    }

    private fun startCloud() {
        val transcriber = cloud ?: run {
            startNative()
            return
        }
        mode = SpeechCaptureMode.Cloud
        Log.i(TAG, "speech_route_changed route=cloud")
        val chunks = Channel<PcmItem>(Channel.BUFFERED)
        recordJob = scope.launch(Dispatchers.IO) {
            val capture = openCloudCapture()
            if (capture == null) {
                chunks.close()
                withContext(Dispatchers.Main) { failMicrophone() }
                return@launch
            }
            val rec = capture.recorder
            synchronized(recorderLock) { recorder = rec }
            var sentFinal = false
            var captureFailed = false
            try {
                rec.startRecording()
                if (rec.recordingState != AudioRecord.RECORDSTATE_RECORDING) {
                    throw IllegalStateException("AudioRecord did not enter recording state")
                }
                val frame = ByteArray(capture.frameBytes)
                val accum = ByteArrayOutputStream()
                while (isActive && shouldListen.get()) {
                    val n = rec.read(frame, 0, frame.size)
                    if (n > 0) {
                        val pcm16k = resampleTo16kPcm(frame.copyOf(n), capture.sampleRate)
                        if (pcm16k.isNotEmpty()) {
                            accum.write(pcm16k)
                            if (accum.size() >= SPEECH_CHUNK_BYTES) {
                                chunks.send(PcmItem(accum.toByteArray(), final = false))
                                accum.reset()
                            }
                        }
                    } else if (n < 0) {
                        break
                    }
                }
                chunks.send(PcmItem(accum.toByteArray(), final = true))
                sentFinal = true
            } catch (ex: CancellationException) {
                throw ex
            } catch (ex: Exception) {
                captureFailed = true
                Log.w(TAG, "cloud capture failed", ex)
            } finally {
                if (!sentFinal && !captureFailed) {
                    try {
                        chunks.send(PcmItem(ByteArray(0), final = true))
                    } catch (_: Exception) {
                    }
                }
                chunks.close()
                try {
                    rec.stop()
                } catch (_: Exception) {
                }
                rec.release()
                synchronized(recorderLock) {
                    if (recorder === rec) recorder = null
                }
            }
            if (captureFailed) {
                withContext(Dispatchers.Main) { failMicrophone() }
            }
        }
        drainJob = scope.launch(Dispatchers.IO) {
            try {
                for (item in chunks) {
                    val text = try {
                        if (item.data.isNotEmpty()) transcriber.transcribe(item.data) else ""
                    } catch (ex: CancellationException) {
                        throw ex
                    } catch (ex: Exception) {
                        Log.w(TAG, "cloud transcription failed", ex)
                        handleCloudFailure()
                        return@launch
                    }
                    if (text.isNotBlank()) {
                        cloudTranscript = if (cloudTranscript.isEmpty()) text else "$cloudTranscript $text"
                        withContext(Dispatchers.Main) { onPartial(cloudTranscript) }
                    }
                    if (item.final) {
                        if (cloudTranscript.isNotBlank()) route.onCloudSuccess()
                        withContext(Dispatchers.Main) {
                            flushingCloud = false
                            onFinal(cloudTranscript)
                            isListening = false
                        }
                    }
                }
            } catch (ex: CancellationException) {
                throw ex
            } catch (ex: Exception) {
                Log.w(TAG, "cloud drain failed", ex)
                handleCloudFailure()
            }
        }
    }

    private suspend fun handleCloudFailure() {
        route.onCloudFailure()
        val stillListening = shouldListen.get()
        shouldListen.set(false)
        synchronized(recorderLock) {
            try {
                recorder?.stop()
            } catch (_: Exception) {
            }
        }
        recordJob?.cancel()
        try {
            recordJob?.join()
        } catch (_: Exception) {
        }
        withContext(Dispatchers.Main) {
            flushingCloud = false
            if (stillListening) {
                Log.i(TAG, "speech_route_changed route=native_fallback reason=cloud_unavailable probe=this_listen")
                onNativeFallback()
                startNative()
            } else if (cloudTranscript.isNotBlank()) {
                onFinal(cloudTranscript)
                isListening = false
            } else {
                isListening = false
                onError("Cloud speech unavailable. Tap the mic to use on-device recognition.")
            }
        }
    }

    private fun openCloudCapture(): CloudCapture? {
        val sources = intArrayOf(
            MediaRecorder.AudioSource.VOICE_RECOGNITION,
            MediaRecorder.AudioSource.MIC,
            MediaRecorder.AudioSource.UNPROCESSED,
            MediaRecorder.AudioSource.DEFAULT
        )
        val rates = intArrayOf(SPEECH_SAMPLE_RATE, 44_100, 48_000, 8_000, 22_050)
        for (source in sources) {
            for (rate in rates) {
                val minBuf = AudioRecord.getMinBufferSize(
                    rate,
                    AudioFormat.CHANNEL_IN_MONO,
                    AudioFormat.ENCODING_PCM_16BIT
                )
                if (minBuf <= 0) continue
                val rec = try {
                    AudioRecord(
                        source,
                        rate,
                        AudioFormat.CHANNEL_IN_MONO,
                        AudioFormat.ENCODING_PCM_16BIT,
                        max(minBuf, minBuf * 2)
                    )
                } catch (_: SecurityException) {
                    return null
                } catch (_: Exception) {
                    continue
                }
                if (rec.state == AudioRecord.STATE_INITIALIZED) {
                    Log.i(TAG, "cloud capture source=$source rate=$rate")
                    return CloudCapture(rec, rate, max(minBuf, 2_048))
                }
                rec.release()
            }
        }
        return null
    }

    private fun failMicrophone() {
        if (mode == SpeechCaptureMode.Cloud && cloud != null) {
            route.onCloudFailure()
            onNativeFallback()
            startNative()
            return
        }
        shouldListen.set(false)
        flushingCloud = false
        isListening = false
        onError("Could not capture speech. Try again.")
    }

    private fun startNative() {
        mode = SpeechCaptureMode.Native
        shouldListen.set(true)
        mainHandler.removeCallbacksAndMessages(null)
        if (!SpeechRecognizer.isRecognitionAvailable(appContext)) {
            isListening = false
            shouldListen.set(false)
            onError("Speech recognition is not available on this device.")
            return
        }
        stopNativeRecognizer()
        val rec = SpeechRecognizer.createSpeechRecognizer(appContext)
        rec.setRecognitionListener(object : RecognitionListener {
            override fun onReadyForSpeech(params: Bundle?) {}
            override fun onBeginningOfSpeech() {}
            override fun onRmsChanged(rmsdB: Float) {}
            override fun onBufferReceived(buffer: ByteArray?) {}
            override fun onEndOfSpeech() {}
            override fun onError(error: Int) {
                if (shouldRestartNativeSpeech(error, shouldListen.get())) {
                    commitNativePartialIfNeeded()
                    scheduleNativeRestart(error)
                    return
                }
                isListening = false
                shouldListen.set(false)
                route.onNativeUtteranceFinished()
                val pending = lastNativePartial
                lastNativePartial = ""
                if (error != SpeechRecognizer.ERROR_CLIENT &&
                    error != SpeechRecognizer.ERROR_NO_MATCH &&
                    error != SpeechRecognizer.ERROR_SPEECH_TIMEOUT
                ) {
                    onError("Could not capture speech. Try again.")
                } else {
                    onFinal(pending)
                }
            }
            override fun onResults(results: Bundle?) {
                val text = results?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
                    ?.firstOrNull()
                    .orEmpty()
                    .ifBlank { lastNativePartial }
                lastNativePartial = ""
                if (shouldListen.get()) {
                    if (text.isNotBlank()) onUtterance(text)
                    scheduleNativeRestart(SpeechRecognizer.ERROR_NO_MATCH)
                    return
                }
                isListening = false
                route.onNativeUtteranceFinished()
                onFinal(text)
            }
            override fun onPartialResults(partialResults: Bundle?) {
                val text = partialResults?.getStringArrayList(SpeechRecognizer.RESULTS_RECOGNITION)
                    ?.firstOrNull()
                    .orEmpty()
                if (text.isNotBlank()) {
                    lastNativePartial = text
                    onPartial(text)
                }
            }
            override fun onEvent(eventType: Int, params: Bundle?) {}
        })
        recognizer = rec
        isListening = true
        rec.startListening(nativeRecognizerIntent())
    }

    private fun commitNativePartialIfNeeded() {
        val pending = lastNativePartial
        lastNativePartial = ""
        if (pending.isNotBlank()) onUtterance(pending)
    }

    private fun nativeRecognizerIntent(): Intent =
        Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            putExtra(RecognizerIntent.EXTRA_PARTIAL_RESULTS, true)
            putExtra(RecognizerIntent.EXTRA_MAX_RESULTS, 1)
            // Best-effort: Google's recognizer often ignores these, so we also restart.
            putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS, 8_000L)
            putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_POSSIBLY_COMPLETE_SILENCE_LENGTH_MILLIS, 8_000L)
            putExtra(RecognizerIntent.EXTRA_SPEECH_INPUT_MINIMUM_LENGTH_MILLIS, 60_000L)
        }

    private fun scheduleNativeRestart(error: Int) {
        if (!shouldListen.get()) return
        mainHandler.removeCallbacksAndMessages(null)
        mainHandler.postDelayed({
            if (!shouldListen.get()) return@postDelayed
            val rec = recognizer
            if (rec == null) {
                startNative()
                return@postDelayed
            }
            try {
                rec.startListening(nativeRecognizerIntent())
                isListening = true
            } catch (ex: Exception) {
                Log.w(TAG, "native restart failed", ex)
                startNative()
            }
        }, nativeSpeechRestartDelayMs(error))
    }

    private fun stopNativeRecognizer() {
        mainHandler.removeCallbacksAndMessages(null)
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

    private data class PcmItem(val data: ByteArray, val final: Boolean)
    private data class CloudCapture(
        val recorder: AudioRecord,
        val sampleRate: Int,
        val frameBytes: Int
    )

    private companion object {
        const val TAG = "PhantomSpeech"
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

data class PhoneDictationUpdate(
    val text: String,
    val prefix: String?,
    val listening: Boolean
)

fun nextPhoneDictation(
    currentText: String,
    prefix: String?,
    isListening: Boolean,
    hypothesis: String,
    isFinal: Boolean,
    keepListening: Boolean = false
): PhoneDictationUpdate {
    val trimmed = hypothesis.trim()
    if (trimmed.isEmpty()) {
        return if (isFinal && !keepListening) {
            PhoneDictationUpdate(currentText, prefix = null, listening = false)
        } else {
            PhoneDictationUpdate(currentText, prefix, listening = isListening || keepListening)
        }
    }
    if (!isFinal && !isListening) {
        return PhoneDictationUpdate(currentText, prefix, listening = false)
    }
    val lockedPrefix = prefix ?: currentText.trimEnd()
    val text = if (isAlreadyDictated(lockedPrefix, trimmed)) {
        lockedPrefix
    } else {
        mergePhoneDictation(lockedPrefix, trimmed)
    }
    return when {
        !isFinal -> PhoneDictationUpdate(text, lockedPrefix, listening = true)
        keepListening -> PhoneDictationUpdate(text, prefix = text.trimEnd(), listening = true)
        else -> PhoneDictationUpdate(text, prefix = null, listening = false)
    }
}

fun isAlreadyDictated(prefix: String, spoken: String): Boolean {
    val head = prefix.trimEnd()
    val tail = spoken.trim()
    if (tail.isEmpty()) return true
    return head == tail || head.endsWith(" $tail")
}
