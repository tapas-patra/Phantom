# Windows App Extraction Manifest

This file records what was moved into `phantom-windows-app` when the repository root was reorganized into four top-level product folders.

## Move As-Is

- `SecureOverlay.sln`
- `SecureOverlay.csproj`
- `App.xaml`
- `App.xaml.cs`
- `MainWindow.xaml`
- `MainWindow.xaml.cs`
- `SettingsPage.xaml`
- `SettingsPage.xaml.cs`
- `StartupWindow.xaml`
- `StartupWindow.xaml.cs`
- `AssemblyInfo.cs`
- `app.manifest`
- `svchost.ico`

## Move Directories

- `Application/`
- `Domain/`
- `Infrastructure/`
- `Platform/`
- `Helpers/`
- `Services/`

## Move Supporting Runtime Files

- `ComboBoxProtection.cs`
- `CursorManager.cs`
- `DebugLogger.cs`
- `DiagnosticTool.cs`
- `FakeCursorWindow.xaml`
- `FakeCursorWindow.xaml.cs`
- `FallbackProtection.cs`
- `FileLogger.cs`
- `InvisibleMessageBox.xaml`
- `InvisibleMessageBox.xaml.cs`
- `Log.cs`
- `MarkdownHelper.cs`
- `MicrophonePermissionWindow.xaml`
- `MicrophonePermissionWindow.xaml.cs`
- `NativeMethods.cs`
- `ProtectedMessageBox.cs`
- `ScreenshotCapture.xaml`
- `ScreenshotCapture.xaml.cs`
- `TaskViewMonitor.cs`
- `WindowProtection.cs`

## Do Not Move

- future website code
- future backend service code
- future dashboard backend code

## Follow-Up

- repo-split handoff docs now live under `docs/repo-split/`

## Preconditions

1. `dotnet build` works in the target repo
2. WebView2 runtime dependency is documented in the target repo
3. startup, login gate, context pack load, and interview flow smoke checks are available
