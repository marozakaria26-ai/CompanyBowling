# Company Bowling MVP - Setup Guide

## Required Software

### Windows PC (Gateway + Unity)
- **.NET 8 SDK** or **.NET 10 SDK** (tested with 8.0.424 and 10.0.400)
- **Unity 2022.3 LTS** or newer
- **Git** (for cloning)
- **Windows 10/11** with Wi-Fi adapter

### Android Phone (Controller)
- **Android 7.0 (API 24)** or newer
- **Accelerometer** (required)
- **Gyroscope** (required)
- **Camera** (for QR scanning)
- **Android Studio** (for building APK) OR pre-built APK

### Network
- **Isolated Wi-Fi network** (e.g., "BOWLING-TEST")
- **No Internet required** - fully local
- PC and phone must be on same network

---

## Building the Gateway

### 1. Clone/Navigate to Repository
```powershell
cd CompanyBowling\Server\Bowling.Gateway
```

### 2. Restore Dependencies
```powershell
dotnet restore
```

### 3. Build
```powershell
dotnet build -c Release
```

### 4. Run
```powershell
dotnet run -c Release
```

The gateway will start on `http://0.0.0.0:5050` and display:
- Local IP address
- WebSocket endpoints
- Session creation URL

---

## Building the Android App

### Option A: Using Android Studio (Recommended)

1. Open `CompanyBowling\Mobile\Bowling.Controller` in Android Studio
2. Let Gradle sync complete
3. Connect Android phone via USB (USB debugging enabled)
4. Run → Run 'app' (or Build → Build Bundle(s) / APK(s) → Build APK(s))

### Option B: Command Line
```powershell
cd CompanyBowling\Mobile\Bowling.Controller
.\gradlew assembleDebug
```
APK location: `app\build\outputs\apk\debug\app-debug.apk`

### Install on Phone
```powershell
adb install app\build\outputs\apk\debug\app-debug.apk
```

---

## Opening Unity Project

1. Open **Unity Hub**
2. Click **Add** → Select `CompanyBowling\Game\CompanyBowling.Unity`
3. Open with **Unity 2022.3 LTS** or newer
4. Wait for package restoration

### Unity Scene Setup
The project includes these prefabs/scripts:
- `Assets/Scripts/Protocol/` - Protocol message classes
- `Assets/Scripts/Network/GatewayConnection.cs` - SignalR WebSocket client
- `Assets/Scripts/Game/BowlingGameController.cs` - Physics game logic
- `Assets/Scripts/UI/GameUIController.cs` - Debug UI

### Required Unity Packages
- `com.unity.nuget.newtonsoft-json` (for JSON serialization)
- WebSocket support (built-in `WebSocketSharp` or similar)

---

## Windows Firewall Configuration

**Critical:** Allow inbound connections on port 5050

### PowerShell (Admin):
```powershell
New-NetFirewallRule -DisplayName "Bowling Gateway" -Direction Inbound -LocalPort 5050 -Protocol TCP -Action Allow
New-NetFirewallRule -DisplayName "Bowling Gateway Unity" -Direction Inbound -LocalPort 5051 -Protocol TCP -Action Allow
```

### Or via Windows Defender Firewall GUI:
1. Open "Windows Defender Firewall with Advanced Security"
2. Inbound Rules → New Rule → Port → TCP → 5050, 5051 → Allow

---

## Connecting PC and Phone

### 1. Create Isolated Wi-Fi Network
**Option A: Mobile Hotspot (Phone)**
- Enable hotspot on phone
- Connect PC to phone's hotspot
- Note: Some phones isolate clients - test connectivity

**Option B: Travel Router (Recommended)**
- Configure router with SSID: `BOWLING-TEST`
- No WAN/Internet connection needed
- Connect both PC and phone

**Option C: Windows Mobile Hotspot**
- Settings → Network → Mobile Hotspot
- Share Ethernet/Wi-Fi connection
- Set SSID: `BOWLING-TEST`

### 2. Verify Connectivity
On PC (PowerShell):
```powershell
# Find PC IP on bowling network
ipconfig | findstr "IPv4"
```

On Phone:
- Connect to `BOWLING-TEST` Wi-Fi
- Open browser → `http://<PC_IP>:5050/health`
- Should return JSON: `{"status":"healthy",...}`

---

## Running the Complete Prototype

### Step 1: Start Gateway (PC)
```powershell
cd CompanyBowling\Server\Bowling.Gateway
dotnet run -c Release
```
Output:
```
🎳 Company Bowling Gateway Starting
Listening on: http://192.168.x.x:5050
Phone WebSocket: ws://192.168.x.x:5050/ws
Unity WebSocket: ws://192.168.x.x:5050/unity
Session API: http://192.168.x.x:5050/api/session/create
```

### Step 2: Create Session & Get QR
**Option A: Browser**
- Open `http://<PC_IP>:5050/api/session/create` 
- Returns JSON with `sessionToken` and `qrCodeBase64`

**Option B: Browser (Visual)**
- Open `http://<PC_IP>:5050/join/<TOKEN>`
- Displays QR code + status page

### Step 3: Start Unity Game (PC)
1. Open Unity project
2. Open `Assets/Scenes/BowlingScene.unity` (create if needed)
3. Add `GatewayConnection` to scene
4. Add `BowlingGameController` to scene
5. Add `GameUIController` to Canvas
6. Press **Play**

### Step 4: Scan QR (Phone)
1. Open **Company Bowling Controller** app
2. Tap **"Scan QR Code"**
3. Point at QR on PC screen
4. App launches controller screen

### Step 5: Bowl!
1. Hold phone **naturally** (portrait, screen facing you)
2. Make **bowling motion**: backswing → forward swing → release
3. Watch Unity: ball rolls down lane, hits pins

---

## Troubleshooting

### Gateway Won't Start
- Check port 5050 not in use: `netstat -an | findstr 5050`
- Run as Administrator if needed
- Check .NET version: `dotnet --version`

### Phone Can't Connect
- Verify same Wi-Fi network
- Check PC firewall allows port 5050
- Test: `ping <PC_IP>` from phone (use Termux or network tools)
- Try `http://<PC_IP>:5050/health` in phone browser

### QR Code Not Scanning
- Ensure good lighting
- Try manual entry in app (token + URL)
- Check QR contains correct PC IP (not 127.0.0.1)

### No Sensor Data
- Grant camera permission for QR
- Grant location permission (required for Wi-Fi scanning on Android 12+)
- Check phone has accelerometer + gyroscope

### Unity Not Receiving Data
- Check GatewayConnection WebSocket URL matches gateway
- Unity Console for connection logs
- Verify `BowlingGameController` references set in Inspector

---

## Quick Test Checklist

- [ ] PC: Gateway running on port 5050
- [ ] PC: Firewall allows 5050, 5051
- [ ] Phone: Connected to same Wi-Fi
- [ ] Phone: Can reach `http://<PC_IP>:5050/health`
- [ ] Phone: App installed, camera permission granted
- [ ] Unity: Project open, scene loaded, Play mode
- [ ] QR: Generated and displayed
- [ ] QR: Scanned successfully
- [ ] Phone: Shows "CONNECTED"
- [ ] Phone: Sensor values change when moving
- [ ] Bowl: Forward swing detected
- [ ] Unity: Ball moves, pins fall