# Full integration test: Gateway + FakeController
$ErrorActionPreference = "Stop"

Write-Host "=== Starting Gateway ===" -ForegroundColor Cyan
$gatewayProc = Start-Process -FilePath "dotnet" `
    -ArgumentList "run","--no-build","--urls","http://localhost:5050" `
    -WorkingDirectory "C:\Users\Ai02\Desktop\PETER ZAKARIA\New folder\CompanyBowling\Server\Bowling.Gateway" `
    -PassThru

Write-Host "Gateway PID: $($gatewayProc.Id)" -ForegroundColor Gray
Start-Sleep 5

Write-Host "=== Testing Gateway Health ===" -ForegroundColor Cyan
try {
    $health = Invoke-RestMethod -Uri "http://localhost:5050/health" -Method Get -ErrorAction Stop
    Write-Host "Health: $($health.status)" -ForegroundColor Green
} catch {
    Write-Host "Health check failed: $($_.Exception.Message)" -ForegroundColor Red
    Stop-Process -Id $gatewayProc.Id -Force
    exit 1
}

Write-Host "=== Creating Session ===" -ForegroundColor Cyan
$session = Invoke-RestMethod -Uri "http://localhost:5050/api/session/create" -Method Post -ErrorAction Stop
$token = $session.sessionToken
Write-Host "Token: $token" -ForegroundColor Green

Write-Host "=== Running FakeController ===" -ForegroundColor Cyan
cd "C:\Users\Ai02\Desktop\PETER ZAKARIA\New folder\CompanyBowling\Tools\FakeController"
$result = dotnet run -c Release -- --host localhost --port 5050 --token $token --throw Normal 2>&1
$result | ForEach-Object { Write-Host $_ }

Write-Host "=== Gateway Logs ===" -ForegroundColor Cyan
# The gateway output is in its console, we can't easily capture it here
# But we can check if the process is still running
if (Get-Process -Id $gatewayProc.Id -ErrorAction SilentlyContinue) {
    Write-Host "Gateway still running" -ForegroundColor Green
} else {
    Write-Host "Gateway process ended" -ForegroundColor Yellow
}

Write-Host "=== Test Complete ===" -ForegroundColor Cyan
Stop-Process -Id $gatewayProc.Id -Force -ErrorAction SilentlyContinue