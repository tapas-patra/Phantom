[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$windowsAppDir = Join-Path $repoRoot "phantom-windows-app"
$hostedConfigPath = Join-Path $windowsAppDir "phantom.hosted.json"
$hostedConfig = Get-Content $hostedConfigPath -Raw | ConvertFrom-Json

$env:PHANTOM_HOSTED_MODE = $hostedConfig.mode
$env:PHANTOM_WINDOWS_BACKEND_BASE_URL = $hostedConfig.desktopBackendBaseUrl
$env:PHANTOM_WEBSITE_BASE_URL = $hostedConfig.websiteBaseUrl
$env:RAG_LOG = "true"

Push-Location $windowsAppDir
try {
    dotnet build .\SecureOverlay.sln -c Debug
    if ($LASTEXITCODE -ne 0) {
        throw "Desktop build failed with exit code $LASTEXITCODE."
    }

    & ".\bin\Debug\net8.0-windows\svchost-shell.exe"
}
finally {
    Pop-Location
}
