@echo off
cd /d "C:\Users\Ai02\Desktop\PETER ZAKARIA\New folder\CompanyBowling\Server\Bowling.Gateway"
dotnet run --no-build --urls "http://localhost:5050" 2>&1 > "C:\Users\Ai02\Desktop\PETER ZAKARIA\New folder\CompanyBowling\gateway_console.log"