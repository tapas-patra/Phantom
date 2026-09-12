package com.phantom.companion

import com.phantom.companion.data.local.mergePhoneDictation
import com.phantom.companion.data.local.nextPhoneDictation
import com.phantom.companion.data.speech.NativeSpeechErrors
import com.phantom.companion.data.speech.shouldRestartNativeSpeech
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PhoneDictationTest {
    @Test
    fun cumulativePartialsReplaceTheLiveTail() {
        val prefix = ""
        val afterFirst = mergePhoneDictation(prefix, "hello")
        val afterSecond = mergePhoneDictation(prefix, "hello world")
        val afterFinal = mergePhoneDictation(prefix, "hello world")
        assertEquals("hello", afterFirst)
        assertEquals("hello world", afterSecond)
        assertEquals("hello world", afterFinal)
    }

    @Test
    fun existingComposerTextStaysAsPrefix() {
        assertEquals(
            "Ask this: what is the runtime",
            mergePhoneDictation("Ask this:", "what is the runtime")
        )
    }

    @Test
    fun blankHypothesisKeepsPrefix() {
        assertEquals("Keep me", mergePhoneDictation("Keep me", "  "))
    }

    @Test
    fun pauseCommitsUtteranceButKeepsListening() {
        val afterPartial = nextPhoneDictation(
            currentText = "",
            prefix = "",
            isListening = true,
            hypothesis = "hello world",
            isFinal = false
        )
        val afterPause = nextPhoneDictation(
            currentText = afterPartial.text,
            prefix = afterPartial.prefix,
            isListening = afterPartial.listening,
            hypothesis = "hello world",
            isFinal = true,
            keepListening = true
        )
        assertEquals("hello world", afterPause.text)
        assertEquals("hello world", afterPause.prefix)
        assertTrue(afterPause.listening)

        val nextUtterance = nextPhoneDictation(
            currentText = afterPause.text,
            prefix = afterPause.prefix,
            isListening = afterPause.listening,
            hypothesis = "and then this",
            isFinal = false
        )
        assertEquals("hello world and then this", nextUtterance.text)
        assertTrue(nextUtterance.listening)
    }

    @Test
    fun explicitStopEndsListening() {
        val stopped = nextPhoneDictation(
            currentText = "hello world",
            prefix = "hello world",
            isListening = true,
            hypothesis = "",
            isFinal = true,
            keepListening = false
        )
        assertEquals("hello world", stopped.text)
        assertEquals(null, stopped.prefix)
        assertFalse(stopped.listening)
    }

    @Test
    fun stopAfterCommittedUtteranceDoesNotDuplicate() {
        val stopped = nextPhoneDictation(
            currentText = "Ask this: hello",
            prefix = "Ask this: hello",
            isListening = true,
            hypothesis = "hello",
            isFinal = true,
            keepListening = false
        )
        assertEquals("Ask this: hello", stopped.text)
        assertFalse(stopped.listening)
    }

    @Test
    fun silenceErrorsRestartWhileMicIsHeld() {
        assertTrue(shouldRestartNativeSpeech(NativeSpeechErrors.NO_MATCH, true))
        assertTrue(shouldRestartNativeSpeech(NativeSpeechErrors.SPEECH_TIMEOUT, true))
        assertFalse(shouldRestartNativeSpeech(NativeSpeechErrors.NO_MATCH, false))
        assertFalse(shouldRestartNativeSpeech(4, true))
    }
}
