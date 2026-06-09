# Phantom Agent Guide

## Purpose
- This repo is a single-project .NET 8 WPF desktop app.
- The app is an overlay-style AI assistant with:
  - multi-provider LLM chat
  - streaming responses
  - screenshot attachment for vision-capable models
  - browser/WebView2-based voice input
  - persisted settings, resume, and job-description context
  - window/cursor/screen-capture protection behavior

## Repo Shape
- `SecureOverlay.sln`: one solution, one project.
- `SecureOverlay.csproj`: project config, NuGet deps, output identity.
- `App.xaml` / `App.xaml.cs`: startup, admin + Windows version checks, global exception handling.
- `MainWindow.xaml` / `MainWindow.xaml.cs`: primary UI and most app behavior.
- `SettingsPage.xaml` / `SettingsPage.xaml.cs`: settings UI.
- `Services/`: provider clients, settings persistence, conversation state, model/key rotation, voice input.
- `Helpers/AIModelRegistry.cs`: single source of truth for providers and model catalogs.
- `Helpers/`: light helper layer; most important file is `AIModelRegistry.cs`.
- `bin/`, `obj/`: generated build output. Ignore unless debugging build artifacts.

## Read This First
- For overall behavior: `MainWindow.xaml.cs`
- For startup/runtime constraints: `App.xaml.cs`
- For provider/model changes: `Helpers/AIModelRegistry.cs`, `Services/AIServiceFactory.cs`, `Services/APIRotationManager.cs`
- For persisted settings/state: `Services/SettingsManager.cs`
- For provider API behavior: `Services/IAIService.cs` and the relevant file in `Services/*Service.cs`
- For settings UI work: `SettingsPage.xaml.cs` and `SettingsPage.xaml`

## Task Routing
- Chat flow, streaming, screenshot attach, cached conversation restore:
  - read `MainWindow.xaml.cs`
- Adding or removing providers/models:
  - edit `Helpers/AIModelRegistry.cs`
  - then check `Services/AIServiceFactory.cs`
  - then check `SettingsManager.cs` and `SettingsPage.xaml.cs`
- API key/model rotation bugs:
  - read `Services/APIRotationManager.cs`
- Prompt/context-window/resume/JD logic:
  - read `Services/ConversationManager.cs`
- Settings file or cache issues:
  - read `Services/SettingsManager.cs`
- Voice/mic/WebView2 issues:
  - read `Services/VoiceInputService.cs`
  - sometimes also `MicrophonePermissionWindow.xaml.cs`
- Screen-capture/window behavior:
  - read `WindowProtection.cs`, `CursorManager.cs`, `TaskViewMonitor.cs`, `ProtectedMessageBox.cs`

## Important Runtime Facts
- Target framework: `net8.0-windows`
- UI stack: WPF
- Additional runtime dependency: WebView2
- Main compiled output name is `svchost-shell` because `AssemblyName` is overridden in `SecureOverlay.csproj`
- App requires:
  - Windows
  - administrator privileges
  - Windows build `19041+`
- This means local verification from non-Windows environments is limited to static inspection only.

## State On Disk
- Runtime database: `%AppData%/Windows Host Service 271/phantom.db`
- Crash/session log: `%AppData%/Windows Host Service 271/crash_log.txt`
- WebView2 user data: `%AppData%/Windows Host Service 271/WebView2Cache`
- Temporary speech HTML: `%AppData%/Windows Host Service 271/Temp/speech_recognition.html`
- Safe mode marker: `%AppData%/Windows Host Service 271/safe_mode.txt`
- Legacy JSON state may still exist under `%AppData%/SecureOverlay/` only for one-way migration

## Architectural Invariants
- `Helpers/AIModelRegistry.cs` is the canonical model registry.
- `SettingsManager.SyncModelListsWithRegistry()` rewrites per-provider model lists from the registry.
- If adding a provider, update all of:
  - `Helpers/AIModelRegistry.cs`
  - `Services/AIServiceFactory.cs`
  - `Services/AppSettings` in `SettingsManager.cs`
  - `SettingsPage.xaml.cs`
  - any provider-selection UI paths in `MainWindow.xaml.cs`
- Conversation cache is intentionally stored separately from `settings.json`.
- Normal close clears conversation cache; restart flow preserves/saves it.
- Screenshot attachment is only valid for vision-capable models; visibility is driven by registry support checks.
- Voice input depends on hidden `WebView2` hosted inside `MainWindow`.

## Build / Run
- Build debug:
  - `dotnet build SecureOverlay.sln -c Debug`
- Build release:
  - `dotnet build SecureOverlay.sln -c Release`
- Expected release output:
  - `bin/Release/net8.0-windows/`

## Low-Signal Areas To Skip
- `bin/`
- `obj/`
- generated `.baml`, `.g.cs`, `.cache`, `.dll`, `.pdb`, `.runtimeconfig.json`

## Practical Notes For Future LLMs
- Do not scan the entire repo by default; it is effectively a single-app codebase with one very large controller file.
- Start with the smallest relevant surface:
  - settings issue -> `Services/SettingsManager.cs`
  - provider/model issue -> `Helpers/AIModelRegistry.cs`
  - UI wiring issue -> `MainWindow.xaml.cs` or `SettingsPage.xaml.cs`
- Search inside `MainWindow.xaml.cs` by feature keyword instead of reading all ~3k lines.
- There is no meaningful test suite in the repo right now.
- There is no README in the repo as of this guide.
