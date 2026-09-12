# Build desktop artifacts locally

Use this when you want to test a desktop build after a code change without waiting for CI.

CI publishes `Phantom-macOS.zip` and `Phantom-Windows-x64.zip` straight to the `desktop-latest` GitHub Release in `tapas-patra/phantom-release-repo`. It does not use `actions/upload-artifact`, so it does not consume Actions artifact storage.

The scripts produce the same zip names as [`.github/workflows/desktop-release.yml`](.github/workflows/desktop-release.yml). Output is gitignored under `release/local/`.

Windows WPF cannot be published on macOS. Build each platform on that OS.

## Prerequisites

### macOS

- Full **Xcode** (not only Command Line Tools). SwiftUI macros fail with Command Line Tools alone.
- Switch the active developer directory if needed:

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
xcode-select -p
```

- `swift` and `ditto` on `PATH`

### Windows

- .NET 8 SDK (`dotnet --list-sdks` should include `8.0.x`)
- PowerShell 5.1 or newer

## 1. Pull the latest code

```bash
git pull
```

## 2. Build on the machine you will test

### macOS

From the repo root:

```bash
chmod +x build-desktop-artifacts.sh
./build-desktop-artifacts.sh
```

That builds the Mac app, zips it, and runs `--self-check`.

Skip the self-check if you only need the zip:

```bash
./build-desktop-artifacts.sh --mac --no-self-check
```

### Windows

From the repo root in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1
```

Mac-only or Windows-only on a machine that can do both:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1 -Mac
powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1 -Windows
```

`--all` / `-All` builds every platform this OS can produce. The other platform is skipped with a message.

## 3. Find the artifacts

| File | What it is |
|---|---|
| `release/local/Phantom-macOS.zip` | Same zip CI would upload as `macos-release` |
| `release/local/macos/Phantom.app` | Unpacked Mac app for local testing |
| `release/local/Phantom-Windows-x64.zip` | Same zip CI would upload as `windows-release` |
| `release/local/windows/svchost-shell.exe` | Unpacked Windows exe for local testing |
| `release/local/MANIFEST.txt` | Git SHA, time, and which platforms were built |

## 4. Test the build

### macOS

```bash
open release/local/macos/Phantom.app
```

If Gatekeeper blocks a local ad-hoc signed build:

```bash
xattr -cr release/local/macos/Phantom.app
open release/local/macos/Phantom.app
```

Or unzip `Phantom-macOS.zip` and open the `Phantom.app` inside.

### Windows

Run:

```text
release\local\windows\svchost-shell.exe
```

Or unzip `Phantom-Windows-x64.zip` and run `svchost-shell.exe` from that folder.

These are unsigned Release builds against the hosted backend URLs already in the app (`phantom.hosted.json` / embedded defaults). They are for local verification, not App Store / Store submission.

## 5. Repeat after code changes

Rebuild on the same machine:

```bash
./build-desktop-artifacts.sh
```

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1
```

The script wipes `release/local/` first unless you pass `--no-clean` / `-NoClean`.

## Useful flags

| Shell | PowerShell | Effect |
|---|---|---|
| `--mac` | `-Mac` | Build macOS only |
| `--windows` | `-Windows` | Build Windows only |
| `--all` | `-All` | Try both; skip the OS this machine cannot build |
| `--no-self-check` | `-NoSelfCheck` | Skip Mac `--self-check` |
| `--no-clean` | `-NoClean` | Keep previous files in `release/local/` |
| `--help` | | Print usage |

## If the Mac build fails on SwiftUI macros

Install Xcode from the App Store, then:

```bash
sudo xcode-select -s /Applications/Xcode.app/Contents/Developer
sudo xcodebuild -license accept
./build-desktop-artifacts.sh
```

Command Line Tools (`xcode-select -p` showing `/Library/Developer/CommandLineTools`) is not enough for this app.
