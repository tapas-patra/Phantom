# Phantom for macOS

Local and Render log locations, correlation guidance, and privacy rules are documented in [`../shared/observability/README.md`](../shared/observability/README.md).

Native Swift/AppKit and SwiftUI client for the existing Phantom hosted backend.

## Implemented

- password login and saved-session restore using macOS Keychain
- hosted startup account validation
- managed provider/model catalog
- streamed hosted AI chat and direct BYO chat for ChatGPT, Claude, Gemini, Mistral, Groq, and NVIDIA
- Windows-equivalent login, chat overlay, and settings screens
- conversation persistence, regenerate, new-topic, and clear-context controls
- interview type, resume, job description, hosted context packs, and knowledge-base retrieval
- full-display screenshot attachment for vision-capable models
- protected click-and-drag area capture, full-display capture with Return, and in-app preview/replace/remove
- microphone speech transcription with optional auto-send
- explicit microphone/Speech Recognition permission request and System Settings recovery controls
- status-bar-level window across Spaces and full-screen applications until hidden with the shortcut
- compact bar mode with screenshot, microphone, settings, and expand controls, separate from native minimize
- optional click-through and adjustable opacity
- global **Command + Control + `** hide/show shortcut
- `NSWindow.sharingType = .none` on every Phantom surface
- settings selectors expand inside the protected Phantom panel instead of creating briefly visible menu windows
- no Dock or menu-bar status icon
- red close button asks for confirmation and then terminates the process
- protected in-window clear/new-topic confirmations and no hover tooltips
- Free, Pro BYO, Premium, mixed-credit runtime-lane restrictions and the Windows BYO model registry
- Premium-ready Knowledge Base refresh and retrieval before chat generation
- Windows-compatible session billing (completed-minute charging, 15-minute blocks, lane spillover, and protected debt cap)
- hosted device-lock acquire/60-second heartbeat/release lifecycle
- durable three-attempt usage reconciliation and 500-event telemetry queues
- three-provider/two-key Keychain limits with error-aware key/model rotation
- the Windows interview prompt, ten-message full-context policy, model token budgets, and Knowledge Base routing
- the shared Phantom brand asset packaged from the monorepo source

## Capture limitation

`sharingType = .none` is requested directly on the native `NSPanel`. It may omit the window in some older or legacy capture paths, but it is not a universal protection guarantee. Apple's current documentation calls this a legacy value and says not to use it to hide content from capture. On macOS 15 and newer, always assume the overlay can be captured until the exact conferencing app/version has passed a real preview test.

Hardware capture, cameras, and capture software that ignores the legacy flag will see the window.

## Build and open

```bash
cd "/Users/tapaskumarpatra/TKP-Other-personal/Phantom/phantom-mac-app"
./Scripts/build-app.sh
./dist/Phantom.app/Contents/MacOS/Phantom --self-check
open ./dist/Phantom.app
```

The app uses `phantom.hosted.json`. Environment variables override it:

- `PHANTOM_WINDOWS_BACKEND_BASE_URL`
- `PHANTOM_WEBSITE_BASE_URL`

## Test flow

1. Sign in with an existing verified Phantom account.
2. Confirm the provider and model catalog loads.
3. Send a chat message and confirm the response streams.
4. Test area screenshot attachment and its preview/replace/remove controls.
5. In Settings, request both Microphone and Speech Recognition permissions; if previously denied, use the privacy-pane buttons and reopen Phantom.
6. Collapse Phantom to its bar and confirm the compact microphone button works.
7. Open Settings and test opacity, click-through, tier restrictions, Knowledge Base status, context, and BYO Keychain storage.
8. Press **Command + Control + `** to hide and show Phantom.
9. Press the red close button, cancel once, then confirm that Quit terminates Phantom.
10. Start a Zoom, Teams, Meet, OBS, and QuickTime recording one at a time.
11. Share the full display and inspect the participant/recording output, including confirmations and screenshot selection.

Do not ship an “invisible” guarantee until every supported macOS and capture-app version passes that matrix.

## Platform-specific exclusions

Windows fake-cursor, task-view suppression, WebView2 voice hosting, and Windows debug simulation tools are not copied because they are OS-specific implementation details. AppKit window/Spaces handling, Speech.framework, ScreenCaptureKit, Keychain, and protected SwiftUI surfaces provide their macOS counterparts.

“Audio capture” currently means microphone speech-to-text, matching the interview-input flow. It does not capture system/output audio.
