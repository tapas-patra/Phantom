package com.phantom.companion

import com.phantom.companion.data.speech.CloudSpeechRoute
import com.phantom.companion.data.speech.MIN_TRANSCRIBE_WAV_BYTES
import com.phantom.companion.data.speech.SPEECH_SAMPLE_RATE
import com.phantom.companion.data.speech.SpeechCaptureMode
import com.phantom.companion.data.speech.buildWav
import com.phantom.companion.data.speech.containsSpeech
import com.phantom.companion.data.speech.prefersManagedCloudSpeech
import com.phantom.companion.data.speech.resampleTo16kPcm
import com.phantom.companion.data.speech.speechLanguageTag
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.util.Locale

class CloudSpeechRouteTest {
    @Test
    fun nonPremiumUsesNativeAndResets() {
        val route = CloudSpeechRoute(nowMs = { 0L })
        assertEquals(SpeechCaptureMode.Native, route.startMode(preferCloud = false))
    }

    @Test
    fun premiumStartsOnCloud() {
        val route = CloudSpeechRoute(nowMs = { 0L })
        assertEquals(SpeechCaptureMode.Cloud, route.startMode(preferCloud = true))
    }

    @Test
    fun firstCloudFailureRetriesCloudOnNextStart() {
        val route = CloudSpeechRoute(nowMs = { 0L })
        route.startMode(preferCloud = true)
        route.onCloudFailure()
        assertEquals(SpeechCaptureMode.Cloud, route.startMode(preferCloud = true))
    }

    @Test
    fun secondCloudFailureStaysNativeUntilCooldownEnds() {
        var now = 1_000L
        val route = CloudSpeechRoute(nowMs = { now })
        route.startMode(preferCloud = true)
        route.onCloudFailure()
        route.startMode(preferCloud = true)
        route.onCloudFailure()
        assertEquals(SpeechCaptureMode.Native, route.startMode(preferCloud = true))

        now += 5 * 60_000L
        assertEquals(SpeechCaptureMode.Native, route.startMode(preferCloud = true))
        route.onNativeUtteranceFinished()
        assertEquals(SpeechCaptureMode.Cloud, route.startMode(preferCloud = true))
    }

    @Test
    fun cloudSuccessClearsFallback() {
        val route = CloudSpeechRoute(nowMs = { 0L })
        route.onCloudFailure()
        route.onCloudSuccess()
        assertEquals(SpeechCaptureMode.Cloud, route.startMode(preferCloud = true))
    }
}

class SpeechPcmTest {
    @Test
    fun wavHeaderIsRiffWavePcm() {
        val pcm = byteArrayOf(1, 0, 2, 0)
        val wav = buildWav(pcm, sampleRate = 16_000, channels = 1)
        assertEquals(48, wav.size)
        assertEquals("RIFF", wav.decodeToString(0, 4))
        assertEquals("WAVE", wav.decodeToString(8, 12))
        assertEquals("fmt ", wav.decodeToString(12, 16))
        assertEquals("data", wav.decodeToString(36, 40))
        val le = ByteBuffer.wrap(wav).order(ByteOrder.LITTLE_ENDIAN)
        assertEquals(36 + pcm.size, le.getInt(4))
        assertEquals(16, le.getInt(16))
        assertEquals(1.toShort(), le.getShort(20))
        assertEquals(1.toShort(), le.getShort(22))
        assertEquals(16_000, le.getInt(24))
        assertEquals(32_000, le.getInt(28))
        assertEquals(2.toShort(), le.getShort(32))
        assertEquals(16.toShort(), le.getShort(34))
        assertEquals(pcm.size, le.getInt(40))
        assertArrayEquals(pcm, wav.copyOfRange(44, wav.size))
    }

    @Test
    fun silentPcmIsNotSpeech() {
        assertFalse(containsSpeech(ByteArray(64)))
    }

    @Test
    fun loudPcmCountsAsSpeech() {
        val pcm = ByteArray(64)
        for (i in pcm.indices step 2) {
            pcm[i] = 0
            pcm[i + 1] = 0x40
        }
        assertTrue(containsSpeech(pcm))
    }

    @Test
    fun languageTagIsIso6391() {
        assertEquals("en", speechLanguageTag(Locale.US))
        assertEquals("en", speechLanguageTag(Locale.UK))
        assertEquals("hi", speechLanguageTag(Locale.forLanguageTag("hi-IN")))
        assertTrue(prefersManagedCloudSpeech("premium"))
        assertTrue(prefersManagedCloudSpeech("Premium"))
        assertFalse(prefersManagedCloudSpeech("pro"))
        assertFalse(prefersManagedCloudSpeech("free"))
    }

    @Test
    fun resampleIsNoOpAt16k() {
        val pcm = byteArrayOf(1, 2, 3, 4)
        assertArrayEquals(pcm, resampleTo16kPcm(pcm, SPEECH_SAMPLE_RATE))
    }

    @Test
    fun resampleHalvesSampleCountFrom32k() {
        val pcm = ByteArray(16)
        for (i in pcm.indices step 2) {
            pcm[i] = i.toByte()
            pcm[i + 1] = 0x10
        }
        val out = resampleTo16kPcm(pcm, 32_000)
        assertEquals(8, out.size)
        assertEquals(pcm[0], out[0])
        assertEquals(pcm[1], out[1])
    }

    @Test
    fun wavBelowBackendMinimumIsStillWellFormed() {
        val wav = buildWav(byteArrayOf(1, 0))
        assertTrue(wav.size < MIN_TRANSCRIBE_WAV_BYTES)
        assertEquals("RIFF", wav.decodeToString(0, 4))
    }
}
