<#
.SYNOPSIS
    Company Bowling MVP - Local Test Runner
.DESCRIPTION
    Starts the Bowling.Gateway and optionally runs the FakeController for testing.
    Does NOT modify firewall - run on localhost only unless firewall is pre-configured.
.NOTES
    Requires: .NET 8 SDK, Unity 2022.3+ (separate)
#>

param(
    [switch]$GatewayOnly,
    [switch]$WithFakeController,
    [string]$ThrowType = "Normal",
    [int]$Duration = 10,
    [string]$Token = ""
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "🎳 Company Bowling MVP - Test Runner" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$repoRoot = Split-Path $PSScriptRoot
$gatewayPath = Join-Path $repoRoot "Server\Bowling.Gateway"
$fakeControllerPath = Join-Path $repoRoot "Tools\FakeController"

# Check .NET
$dotnetVersion = dotnet --version
Write-Host "[Info] .NET SDK: $dotnetVersion" -ForegroundColor Gray

# Build Gateway
Write-Host "`n[1/3] Building Gateway..." -ForegroundColor Yellow
Push-Location $gatewayPath
dotnet build -c Release -v q
if ($LASTEXITCODE -ne 0) {
    Write-Host "[Error] Gateway build failed" -ForegroundColor Red
    exit 1
}
Pop-Location

# Build FakeController if needed
if ($WithFakeController) {
    Write-Host "`n[2/3] Building FakeController..." -ForegroundColor Yellow
    Push-Location $fakeControllerPath
    dotnet build -c Release -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[Error] FakeController build failed" -ForegroundColor Red
        exit 1
    }
    Pop-Location
}

# Start Gateway
Write-Host "`n[3/3] Starting Gateway on http://localhost:5050..." -ForegroundColor Green
Write-Host "[Info] Press Ctrl+C to stop" -ForegroundColor Gray

$gatewayProcess = Start-Process -FilePath "dotnet" `
    -ArgumentList "run", "--no-build", "-c", "Release", "--urls", "http://localhost:5050" `
    -WorkingDirectory $gatewayPath `
    -PassThru

# Wait for gateway to start
Start-Sleep 3

# Test health endpoint
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5050/health" -Method Get -ErrorAction Stop
    Write-Host "[OK] Gateway healthy: $($health.status)" -ForegroundColor Green
} catch {
    Write-Host "[Error] Gateway health check failed" -ForegroundColor Red
    Stop-Process -Id $gatewayProcess.Id -Force
    exit 1
}

# Create session if using FakeController
if ($WithFakeController -and -not [string]::IsNullOrEmpty($Token)) {
    Write-Host "`n[Info] Creating session..." -ForegroundColor Yellow
    $session = Invoke-RestMethod -Uri "http://localhost:5050/api/session/create" -Method Post
    $Token = $session.sessionToken
    Write-Host "[OK] Session created: $Token" -ForegroundColor Green
    Write-Host "[Info] QR URL: http://localhost:5050/join/$Token" -ForegroundColor Cyan
}

if ($WithFakeController) {
    if ([string]::IsNullOrEmpty($Token)) {
        Write-Host "[Error] Token required for FakeController. Use -Token or let script create one." -ForegroundColor Red
        Stop-Process -Id $gatewayProcess.Id -Force
        exit 1
    }

    Write-Host "`n[Info] Starting FakeController ($ThrowType throw)..." -ForegroundColor Yellow
    Push-Location $fakeControllerPath
    dotnet run -c Release -- --host localhost --port 5050 --token $Token --throw $ThrowType
    Pop-Location
}

# Keep gateway running if not using FakeController
if (-not $WithFakeController) {
    Write-Host "`n[Info] Gateway running. Press Ctrl+C to stop." -ForegroundColor Cyan
    try {
        $gatewayProcess.WaitForExit()
    } catch {
        Stop-Process -Id $gatewayProcess.Id -Force
    }
} else {
    Stop-Process -Id $gatewayProcess.Id -Force
}

Write-Host "`n[Done] Test complete" -ForegroundColor Cyan