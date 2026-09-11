package com.phantom.companion.data.local

import android.content.Context
import android.os.Build
import android.util.Base64
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import com.phantom.companion.BuildConfig
import java.security.MessageDigest
import java.security.SecureRandom
import java.util.UUID

data class DeviceIdentity(
    val installId: String,
    val deviceLabel: String,
    val deviceFingerprintHash: String,
    val secretFingerprintHint: String,
    val appVersion: String
)

class DeviceIdentityStore(private val context: Context) {

    private val prefs by lazy {
        try {
            val masterKey = MasterKey.Builder(context)
                .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
                .build()
            EncryptedSharedPreferences.create(
                context,
                PREFS_NAME,
                masterKey,
                EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
                EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM
            )
        } catch (e: Throwable) {
            // Fallback for test / legacy container environment
            context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
        }
    }

    @Synchronized
    fun getIdentity(): DeviceIdentity {
        var installId = prefs.getString(KEY_INSTALL_ID, null)
        var secretBase64 = prefs.getString(KEY_DEVICE_SECRET, null)

        if (installId.isNullOrEmpty() || secretBase64.isNullOrEmpty()) {
            installId = UUID.randomUUID().toString()
            val secretBytes = ByteArray(32)
            SecureRandom().nextBytes(secretBytes)
            secretBase64 = Base64.encodeToString(secretBytes, Base64.NO_WRAP)

            prefs.edit()
                .putString(KEY_INSTALL_ID, installId)
                .putString(KEY_DEVICE_SECRET, secretBase64)
                .apply()
        }

        val deviceLabel = "Android ${Build.MODEL.trim()}"
        val fingerprintSource = "$installId|$deviceLabel|$secretBase64"
        val deviceFingerprintHash = sha256Hex(fingerprintSource)
        val secretHash = sha256Hex(secretBase64)
        val secretFingerprintHint = secretHash.take(12)
        val appVersion = BuildConfig.VERSION_NAME

        return DeviceIdentity(
            installId = installId,
            deviceLabel = deviceLabel,
            deviceFingerprintHash = deviceFingerprintHash,
            secretFingerprintHint = secretFingerprintHint,
            appVersion = appVersion
        )
    }

    companion object {
        private const val PREFS_NAME = "phantom_device_identity"
        private const val KEY_INSTALL_ID = "install_id"
        private const val KEY_DEVICE_SECRET = "device_secret"

        fun sha256Hex(input: String): String {
            val bytes = MessageDigest.getInstance("SHA-256").digest(input.toByteArray(Charsets.UTF_8))
            return bytes.joinToString("") { "%02x".format(it) }
        }
    }
}
