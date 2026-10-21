# Test script that runs gateway and captures output
$gatewayPath = "C:\Users\Ai02\Desktop\PETER ZAKARIA\New folder\CompanyBowling\Server\Bowling.Gateway"
$logFile = "C:\Users\Ai02\Desktop\PETER ZAKARIA\New folder\CompanyBowling\gateway_test.log"

# Kill any existing gateway
taskkill /F /IM Bowling.Gateway.exe /T 2>$null
Start-Sleep 1

# Start gateway with output to log file
$process = Start-Process -FilePath "dotnet" `
    -ArgumentList "run","--no-build","--urls","http://localhost:5050" `
    -WorkingDirectory $gatewayPath `
    -PassThru `
    -RedirectStandardOutput $logFile `
    -RedirectStandardError $logFile

Write-Host "Gateway started with PID: $($process.Id)"
Write-Host "Logging to: $logFile"
Start-Sleep 4

# Test health
$health = Invoke-RestMethod -Uri "http://localhost:5050/health" -Method Get
Write-Host "Health: $($health.status)"

# Create session
$session = Invoke-RestMethod -Uri "http://localhost:5050/api/session/create" -Method Post
$token = $session.sessionToken
Write-Host "Token: $token"

# Run FakeController
Write-Host "Running FakeController..."
cd "C:\Users\Ai02\Desktop\PETER ZAKARIA\New folder\CompanyBowling\Tools\FakeController"
dotnet run -c Release -- --host localhost --port 5050 --token $token --throw Normal

# Show gateway logs
Write-Host "`n=== GATEWAY LOGS ==="
Get-Content $logFile -ErrorAction SilentlyContinue

# Cleanup
Stop-Process -Id $process.Id -Force