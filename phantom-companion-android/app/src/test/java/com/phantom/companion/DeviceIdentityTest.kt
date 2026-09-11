package com.phantom.companion

import com.phantom.companion.data.local.DeviceIdentityStore
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class DeviceIdentityTest {

    @Test
    fun testSha256HexProducesCorrectLowercase64Chars() {
        val input = "test-string-input"
        val hash = DeviceIdentityStore.sha256Hex(input)
        assertEquals(64, hash.length)
        assertEquals(hash.lowercase(), hash)
        // Ensure only hexadecimal characters
        assertTrue(hash.matches(Regex("^[0-9a-f]{64}$")))
    }

    @Test
    fun testSecretFingerprintHintLength() {
        val dummySecretBase64 = "4/7/W6U4z5j4N7W6U4z5j4N7W6U4z5j4N7W6U4z5j4M="
        val fullHash = DeviceIdentityStore.sha256Hex(dummySecretBase64)
        val hint = fullHash.take(12)
        assertEquals(12, hint.length)
        assertTrue(hint.matches(Regex("^[0-9a-f]{12}$")))
    }

    @Test
    fun testPairingCodeSanitization() {
        val rawInput = " ab-7k2q "
        val sanitized = rawInput.trim().uppercase().replace(Regex("[^2-9A-HJ-NP-Z]"), "")
        assertEquals("AB7K2Q", sanitized)
        assertEquals(6, sanitized.length)

        // Invalid characters 0, O, I, 1 should be stripped
        val withForbiddenChars = "0O1IAB7K"
        val stripped = withForbiddenChars.uppercase().replace(Regex("[^2-9A-HJ-NP-Z]"), "")
        assertEquals("AB7K", stripped)
    }
}
