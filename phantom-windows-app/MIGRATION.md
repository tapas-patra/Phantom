# Windows App Migration

When extraction starts, move these root-level assets into `phantom-windows-app`:

- `SecureOverlay.sln`
- `SecureOverlay.csproj`
- `App.xaml(.cs)`
- `MainWindow.xaml(.cs)`
- `SettingsPage.xaml(.cs)`
- `StartupWindow.xaml(.cs)`
- `Helpers/`
- `Services/`
- `Application/`
- `Domain/`
- `Infrastructure/`
- `Platform/`
- remaining WPF support files and assets

Do not move:
- future website/dashboard code
- future backend service code

Precondition:
- working `dotnet` build and smoke validation available during extraction
