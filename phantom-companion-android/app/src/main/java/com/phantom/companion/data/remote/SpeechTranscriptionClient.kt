package com.phantom.companion.data.remote

import com.phantom.companion.data.speech.MIN_TRANSCRIBE_WAV_BYTES
import com.phantom.companion.data.speech.buildWav
import com.phantom.companion.data.speech.containsSpeech
import com.phantom.companion.data.speech.speechLanguageTag
import com.phantom.companion.domain.model.ErrorBody
import com.phantom.companion.domain.model.SpeechTranscriptionResponse
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.util.Locale

class SpeechTranscriptionException(
    message: String,
    val statusCode: Int? = null
) : Exception(message)

class SpeechTranscriptionClient(
    private val http: OkHttpClient,
    baseUrl: String,
    private val json: Json
) {
    private val transcribeUrl = "${baseUrl.trimEnd('/')}/api/desktop/speech/transcribe"

    suspend fun transcribePcm16(
        pcm16: ByteArray,
        locale: Locale = Locale.getDefault()
    ): String = withContext(Dispatchers.IO) {
        if (!containsSpeech(pcm16)) return@withContext ""
        val wav = buildWav(pcm16)
        if (wav.size < MIN_TRANSCRIBE_WAV_BYTES) return@withContext ""
        val body = MultipartBody.Builder()
            .setType(MultipartBody.FORM)
            .addFormDataPart(
                "audio",
                "speech.wav",
                wav.toRequestBody("audio/wav".toMediaType())
            )
            .addFormDataPart("language", speechLanguageTag(locale))
            .build()
        val request = Request.Builder()
            .url(transcribeUrl)
            .post(body)
            .build()
        http.newCall(request).execute().use { response ->
            val payload = response.body?.string().orEmpty()
            if (!response.isSuccessful) {
                val parsed = runCatching { json.decodeFromString<ErrorBody>(payload) }.getOrNull()
                val detail = parsed?.error ?: parsed?.message ?: "Managed speech failed (${response.code})."
                throw SpeechTranscriptionException(detail, response.code)
            }
            json.decodeFromString<SpeechTranscriptionResponse>(payload).text.trim()
        }
    }
}
