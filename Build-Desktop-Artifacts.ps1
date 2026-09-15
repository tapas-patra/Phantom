# Build the same desktop zip names as .github/workflows/desktop-release.yml,
# locally, without GitHub artifact storage.
#
#   powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1
#   powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1 -Mac
#   powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1 -Windows
#   powershell -ExecutionPolicy Bypass -File .\Build-Desktop-Artifacts.ps1 -All
#
# Output lives in release/local\ (gitignored):
#   Phantom-macOS.zip
#   Phantom-Windows-x64.zip
#   macos\Phantom.app
#   windows\svchost-shell.exe
#
# Keep publish flags in sync with .github/workflows/desktop-release.yml.

[CmdletBinding()]
param(
    [switch]$Mac,
    [switch]$Windows,
    [switch]$All,
    [switch]$NoSelfCheck,
    [switch]$NoClean
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $root "release\local"
$productVersion = if ($env:PHANTOM_PRODUCT_VERSION) { $env:PHANTOM_PRODUCT_VERSION } else { "0.0.0-local" }
$env:PHANTOM_PRODUCT_VERSION = $productVersion
$onMac = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::OSX)
$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

if ($All) {
    $Mac = $true
    $Windows = $true
}
elseif (-not $Mac -and -not $Windows) {
    if ($onMac) { $Mac = $true }
    elseif ($onWindows) { $Windows = $true }
    else { throw "This OS cannot build Phantom desktop apps. Use macOS or Windows." }
}

$gitSha = "unknown"
try { $gitSha = (git -C $root rev-parse --short HEAD).Trim() } catch { }
$builtAt = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
$built = New-Object System.Collections.Generic.List[string]
$skipped = New-Object System.Collections.Generic.List[string]

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Missing required command: $Name"
    }
}

if (-not (Test-Path $out)) {
    New-Item -ItemType Directory -Path $out | Out-Null
}
if (-not $NoClean) {
    foreach ($path in @(
        (Join-Path $out "macos"),
        (Join-Path $out "windows"),
        (Join-Path $out "Phantom-macOS.zip"),
        (Join-Path $out "Phantom-Windows-x64.zip"),
        (Join-Path $out "MANIFEST.txt")
    )) {
        if (Test-Path $path) { Remove-Item -Recurse -Force $path }
    }
}
New-Item -ItemType Directory -Path $out -Force | Out-Null

if ($Mac) {
    if (-not $onMac) {
        Write-Host "Skipping macOS artifact: this host is not macOS."
        Write-Host "On a Mac run: ./build-desktop-artifacts.sh --mac"
        $skipped.Add("macos") | Out-Null
    }
    else {
        Require-Command swift
        Require-Command ditto
        $builder = Join-Path $root "phantom-mac-app/Scripts/build-app.sh"
        Write-Host "==> Building macOS app (same path as CI)"
        & $builder
        if ($LASTEXITCODE -ne 0) { throw "macOS build-app.sh failed with exit code $LASTEXITCODE." }

        $app = Join-Path $root "phantom-mac-app/dist/Phantom.app"
        if (-not (Test-Path $app)) { throw "macOS build did not produce $app" }

        $macOut = Join-Path $out "macos"
        New-Item -ItemType Directory -Path $macOut -Force | Out-Null
        $copiedApp = Join-Path $macOut "Phantom.app"
        if (Test-Path $copiedApp) { Remove-Item -Recurse -Force $copiedApp }
        & ditto $app $copiedApp
        & xattr -cr $copiedApp 2>$null

        Write-Host "==> Packaging Phantom-macOS.zip"
        $zip = Join-Path $out "Phantom-macOS.zip"
        if (Test-Path $zip) { Remove-Item -Force $zip }
        & ditto -c -k --sequesterRsrc --keepParent $app $zip

        if (-not $NoSelfCheck) {
            Write-Host "==> Running --self-check"
            $exe = Join-Path $copiedApp "Contents/MacOS/Phantom"
            & $exe --self-check
            if ($LASTEXITCODE -ne 0) { throw "macOS --self-check failed with exit code $LASTEXITCODE." }
        }

        $built.Add("macos") | Out-Null
        Write-Host "macOS artifact: $zip"
        Write-Host "Test with: open $copiedApp"
    }
}

if ($Windows) {
    if (-not $onWindows) {
        Write-Host "Skipping Windows artifact: WPF / net8.0-windows must be published on Windows."
        $skipped.Add("windows") | Out-Null
    }
    else {
        Require-Command dotnet
        Write-Host "==> Publishing Windows app (same flags as CI)"
        $winOut = Join-Path $out "windows"
        New-Item -ItemType Directory -Path $winOut -Force | Out-Null
        $project = Join-Path $root "phantom-windows-app/SecureOverlay.csproj"
        & dotnet publish $project `
            -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:EnableCompressionInSingleFile=true `
            -p:PhantomProductVersion=$productVersion `
            -p:IncludeSourceRevisionInInformationalVersion=false `
            -o $winOut
        if ($LASTEXITCODE -ne 0) { throw "Windows publish failed with exit code $LASTEXITCODE." }

        Write-Host "==> Packaging Phantom-Windows-x64.zip"
        $zip = Join-Path $out "Phantom-Windows-x64.zip"
        if (Test-Path $zip) { Remove-Item -Force $zip }
        Compress-Archive -Path (Join-Path $winOut "*") -DestinationPath $zip -Force

        $built.Add("windows") | Out-Null
        Write-Host "Windows artifact: $zip"
        Write-Host "Test with: $(Join-Path $winOut 'svchost-shell.exe')"
    }
}

$manifest = Join-Path $out "MANIFEST.txt"
@(
    "Phantom local desktop artifacts"
    "git=$gitSha"
    "built_at_utc=$builtAt"
    "version=$productVersion"
    "host=$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)"
    "built=$(if ($built.Count) { $built -join ',' } else { 'none' })"
    "skipped=$(if ($skipped.Count) { $skipped -join ',' } else { 'none' })"
) | Set-Content -Path $manifest -Encoding UTF8

Write-Host ""
Write-Host "Done. See $manifest"
if ($built.Count -eq 0) {
    throw "No artifacts were built."
}
if ($Windows -and -not $onWindows) {
    Write-Host "Windows zip was not produced on this machine. Run this script on Windows."
}
