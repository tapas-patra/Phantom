package com.phantom.companion.data.speech

import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.Locale

const val SPEECH_SAMPLE_RATE = 16_000
const val SPEECH_CHANNELS: Short = 1

fun buildWav(
    pcm: ByteArray,
    sampleRate: Int = SPEECH_SAMPLE_RATE,
    channels: Short = SPEECH_CHANNELS
): ByteArray {
    val buffer = ByteBuffer.allocate(44 + pcm.size).order(ByteOrder.LITTLE_ENDIAN)
    buffer.put("RIFF".toByteArray(Charsets.US_ASCII))
    buffer.putInt(36 + pcm.size)
    buffer.put("WAVE".toByteArray(Charsets.US_ASCII))
    buffer.put("fmt ".toByteArray(Charsets.US_ASCII))
    buffer.putInt(16)
    buffer.putShort(1)
    buffer.putShort(channels)
    buffer.putInt(sampleRate)
    buffer.putInt(sampleRate * channels * 2)
    buffer.putShort((channels * 2).toShort())
    buffer.putShort(16)
    buffer.put("data".toByteArray(Charsets.US_ASCII))
    buffer.putInt(pcm.size)
    buffer.put(pcm)
    return buffer.array()
}

fun containsSpeech(pcm: ByteArray): Boolean {
    if (pcm.size < 2) return false
    var total = 0L
    var count = 0
    var index = 0
    while (index + 1 < pcm.size) {
        val sample = (pcm[index].toInt() and 0xff) or ((pcm[index + 1].toInt() and 0xff) shl 8)
        val signed = if (sample >= 0x8000) sample - 0x10000 else sample
        total += kotlin.math.abs(signed)
        count++
        index += 16
    }
    return count > 0 && total / count >= 120
}

fun speechLanguageTag(locale: Locale = Locale.getDefault()): String {
    val raw = locale.toLanguageTag().take(12)
    val filtered = raw.filter { it in 'A'..'Z' || it in 'a'..'z' || it == '-' }
    return filtered.ifBlank { locale.language.filter { it in 'a'..'z' || it in 'A'..'Z' }.ifBlank { "en" } }
}

fun prefersManagedCloudSpeech(accessTier: String?): Boolean =
    accessTier.equals("premium", ignoreCase = true)
