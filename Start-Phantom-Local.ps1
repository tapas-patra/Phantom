[CmdletBinding()]
param(
    [switch]$SeedUsers
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$envFile = Join-Path $repoRoot "local-dev.env.ps1"

if (Test-Path $envFile) {
    . $envFile
}

function Require-EnvVar {
    param(
        [string]$Name
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing required environment variable '$Name'. Set it in local-dev.env.ps1 or in your shell."
    }

    return $value
}

function Require-Command {
    param(
        [string]$Name
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Open-PowerShellWindow {
    param(
        [string]$Title,
        [string]$WorkingDirectory,
        [string]$Command
    )

    $escapedWorkingDirectory = $WorkingDirectory.Replace("'", "''")
    $fullCommand = @"
`$Host.UI.RawUI.WindowTitle = '$Title'
Set-Location '$escapedWorkingDirectory'
$Command
"@

    Start-Process powershell.exe -ArgumentList @(
        "-NoExit",
        "-ExecutionPolicy", "Bypass",
        "-Command", $fullCommand
    ) | Out-Null
}

Require-Command "dotnet"
Require-Command "npm"

$windowsBackendDbUrl = Require-EnvVar "PHANTOM_WINDOWS_BACKEND_DATABASE_URL"
$windowsBackendAdminApiKey = Require-EnvVar "PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY"
$dashboardBackendDbUrl = [Environment]::GetEnvironmentVariable("PHANTOM_DASHBOARD_BACKEND_DATABASE_URL")
if ([string]::IsNullOrWhiteSpace($dashboardBackendDbUrl)) {
    $dashboardBackendDbUrl = $windowsBackendDbUrl
}

$dashboardAdminApiKey = Require-EnvVar "PHANTOM_DASHBOARD_ADMIN_API_KEY"
$websiteBaseUrl = Require-EnvVar "PHANTOM_WEBSITE_BASE_URL"
$windowsBackendBaseUrl = Require-EnvVar "PHANTOM_WINDOWS_BACKEND_BASE_URL"
$dashboardApiBaseUrl = Require-EnvVar "VITE_PHANTOM_DASHBOARD_API_BASE_URL"
$dashboardWebsiteAdminApiKey = Require-EnvVar "VITE_PHANTOM_DASHBOARD_ADMIN_API_KEY"
$websiteWindowsBackendApiBaseUrl = Require-EnvVar "VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL"
$razorpayKeyId = [Environment]::GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_ID")
$razorpayKeySecret = [Environment]::GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_SECRET")
$razorpayWebhookSecret = [Environment]::GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_RAZORPAY_WEBHOOK_SECRET")
$otpProvider = [Environment]::GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_OTP_PROVIDER")
$otpApiKey = [Environment]::GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_OTP_API_KEY")
$otpTemplateName = [Environment]::GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_OTP_TEMPLATE_NAME")
$mockOtpCode = [Environment]::GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_MOCK_OTP_CODE")

$windowsBackendDir = Join-Path $repoRoot "phantom-windows-app-backend"
$dashboardBackendDir = Join-Path $repoRoot "phantom-dashboard-backend"
$websiteDir = Join-Path $repoRoot "phantom-website-dashboard"
$windowsAppDir = Join-Path $repoRoot "phantom-windows-app"
$windowsAppHostedConfigPath = Join-Path $windowsAppDir "phantom.hosted.json"

$windowsAppHostedConfig = @{
    mode = "remote"
    websiteBaseUrl = $websiteBaseUrl
    desktopBackendBaseUrl = $windowsBackendBaseUrl
} | ConvertTo-Json

Set-Content -Path $windowsAppHostedConfigPath -Value $windowsAppHostedConfig -Encoding UTF8

if ($SeedUsers) {
    Write-Host "Seeding desktop test users into the configured PostgreSQL database..."
    Push-Location $windowsBackendDir
    try {
        $env:PHANTOM_WINDOWS_BACKEND_DATABASE_URL = $windowsBackendDbUrl
        $env:PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY = $windowsBackendAdminApiKey
        dotnet run -- --seed-test-users
    }
    finally {
        Pop-Location
    }
}

$windowsBackendCommand = @"
`$env:PHANTOM_WINDOWS_BACKEND_DATABASE_URL = '$windowsBackendDbUrl'
`$env:PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY = '$windowsBackendAdminApiKey'
`$env:PHANTOM_PUBLIC_WEBSITE_BASE_URL = '$websiteBaseUrl'
`$env:PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_ID = '$razorpayKeyId'
`$env:PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_SECRET = '$razorpayKeySecret'
`$env:PHANTOM_WINDOWS_BACKEND_RAZORPAY_WEBHOOK_SECRET = '$razorpayWebhookSecret'
`$env:PHANTOM_WINDOWS_BACKEND_OTP_PROVIDER = '$otpProvider'
`$env:PHANTOM_WINDOWS_BACKEND_OTP_API_KEY = '$otpApiKey'
`$env:PHANTOM_WINDOWS_BACKEND_OTP_TEMPLATE_NAME = '$otpTemplateName'
`$env:PHANTOM_WINDOWS_BACKEND_MOCK_OTP_CODE = '$mockOtpCode'
dotnet restore
dotnet run --urls http://localhost:5057
"@

$dashboardBackendCommand = @"
`$env:PHANTOM_DASHBOARD_BACKEND_DATABASE_URL = '$dashboardBackendDbUrl'
`$env:PHANTOM_DASHBOARD_ADMIN_API_KEY = '$dashboardAdminApiKey'
`$env:PHANTOM_WINDOWS_BACKEND_BASE_URL = '$windowsBackendBaseUrl'
`$env:PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY = '$windowsBackendAdminApiKey'
dotnet restore
dotnet run --urls http://localhost:5067
"@

$websiteCommand = @"
`$env:VITE_PHANTOM_DASHBOARD_API_BASE_URL = '$dashboardApiBaseUrl'
`$env:VITE_PHANTOM_DASHBOARD_ADMIN_API_KEY = '$dashboardWebsiteAdminApiKey'
`$env:VITE_PHANTOM_WINDOWS_BACKEND_API_BASE_URL = '$websiteWindowsBackendApiBaseUrl'
npm install
npm run dev
"@

$windowsAppCommand = @"
`$env:PHANTOM_HOSTED_MODE = 'remote'
`$env:PHANTOM_WINDOWS_BACKEND_BASE_URL = '$windowsBackendBaseUrl'
`$env:PHANTOM_WEBSITE_BASE_URL = '$websiteBaseUrl'
dotnet build .\SecureOverlay.sln -c Debug
& '.\bin\Debug\net8.0-windows\svchost-shell.exe'
"@

Open-PowerShellWindow -Title "Phantom Windows Backend" -WorkingDirectory $windowsBackendDir -Command $windowsBackendCommand
Start-Sleep -Seconds 2
Open-PowerShellWindow -Title "Phantom Dashboard Backend" -WorkingDirectory $dashboardBackendDir -Command $dashboardBackendCommand
Start-Sleep -Seconds 2
Open-PowerShellWindow -Title "Phantom Website Dashboard" -WorkingDirectory $websiteDir -Command $websiteCommand
Start-Sleep -Seconds 2
Open-PowerShellWindow -Title "Phantom Windows App" -WorkingDirectory $windowsAppDir -Command $windowsAppCommand

Write-Host "Opened Phantom local stack windows."
Write-Host "Website: $websiteBaseUrl"
Write-Host "Windows backend: $windowsBackendBaseUrl"
Write-Host "Dashboard backend: http://localhost:5067"
Write-Host "Windows app hosted config: $windowsAppHostedConfigPath"
