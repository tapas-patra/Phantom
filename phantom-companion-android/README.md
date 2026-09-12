# Phantom Companion for Android

Phantom Companion is the companion application for the Phantom AI Windows client, allowing users to pair mobile devices with their desktop workstation via QR codes or pairing codes, initiate low-latency screen captures and vision analysis, manage live interactive chat streaming sessions with thought indicators, and view wallet credits and account status.

---

## 1. Package Name & Identity

- **Application ID:** `com.phantom.companion`
- **Namespace:** `com.phantom.companion`
- **Target SDK:** 36 (Android 16)
- **Min SDK:** 26 (Android 8.0)
- **Language & Framework:** Kotlin 2.0+ with Jetpack Compose & Material Design 3 (Dark Night Theme)
- **Network Architecture:** Retrofit 2 + Kotlinx Serialization + OkHttp 4 + OkHttp WebSocket
- **Security:** Android Keystore & `EncryptedSharedPreferences` (Tink AES-256-GCM / AES-256-SIV)
- **Screen Capture / QR:** CameraX + ZXing (QR scan analyzer)

---

## 2. Live Endpoints Verified

The application communicates with the backend hosted at:
`https://phantom-ai-windows-app-backend.onrender.com`

Live verification tests were performed against the running backend with the following responses recorded:

| Endpoint | Method | Live HTTP Status | Live Verified Response / Behavior |
| :--- | :--- | :--- | :--- |
| `/health` | `GET` | **200 OK** | `{"status":"live","service":"phantom-windows-app-backend","utc":"2026-09-10T23:07:11.6398433Z"}` |
| `/api/desktop/auth/login` (missing fields) | `POST` | **400 Bad Request** | `{"error":"Email is required."}` (Inline error validation verified) |
| `/api/desktop/auth/login` (missing pass) | `POST` | **400 Bad Request** | `{"error":"Invalid email or password."}` (Banner credential validation verified) |
| `/api/desktop/auth/forgot-password` | `POST` | **200 OK** | `{"message":"If that account exists, a password reset link has been sent."}` |
| `/api/companion/pairings` (capability probe) | `GET` | **404 Not Found** | **Probe verified**: Backend companion routes not deployed yet; app sets `companionApiReady = false`, shows *"Desktop companion service is not enabled on this backend yet."*, disables Capture & Ask, and prevents crashes without mocking |

---

## 3. How to Assemble a Release

### Release APK
To build an optimized, signed release APK:
```bash
gradle :app:assembleRelease
```
The output APK will be located at:
`app/build/outputs/apk/release/app-release.apk`

### Release Android App Bundle (AAB for Google Play)
To build a production bundle:
```bash
gradle :app:bundleRelease
```
The output AAB will be located at:
`app/build/outputs/bundle/release/app-release.aab`

### Signing Configuration
Set the following environment variables prior to running the release task:
- `KEYSTORE_PATH`: Path to your upload keystore file (`.jks`).
- `STORE_PASSWORD`: Keystore password.
- `KEY_PASSWORD`: Key alias password.

If unset, the build script defaults to `./my-upload-key.jks` with key alias `upload`.

### Debug APK
To assemble a debug APK for development:
```bash
gradle :app:assembleDebug
```
Output: `app/build/outputs/apk/debug/app-debug.apk`

---

## 4. Architecture & Security Implementation

1. **Device Identity (`DeviceIdentityStore.kt`)**:
   - Generates persistent UUIDv4 `installId`.
   - Generates Keystore/EncryptedSharedPreferences-backed 32-byte `deviceSecret`.
   - Computes `deviceFingerprintHash = sha256_hex(installId + "|" + deviceLabel + "|" + base64(deviceSecret))` (lowercase 64-character SHA-256).
   - Computes `secretFingerprintHint = first 12 characters of sha256_hex(base64(deviceSecret))`.
   - Refreshes always match login credentials to prevent `Refresh token device mismatch`.

2. **Session Storage (`SessionStore.kt`)**:
   - `EncryptedSharedPreferences` ensures tokens and refresh tokens are encrypted at rest with hardware-backed master keys.
   - Proactive token refresh triggers at 80% remaining TTL and automatically on 401 via `TokenAuthenticator`.

3. **Backend Communication & Guardrails**:
   - Custom `AuthInterceptor` attaches `X-Phantom-Correlation-Id` and `X-Phantom-Operation-Id` per request.
   - Strictly enforces safety rule: **never sends `Origin` or `X-Phantom-CSRF`** headers.
   - Handles Render cold start timeouts with probe-and-retry ("Waking Phantom…").

4. **Pairing & Deep Link Flow**:
   - Supports camera QR code scan via ZXing (`QrCodeAnalyzer.kt`) and manual 6-character Base32 entry (`[2-9A-HJ-NP-Z]`, excluding `0, O, 1, I`).
   - Deep link schema: `phantom-companion://pair?code=...&relay=...` with host validation.

5. **Streaming Emulator Display & Offline Backend Resilience**:
   - The initial route is determined synchronously from local encrypted storage, eliminating full-screen blocking spinners on cold start.
   - Removed `FLAG_SECURE` in debug builds which caused browser streaming emulators to display a solid black canvas (due to Android OS screenshot/stream protection).
   - If the backend is cold-starting, unreachable, or returning 404 for companion endpoints, the app displays informative, non-blocking UI states (such as *"Connecting to Phantom backend…"*, cold start countdowns, or capability notices) without crashing or mock fallbacks. As soon as endpoints come online, the app seamlessly authenticates, pairs, and opens live WebSocket relays.
