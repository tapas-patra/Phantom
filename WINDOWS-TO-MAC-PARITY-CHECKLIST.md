# Phantom Windows → macOS parity checklist

Audit date: 2026-08-11  
Source of truth: `phantom-windows-app/`  
Compared target: `phantom-mac-app/`

This is a static source audit. A checked item would mean parity is verified; every item below is currently unchecked because the Windows behavior is either missing or only partially present on macOS.

> Implementation update (2026-08-11): the checklist below is preserved as the pre-implementation baseline. The current implementation status and deliberately remaining gaps are recorded after **Verification notes**.

Legend:

- `[ ]` missing
- `[~]` partially implemented or behaviorally different
- `Platform decision` means the Windows mechanism should not be copied literally, but the macOS product still needs an explicit equivalent or acceptance decision

## P0 — release-blocking behavior gaps

This is a priority summary; the same areas are decomposed into implementation-sized checks below and should not be double-counted.

- [ ] **Offline startup authority.** Windows persists the account snapshot and can open from a valid cached lease when the backend is unavailable. macOS persists no `StartupSnapshot`, does not decode lease fields, and cannot make an offline launch decision. Windows evidence: `Infrastructure/Hosted/LocalStartupGateService.cs`, `Infrastructure/Persistence/SqliteAccountCacheRepository.cs`. macOS evidence: `PhantomStore.bootstrap()`, `StartupSnapshot` in `BackendClient.swift`.
- [ ] **Restricted-shell startup.** Windows can open the app while blocking new interviews but allowing the already locked interview to resume. macOS has no `AppLaunchContext`, `CanStartInterview`, or `CanResumeLockedInterview` equivalent; after email verification it enters the chat shell and defers only a subset of checks until send time.
- [ ] **Hosted resumable-lock recovery at launch.** macOS omits `HasResumableLockedSession`, `LastLockedSessionId`, and `LastLockTokenHash` from the startup DTO and never reconnects a hosted resumable session to its local runtime state.
- [ ] **Lease-expiry enforcement.** macOS omits `LeaseExpiresAtUtc`, `OfflineModeEnabled`, `LastValidatedAtUtc`, and startup-check `Source`, so it cannot reproduce Windows rules for expired leases, offline mode, and new-interview blocking.
- [ ] **Free-trial 15-minute boundary enforcement.** Windows stops at the first 15-minute demo block unless the user explicitly enables the second block. macOS exposes one generic extension toggle but has no live boundary timer or automatic boundary finalization.
- [ ] **Paid-credit/debt boundary enforcement while the app remains open.** Windows finalizes when available paid credits are consumed, or when the protected debt cap reaches 1 credit. macOS checks balances only when creating a session and finalizes only on clear, new topic, logout, or quit.
- [ ] **Authentication-failure cleanup during an active interview.** Windows clears the invalid auth session, abandons metering, releases the lock, and returns the user to re-authentication. After refresh failure, macOS leaves the saved session in Keychain, pauses the local interview, and leaves cleanup for a later user action.
- [ ] **NVIDIA is not selectable on macOS.** `BYOClient` contains an NVIDIA endpoint, but `BYOCatalog.providers` omits NVIDIA, so no NVIDIA key or model can be selected. This contradicts the macOS README claim that NVIDIA direct BYO chat is implemented.

## Startup, authentication, and account gates

- [ ] Reproduce the Windows startup state machine: auth choice, login form, checking-account state, ready state, blocked state, backend-unavailable state, retry, back, sign-out, and explicit “Open App”. macOS has only login/chat/settings screens and an inline status string.
- [ ] Cache the Windows startup-authority fields actually used by launch logic: lease, resumable lock, offline permission, validation timestamp, and source. macOS decodes only email verification, tier, wallet, power flag, and Knowledge Base.
- [ ] Proactively refresh sessions based on `ExpiresAtUtc`. macOS omits `AuthenticatedAtUtc` and `ExpiresAtUtc` from `AuthSession` and refreshes only after a 401.
- [ ] Validate `AuthSession.isAuthenticated` before restoring or entering the app. macOS decodes it but does not gate `bootstrap()` or `enterApp()` on it.
- [ ] Pass device metadata on the registration URL. Windows adds source, app version, install ID, device label, fingerprint, and secret hint; macOS opens only `/register`.
- [ ] Derive the app version from the built application. macOS hard-codes `0.4.0` in login and lock acquisition; Windows reads assembly version metadata.
- [~] Make the device fingerprint secret-backed. Windows includes a per-install secret and machine signals in the fingerprint. macOS stores a Keychain secret but its fingerprint hashes only install ID and host label, so the secret does not protect device identity.
- [ ] Add hosted-configuration validation and a visible fatal startup error for missing/invalid backend or website URLs. macOS silently falls back to production URLs.
- [ ] Add global startup/UI-thread/unhandled exception handling, local crash diagnostics, and hosted crash telemetry. macOS has no equivalent to Windows `App.OnUnhandledException` and `OnDispatcherUnhandledException`.
- [ ] Add storage-bootstrap failure handling. Windows enters an explicit read-only safe mode with a marker and reason; macOS persistence uses `try?` in several paths and can silently drop state.

## Interview metering, credits, and locks

- [ ] Add the live interview timer and status (`Live`/`Paused`, elapsed time, completed minutes, projected credits). macOS shows no session timer.
- [ ] Add the live account credit indicator and active credit-lane indicator. macOS displays only the startup snapshot in Settings and a BYO/MANAGED pill.
- [ ] Add configurable inactivity auto-pause with a minimum of 10 minutes, 30-second checks, an in-chat notice, and telemetry. macOS pauses only after request failure.
- [ ] Split extension consent into Windows-equivalent settings: free-trial second-block consent and paid post-exhaustion consent. macOS has only `allowPaidSessionExtension`.
- [ ] Require extension opt-in before the next request once a boundary is reached. macOS never sets or checks a boundary opt-in-required state.
- [ ] Recalculate the active runtime lane as credits are consumed and switch between Premium managed and Pro BYO at the same points as Windows. macOS chooses a lane from the current snapshot but has no once-per-second/boundary synchronization.
- [ ] Show and enforce projected Premium extension debt before it exceeds the protected continuation cap. The finalizer caps debt, but macOS has no proactive stop.
- [ ] Refresh account chrome/status after every activation, pause, resume, boundary finalization, auth failure, and Settings change.
- [ ] Persist hosted lock recovery metadata alongside the account cache. macOS stores only the local `InterviewSession` and wallet JSON.
- [ ] Preserve Windows behavior when hosted lock transport fails across a relaunch. The in-process five-minute fallback exists on macOS, but there is no startup account/lease integration for the next launch.
- [ ] Add lock and billing telemetry parity: activation, pause reason, inactivity pause, resume, boundary finalization, final charge, reconciliation state, lock active, heartbeat failure, and sampled heartbeat success.
- [ ] Add usage queue observability: pending/failed/dead-letter counts and recent dead-letter details. The macOS queue is durable and retries three times, but it has no diagnostic surface or logs.

## AI providers, models, keys, and retry behavior

- [ ] Add NVIDIA to the BYO provider registry, Settings UI, entitlement counting, Keychain management, model selection, and context configuration.
- [ ] Fetch BYO model catalogs directly from OpenAI, Anthropic, Gemini, Mistral, Groq, and NVIDIA when a key exists. Windows refreshes stale catalogs every 12 hours; macOS uses only a hard-coded seed list.
- [ ] Cache BYO catalog refresh timestamps and retain the last successful catalog when a refresh fails.
- [ ] Refresh the managed backend catalog at startup and after Settings closes, then reinitialize the managed runtime if its provider/model changed. macOS loads the managed catalog only while entering the app.
- [ ] Filter non-chat model IDs and infer vision support for newly discovered models. macOS cannot expose models outside its seed list.
- [ ] Preserve a selected model per provider. macOS stores one global `chat.model`, so switching providers can discard the previous provider-specific choice.
- [ ] Automatically choose the first configured BYO provider when the current provider has no key. macOS leaves an unconfigured seed provider selected and blocks the request until the user switches manually.
- [ ] Remove or disable retained BYO keys when an account becomes Premium-only, matching Windows entitlement cleanup. macOS hides the fields but leaves old provider keys in Keychain.
- [ ] Correct Groq context budgets. macOS falls through to a generic 32k/2k/10 configuration, while Windows has distinct budgets for Llama 3.3, Llama 3.1, Llama 4 Scout, Compound Mini, and Qwen3.
- [ ] Use dynamic/managed catalog metadata when computing model context and vision capability, with the Windows registry as fallback.
- [ ] Persist temporary 429 key state and the last-429 timestamp. macOS keeps rate-limited key indexes only in memory; Windows preserves rotation state.
- [ ] Add the visible API-key position indicator (`Key n/total`) and provider/model switch notification/flash.
- [ ] Retry errors that occur after an HTTP 200 stream has opened. macOS returns `AsyncBytes` before consuming the stream, so SSE errors and mid-stream failures bypass `byoStreamWithRotation()`.
- [ ] Add Windows-equivalent retry/model fallback for managed-backend streams. macOS retries only a 401 refresh; 429, 5xx, timeout, empty-stream, and mid-stream failures do not run through the five-attempt rotation policy.
- [~] Complete BYO → managed-extension fallback. It exists for failures thrown while opening the BYO request, but not for errors raised while reading the stream.
- [ ] Add the BYO debug error simulator and all Windows modes: 429, timeout, random, alternating keys, and first-two-fail.
- [ ] Add the debug-mode warning banner and request counter/status.

## Prompting, conversation memory, and response handling

- [ ] Summarize resumes asynchronously into the Windows structured career-summary format and cache the result.
- [ ] Summarize job descriptions asynchronously to a concise requirements/responsibility summary and cache the result.
- [ ] Invalidate cached resume/JD summaries only when their source text changes.
- [ ] Warm resume, JD, and live Knowledge Base context before the first request instead of sending raw text until later.
- [ ] Persist resume/JD summaries in context packs and restore them with conversation state.
- [~] Match the complete Windows prompt guardrails. macOS omits the exact Mermaid formatting rules, the 2–5 line explanation guidance, and the instruction not to claim a diagram cannot be drawn when Mermaid can express it.
- [~] Match model-aware conversation budgeting for every Windows model. The ten recent full messages and older-summary window exist, but the wrong Groq/default budgets change what is sent.
- [ ] Track `HasCode` on assistant responses and restore it with cached conversations.
- [ ] Store timestamps and estimated-token metadata per message, matching the Windows conversation record.
- [ ] Add explicit conversation export/import used by controlled restart, including restoration of embedded resume/JD context from the system prompt.
- [~] Match normal-close cache semantics. Windows clears conversation cache on normal close and preserves it only for in-app restart; macOS persists every conversation across ordinary quit/reopen.
- [ ] Add in-app restart that saves the conversation, relaunches the main shell, bypasses only the intended gate, and restores history.
- [ ] Cancel and visibly replace an in-flight request when a new request starts. macOS disables send while streaming and has no user-facing cancel/replacement flow.
- [ ] Remove or replace the pending empty assistant bubble when a stream is cancelled before producing content.
- [~] Match keyboard composition behavior: Enter sends and Shift+Enter inserts a newline. macOS uses Command+Return to send.

## Knowledge Base, RAG, and context packs

- [ ] Add local Knowledge Base fallback over resume/JD chunks when hosted KB is unavailable, not ready, times out, or returns no hits. macOS has no local chunk/search implementation.
- [ ] Decode and retain `DocumentId`, `Score`, and KB metadata from hosted search results. macOS keeps only document title and text.
- [ ] Decode `SourceDocumentIds` and `UpdatedAtUtc` on profile, experience, and project cards.
- [ ] Scope deep project retrieval to the selected project’s source documents.
- [ ] Support “same/that document” follow-ups by restricting retrieval to the previously returned document IDs.
- [ ] Preserve the previous useful snippets when a follow-up retrieval unexpectedly returns no results.
- [ ] Add the Windows deterministic route set and traceable plan: Direct, Profile, Experience, Project, and Retrieve with Global/PreviousDocuments/ActiveProject scope.
- [~] Complete profile routing. macOS has keyword grounding, but not Windows pack variants for intro, strengths, role, and general profile, nor current-experience fallback.
- [~] Complete experience selection. macOS matches company/role/skill and current role, but lacks Windows token scoring, relevance-over-current precedence, and oldest-to-newest experience timeline grounding.
- [~] Complete project selection. macOS supports named/recent/alternate/active project basics, but lacks Windows weighted title/slug/content scoring and does not refuse an unknown named project as strictly.
- [ ] Add project grounding variants: overview, architecture, challenges, stack, and impact.
- [ ] Retrieve project snippets only when the structured card is missing the requested field or the question asks for implementation-level detail.
- [ ] Pre-warm and cache profile/project interview packs by Knowledge Base revision.
- [ ] Invalidate warm packs when the KB revision changes.
- [ ] Add grounded fallback behavior for missing profile, missing experience, generic/fresher project questions, and unknown named projects.
- [ ] Return the Windows retrieval-miss messages instead of silently asking the model with no evidence.
- [ ] Prevent unrelated global resume/profile content from leaking into a project-scoped prompt.
- [ ] Add RAG trace logging for route, scope, target, KB freshness, retrieval timing, chosen documents, fallbacks, and cache hits.
- [~] Complete hosted context-pack lifecycle. macOS can list/save/delete account packs, but omits `UpdatedAtUtc`, edit/read-only state, unsaved-change detection, and the Windows apply/reset decision.
- [ ] Reset conversation grounding when the applied local/hosted context pack content changes, so old-context messages do not remain in the new interview.
- [ ] Add repository-backed local context-pack state, local draft normalization, legacy migration, cached summaries, and generated resume/JD documents/chunks.

## Chat rendering and interaction

- [ ] Render Mermaid diagrams in the chat surface. macOS displays Markdown as `AttributedString` text and has no Mermaid runtime.
- [ ] Add Mermaid syntax correction/retry for malformed diagrams before rendering.
- [ ] Add the Windows optimized-context/token diagnostic view.
- [~] Match the token counter. macOS displays a rough total over stored messages; Windows reports the optimized context and changes warning color near limits.
- [ ] Add the protected debug panel with live logs, copy, clear, auto-scroll, and context inspection.
- [ ] Add per-request correlation/phase tracing for first desktop chunk, retries, completion size, and latency.
- [ ] Add resume and job-description word counts and summary readiness/status in Settings.
- [~] Add Windows-equivalent Settings commit semantics. Most macOS bindings persist immediately and “Back” cannot cancel a set of edits; Windows applies changes only on Save and supports Cancel.
- [ ] Add the launch-restriction banner inside the chat shell.
- [ ] Add the quick header opacity control. macOS opacity is available only in Settings.
- [ ] Clear an attached screenshot immediately when the user changes to a non-vision model, and revalidate vision support again at send time. macOS only disables new capture, so an already attached image can still be sent after a model switch.

## Window, shortcuts, protection, and desktop integration

- [ ] Add global shortcuts for Settings and Quit, equivalent to Windows Ctrl+Alt+= / F14 paths. macOS implements only hide/show.
- [ ] Add an alternate global hide/show binding equivalent to Windows F13, or explicitly decide that one shortcut is sufficient.
- [ ] Add a protected legacy-app handoff for accounts with `CanUseDesktopPowerFeatures`, including path validation and process launch.
- [~] Protect transient provider/model picker surfaces. Windows explicitly protects combo-box popups and custom menus; macOS sets `sharingType = .none` on its main panel, capture panel, and quit alert, but has no code that applies it to SwiftUI picker/menu windows.
- [~] Verify Mission Control and app-switcher behavior. macOS uses accessory activation and an `NSPanel`, but has no explicit, tested equivalent of Windows Task View suppression for every Phantom surface.
- [~] Verify capture exclusion per supported macOS/conferencing-app version. The AppKit request exists, but Apple does not guarantee `NSWindow.sharingType = .none` on current macOS; this is an unresolved product guarantee, not verified Windows parity.

### Platform decision — do not port literally

- Windows administrator privilege and build-19041 checks are Windows prerequisites, not macOS features.
- Windows fake-cursor/two-cursor implementation is OS-specific. Decide whether macOS needs a native cursor-concealment/fake-cursor product equivalent; none exists today.
- Windows Win+Tab hooks, `SetWindowDisplayAffinity`, DWM attributes, tool-window flags, and WebView2 voice hosting must not be copied literally. The required outcomes are addressed separately above.
- macOS ScreenCaptureKit/CoreGraphics capture, Speech.framework, Keychain, all-Spaces behavior, click-through, and native minimize are valid platform equivalents or macOS additions and are not gaps by themselves.

## Verified parity already present (not implementation work)

The following Windows capabilities have a concrete macOS counterpart and should not be reimplemented just to mirror class names:

- password login, saved session in Keychain, refresh-on-401, and hosted logout
- managed catalog load at login and hosted streamed chat
- direct BYO request formats for ChatGPT, Claude, Gemini, Mistral, and Groq
- two keys per provider, three configured-provider limit, invalid-key persistence, and five-attempt request-open rotation
- interview prompt presets and first-person live-answer style
- ten recent full messages plus older summaries in a sliding window
- screenshot area selection, Return for full display, Escape to cancel, PNG resize to 1600px, preview/replace/remove, and vision gating
- native microphone and Speech Recognition permissions, transcription, manual stop, optional auto-send, and privacy-settings links
- clear-all, new-topic-with-resume, regenerate-last-response, and streaming chat
- managed/BYO entitlement selection, session records, minute-based charging, proportional source allocation, spillover, debt cap in finalization, durable reconciliation, and durable telemetry queue
- hosted lock acquire, 60-second heartbeat, release, and an in-process five-minute local fallback on transport failure
- Premium hosted context-pack list/save/delete and basic Knowledge Base status/search
- topmost panel, all-Spaces/full-screen auxiliary behavior, compact bar, opacity, click-through, native resize/minimize, hide/show shortcut, and quit confirmation

## Verification notes

- Windows runtime behavior was inspected statically; this host cannot execute the WPF client.
- The updated macOS package builds successfully with `swift build --disable-sandbox --package-path phantom-mac-app`; the executable `--self-check` also passes. SwiftPM emits only user-cache permission warnings in this managed workspace.
- The macOS app directory is currently untracked by Git, so this audit describes the working tree exactly as it existed on the audit date rather than a committed macOS revision.

## Implementation status after the parity pass

### Implemented

- [x] Cached startup snapshots, lease/offline evaluation, restricted-shell start/resume authority, resumable-session matching, proactive token refresh, invalid-session cleanup, registration device metadata, dynamic app version, secret-backed device fingerprint, URL validation, crash marker/next-launch telemetry, and read-only runtime safe mode.
- [x] Live elapsed/minute/credit status, free-trial 15/30-minute boundaries, paid-credit and protected debt boundaries, separate extension consent, next-request opt-in, inactivity auto-pause, live lane switching, durable lock recovery, final-charge telemetry, and queue/dead-letter diagnostics.
- [x] NVIDIA BYO support, direct 12-hour model-catalog refresh/cache for all six providers, model filtering/vision inference, per-provider model selection, configured-provider fallback, Premium-only key cleanup, Groq budgets, persisted 429 state, visible key position, mid-stream BYO/managed retry, managed model/provider fallback, BYO-to-managed extension fallback, and all debug simulator modes.
- [x] Source-keyed resume/JD summary caching and warmup, message timestamps/token/code metadata, controlled restart restore, normal-close cache clearing, request cancel/replace cleanup, and Enter/Shift+Enter composition behavior.
- [x] Hosted snippet document IDs/scores, source-document metadata on structured cards, active-project and previous-document scoping, prior-snippet preservation, local resume/JD retrieval fallback, deterministic profile/experience/project routing, project variants, retrieval-miss guardrails, project-scope leak prevention, and RAG route telemetry.
- [x] Context-change grounding reset, hosted pack updated timestamps/unsaved state/reset, complete Mermaid prompt guardrails, Mermaid rendering with syntax normalization/fallback, live diagnostics/copy/clear, request correlation/first-chunk/completion timing, context word counts/readiness, Settings Save/Cancel, restriction/debug banners, quick opacity, and screenshot revalidation.
- [x] Settings/Quit/F13 shortcuts, protected legacy `.app` handoff, and protection/all-Spaces policy applied when transient Phantom windows become key.

### Still partial or platform-dependent

- [~] The startup authority states and retry path are present, but macOS still uses one native login surface and automatically opens an allowed shell instead of reproducing every Windows startup page and explicit **Open App** transition pixel-for-pixel.
- [~] The compact deterministic RAG router covers route/scope safety and revision-keyed warm-pack invalidation, but does not reproduce every Windows heuristic weight, profile-pack wording variant, or its full local document repository implementation.
- [~] Mermaid performs one native normalization pass and shows a safe source fallback on render failure. Rendering uses Mermaid from a CDN, so offline diagram rendering would require bundling the Mermaid runtime.
- [~] Diagnostics expose logs, RAG trace, queue counts, dead letters, copy, and clear. The panel does not add a separate auto-scroll toggle because its bounded text view is already small and user-scrollable.
- [~] Objective capture exclusion and Mission Control/app-switcher guarantees remain OS/application-version verification items; AppKit protection is requested on the main panel, alerts, capture panel, open panel, and key transient windows, but macOS does not provide the same guarantee as Windows display affinity.
- [~] A full native UI inspection was attempted after build, but the Computer Use bridge timed out because Phantom runs as an accessory-policy app. Compile and executable logic checks passed; visual/capture acceptance still needs a manual run on the target macOS versions and conferencing apps.
