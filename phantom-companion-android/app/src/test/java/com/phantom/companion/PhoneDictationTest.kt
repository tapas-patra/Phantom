package com.phantom.companion

import com.phantom.companion.data.local.mergePhoneDictation
import org.junit.Assert.assertEquals
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
}
