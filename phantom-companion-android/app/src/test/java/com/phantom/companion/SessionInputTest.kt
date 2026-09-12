package com.phantom.companion

import com.phantom.companion.domain.model.DEFAULT_SCREEN_PROMPT
import com.phantom.companion.domain.model.DesktopPresenceState
import com.phantom.companion.domain.model.mapDesktopPresence
import com.phantom.companion.domain.model.resolveFollowUpText
import com.phantom.companion.domain.model.shouldApplyRemoteComposer
import com.phantom.companion.domain.model.shouldPublishComposer
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SessionInputTest {
    @Test
    fun typedTextWinsOverDefaultPrompt() {
        assertEquals("What is this?", resolveFollowUpText("What is this?", 0))
        assertEquals("What is this?", resolveFollowUpText("What is this?", 2))
    }

    @Test
    fun emptyTextUsesDefaultWhenScreenshotsAreAttached() {
        assertEquals(DEFAULT_SCREEN_PROMPT, resolveFollowUpText("  ", 1))
        assertNull(resolveFollowUpText("", 0))
    }

    @Test
    fun staleThinkingDoesNotStickAfterChatFinishes() {
        assertEquals(
            DesktopPresenceState.READY,
            mapDesktopPresence("thinking", allowCapturing = false, allowThinking = false)
        )
        assertEquals(
            DesktopPresenceState.THINKING,
            mapDesktopPresence("thinking", allowCapturing = false, allowThinking = true)
        )
    }

    @Test
    fun composerEditsPublishOnlyWhenTheyChange() {
        assertTrue(shouldPublishComposer("hello there", "hello"))
        assertFalse(shouldPublishComposer("hello", "hello"))
        assertTrue(shouldPublishComposer("", "hello"))
    }

    @Test
    fun remoteComposerAppliesEditsAndSendClear() {
        assertTrue(shouldApplyRemoteComposer("hello", "hello there", sent = false))
        assertFalse(shouldApplyRemoteComposer("hello", "hello", sent = false))
        assertTrue(shouldApplyRemoteComposer("hello", "", sent = true))
        assertFalse(shouldApplyRemoteComposer("", "", sent = true))
    }

    @Test
    fun staleCapturingDoesNotStickAfterCaptureFinishes() {
        assertEquals(
            DesktopPresenceState.READY,
            mapDesktopPresence("capturing", allowCapturing = false, allowThinking = false)
        )
    }
}
