package com.phantom.companion

import com.phantom.companion.data.local.SessionStore
import com.phantom.companion.domain.model.AuthSession
import com.phantom.companion.domain.model.DisplayInfo
import com.phantom.companion.domain.model.Pairing
import com.phantom.companion.domain.model.RelayBody
import com.phantom.companion.domain.model.RelayEnvelope
import com.phantom.companion.domain.model.StartupSnapshot
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test

class SerializationTest {

    private val json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        encodeDefaults = true
    }

    @Test
    fun testAuthSessionSerialization() {
        val session = AuthSession(
            userId = "usr_123456",
            email = "user@example.com",
            accessToken = "atk_abcdef1234567890",
            refreshToken = "rtk_0987654321fedcba",
            authMethod = "password",
            deviceInstallId = "inst_998877",
            deviceFingerprintHash = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
            authenticatedAtUtc = "2026-03-31T12:00:00Z",
            expiresAtUtc = "2026-04-01T12:00:00Z",
            isAuthenticated = true
        )

        val encoded = json.encodeToString(session)
        val decoded = json.decodeFromString<AuthSession>(encoded)

        assertEquals("usr_123456", decoded.userId)
        assertEquals("user@example.com", decoded.email)
        assertEquals("atk_abcdef1234567890", decoded.accessToken)
        assertTrue(decoded.isAuthenticated)
    }

    @Test
    fun testStartupSnapshotParsingWithOptionalHostedKb() {
        val rawJson = """
            {
              "userId": "usr_999",
              "email": "pilot@phantom.ai",
              "emailVerified": true,
              "accessTier": "pro",
              "phoneVerified": false,
              "wallet": {
                "proAvailableCredits": 150.50,
                "premiumAvailableCredits": 25.00,
                "premiumNegativeCredits": 0.0
              },
              "leaseExpiresAtUtc": null,
              "hasResumableLockedSession": false,
              "lastLockTokenHash": "",
              "lastLockedSessionId": "",
              "offlineModeEnabled": false,
              "canUseDesktopPowerFeatures": true,
              "lastValidatedAtUtc": "2026-03-31T12:00:00Z",
              "hostedKnowledgeBase": null,
              "source": "database"
            }
        """.trimIndent()

        val decoded = json.decodeFromString<StartupSnapshot>(rawJson)
        assertEquals("pilot@phantom.ai", decoded.email)
        assertTrue(decoded.emailVerified)
        assertEquals("pro", decoded.accessTier)
        assertEquals(150.50, decoded.wallet.proAvailableCredits, 0.001)
    }

    @Test
    fun testRelayEnvelopeEncodingDecoding() {
        // Phone → desktop capture.ask: payload lives under `body` (spec §5.1).
        val envelope = RelayEnvelope(
            v = 1,
            id = "env_001",
            type = "capture.ask",
            ts = "2026-03-31T12:00:00Z",
            pairingId = "pair_111",
            role = "phone",
            body = RelayBody(
                displayId = "0",
                prompt = "Analyze this screenshot please."
            )
        )

        val encoded = json.encodeToString(envelope)
        val decoded = json.decodeFromString<RelayEnvelope>(encoded)

        assertEquals("capture.ask", decoded.type)
        assertEquals("0", decoded.body?.displayId)
        assertEquals("Analyze this screenshot please.", decoded.body?.prompt)
    }

    @Test
    fun testDesktopHelloBodyParsing() {
        // Desktop → phone desktop.hello: status/model/vision/displays arrive under `body` (C2).
        val rawJson = """
            {
              "v": 1,
              "id": "env_002",
              "type": "desktop.hello",
              "ts": "2026-03-31T12:00:00Z",
              "pairingId": "pair_111",
              "role": "desktop",
              "body": {
                "status": "idle",
                "model": "groq/compound",
                "provider": "Groq",
                "vision": false,
                "displays": [
                  { "id": "1", "name": "Built-in Retina Display", "isDefault": false },
                  { "id": "2", "name": "", "isDefault": true }
                ],
                "lockExpiresAtUtc": ""
              }
            }
        """.trimIndent()

        val decoded = json.decodeFromString<RelayEnvelope>(rawJson)
        assertEquals("desktop.hello", decoded.type)
        assertEquals("idle", decoded.body?.status)
        assertEquals("groq/compound", decoded.body?.model)
        assertEquals(false, decoded.body?.vision)
        assertEquals(2, decoded.body?.displays?.size)
        assertEquals("2", decoded.body?.displays?.firstOrNull { it.isDefault }?.id)
    }

    @Test
    fun testIsoDateParsing() {
        val isoZ = "2026-03-31T12:00:00Z"
        val timestampZ = SessionStore.parseIsoDate(isoZ)
        assertNotNull(timestampZ)

        val isoFractional = "2026-03-31T12:00:00.1234567Z"
        val timestampFractional = SessionStore.parseIsoDate(isoFractional)
        assertNotNull(timestampFractional)
    }

    @Test
    fun testBackendMinimalRelayPingDecodes() {
        // The backend's own keepalive ping is {v,type,ts} with no id/pairingId/role/body
        // (CompanionRelayHost.RelayPingFrame). The phone MUST decode it so it can answer
        // relay.pong and reset the server's 45s receive timeout (C1). The envelope fields
        // are nullable/ defaulted so this minimal frame decodes without throwing.
        val rawJson = """{"v":1,"type":"relay.ping","ts":"2026-03-31T12:00:00Z"}"""
        val decoded = json.decodeFromString<RelayEnvelope>(rawJson)
        assertEquals("relay.ping", decoded.type)
        assertEquals(null, decoded.id)
        assertEquals(null, decoded.pairingId)
        assertEquals(null, decoded.body)
    }
}
