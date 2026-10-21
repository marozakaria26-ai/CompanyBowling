# Company Bowling MVP - Testing Guide

## Test Environment Setup

### Hardware Requirements
- Windows PC with .NET 8/10 SDK
- Android phone (Android 7+, accelerometer + gyroscope)
- Isolated Wi-Fi network (no Internet required)

### Software Versions Tested
- .NET SDK: 8.0.424, 10.0.400
- Unity: 2022.3 LTS+
- Android: API 24-34
- Gradle: 8.2+

---

## Pre-Test Checklist

### Network
- [ ] PC and phone connected to same Wi-Fi (e.g., "BOWLING-TEST")
- [ ] No Internet access on Wi-Fi (verify: phone browser can't reach google.com)
- [ ] PC firewall allows inbound TCP 5050, 5051
- [ ] PC IP known: `ipconfig | findstr IPv4` → note address (e.g., 192.168.4.12)

### Gateway Build
- [ ] `cd Server/Bowling.Gateway && dotnet build -c Release` succeeds
- [ ] No compilation errors

### Android Build
- [ ] `cd Mobile/Bowling.Controller && ./gradlew assembleDebug` succeeds
- [ ] APK installed on phone: `adb install app/build/outputs/apk/debug/app-debug.apk`
- [ ] App appears in launcher

### Unity
- [ ] Project opens in Unity 2022.3+ without errors
- [ ] Packages restored (Newtonsoft.Json, WebSocketSharp)

---

## End-to-End Test Sequence

### Test 1: Gateway Startup
**Command:**
```powershell
cd CompanyBowling\Server\Bowling.Gateway
dotnet run -c Release
```

**Expected Output:**
```
🎳 Company Bowling Gateway Starting
========================================
Listening on: http://192.168.x.x:5050
Phone WebSocket: ws://192.168.x.x:5050/ws
Unity WebSocket: ws://192.168.x.x:5050/unity
Session API: http://192.168.x.x:5050/api/session/create
========================================
```

**Verify:**
- [ ] Console shows local LAN IP (not 127.0.0.1)
- [ ] Port 5050 listening: `netstat -an | findstr 5050`
- [ ] Health endpoint: `curl http://<PC_IP>:5050/health` → `{"status":"healthy"}`

---

### Test 2: Session Creation
**Command:**
```powershell
curl -X POST http://<PC_IP>:5050/api/session/create
```

**Expected Response:**
```json
{
  "sessionToken": "abc123...",
  "gatewayUrl": "http://192.168.x.x:5050",
  "port": 5050,
  "expiresAt": 638000000000000000,
  "qrCodeBase64": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUg..."
}
```

**Verify:**
- [ ] `sessionToken` present (Base64URL format)
- [ ] `qrCodeBase64` starts with `data:image/png;base64,`
- [ ] `gatewayUrl` matches PC LAN IP
- [ ] Token expires ~60s from now

---

### Test 3: QR Code Display
**Action:** Open browser to `http://<PC_IP>:5050/join/<TOKEN>`

**Expected:**
- [ ] Page loads with "Company Bowling" title
- [ ] QR code visible (256x256+ pixels)
- [ ] Token displayed below QR
- [ ] Status: "Waiting for phone to connect..."
- [ ] Auto-refresh checks status every 2s

---

### Test 4: Phone Connection
**Action:** 
1. Open "Company Bowling Controller" app on phone
2. Tap "Scan QR Code"
3. Scan QR from PC screen
4. App transitions to Controller screen

**Expected:**
- [ ] Camera permission granted
- [ ] QR scans successfully
- [ ] App shows "Status: Connecting..." → "Status: Connected"
- [ ] Gateway logs: "Phone registered successfully: abcd****wxyz"
- [ ] Unity logs: "Phone connected: <device_name>"

**Gateway Console:**
```
Phone registration attempt: abcd****wxyz Device: Pixel 7
Phone registered successfully: abcd****wxyz
```

---

### Test 5: Sensor Streaming
**Action:** Hold phone, move it around gently

**Expected:**
- [ ] Gateway logs: "Sensor stream: XX Hz" (updates periodically)
- [ ] Phone screen shows live Accel/Gyro values changing
- [ ] Unity UI shows "Sensor Rate: 50-60 Hz"
- [ ] No errors in gateway console

**Gateway Console:**
```
Sensor stream: 62 Hz
```

---

### Test 6: Idle/Ready Detection
**Action:** Hold phone upright (portrait, screen facing you) still for 2s

**Expected:**
- [ ] Phone state: "State: READY"
- [ ] Gateway throw detector: IDLE → READY transition logged (Debug level)
- [ ] No throw detected yet

---

### Test 7: Backswing Detection
**Action:** Pull phone back smoothly (like bowling backswing) - arm back, ~1-2 seconds

**Expected:**
- [ ] Phone state: "State: BACKSWING"
- [ ] Gateway logs (Debug): "Throw detector: READY → BACKSWING"
- [ ] Accelerometer Y goes negative (-3g to -10g range)

---

### Test 8: Forward Swing & Release Detection
**Action:** Swing phone forward smoothly, release at bottom (simulate ball release)

**Expected:**
- [ ] Phone state: "State: FORWARD_SWING" → "State: RELEASE" → "State: COOLDOWN"
- [ ] Gateway logs: "Throw detector: BACKSWING → FORWARD_SWING → RELEASE"
- [ ] Gateway logs: "Throw detected: Power=0.XX, Direction=X.XX, Spin=X.XX, Angle=X.X"
- [ ] Phone shows "LAST THROW DETECTED!" with parameters
- [ ] Unity receives `ThrowReleasedMessage`

**Gateway Console:**
```
Throw detector: BACKSWING → FORWARD_SWING
Throw detector: FORWARD_SWING → RELEASE
Throw detected: Power: 0.81, Direction: -0.04, Spin: 0.12, Angle: 6.8
```

---

### Test 9: Unity Physics Execution
**Action:** Observe Unity Game view after throw

**Expected:**
- [ ] Ball launches from start position
- [ ] Ball travels down lane with realistic physics
- [ ] Ball collides with pins
- [ ] Pins fall over (rigidbody physics)
- [ ] Ball continues or stops in pit
- [ ] Unity logs: "Throw executed: Velocity=(X,Y,Z), AngularVelocity=(0,Y,0)"

**Unity Console:**
```
[BowlingGame] Throw executed: Velocity=(-0.5, 0.0, 20.0), AngularVelocity=(0.0, 5.0, 0.0)
[BowlingGame] Throw complete received: 7 pins
```

---

### Test 10: Lane Reset
**Action:** Wait 5s after ball stops, or press "Reset" button in Unity UI

**Expected:**
- [ ] Ball returns to start position
- [ ] All 10 pins stand upright
- [ ] Phone state returns to "State: IDLE" → "State: READY"
- [ ] Can throw again

---

### Test 11: Session Expiry
**Action:** Don't scan QR for 65s after creation

**Expected:**
- [ ] Gateway logs: "Session expired/cleaned up: abcd****wxyz"
- [ ] QR page shows "Session expired"
- [ ] New session required

---

### Test 12: Disconnect/Reconnect
**Action:** 
1. Close phone app during session
2. Reopen app, scan new QR

**Expected:**
- [ ] Gateway logs: "Phone disconnected: Disconnected"
- [ ] Unity shows phone disconnected
- [ ] New session works normally

---

## Performance Benchmarks

| Metric | Target | Measured |
|--------|--------|----------|
| Sensor Rate | 50-100 Hz | ___ Hz |
| Latency (Phone→Unity) | <50ms | ___ ms |
| Throw Detection Accuracy | >90% | ___% |
| False Positive Rate | <5% | ___% |
| CPU (Gateway) | <10% | ___% |
| Battery (Phone, 10 min) | <5% | ___% |

---

## Manual Test Cases

### TC-01: Normal Bowl
- Hold phone naturally, smooth backswing, forward swing, release
- **Pass:** Ball rolls straight, hits pocket (1-3 or 1-2)

### TC-02: Left Hook
- Rotate wrist inward during release
- **Pass:** Ball curves left, spin parameter > 0.1

### TC-03: Right Hook  
- Rotate wrist outward during release
- **Pass:** Ball curves right, spin parameter < -0.1

### TC-04: Weak Throw
- Very gentle swing
- **Pass:** Power < 0.15, throw rejected, no ball movement

### TC-05: False Positive - Walking
- Walk around holding phone
- **Pass:** No throw detected (state stays IDLE/READY)

### TC-06: False Positive - Shaking
- Shake phone vigorously
- **Pass:** No throw detected (gyro pattern differs)

### TC-07: Rapid Throws
- Throw, wait 1s, throw again
- **Pass:** Second throw ignored (cooldown 3s)

### TC-08: Phone Rotation
- Rotate phone 90° (landscape) during game
- **Pass:** Sensor coords handled correctly (orientation quaternion)

---

## Automated Test Script (PowerShell)

```powershell
# Run from CompanyBowling root
# Requires: curl, adb, jq (optional)

$PC_IP = "192.168.4.12"  # Update!

Write-Host "=== Test 1: Gateway Health ==="
curl -s "http://$PC_IP:5050/health" | jq

Write-Host "=== Test 2: Create Session ==="
$session = curl -s -X POST "http://$PC_IP:5050/api/session/create" | jq -r '.sessionToken'
Write-Host "Token: $session"

Write-Host "=== Test 3: Session Status (pre-pair) ==="
curl -s "http://$PC_IP:5050/api/session/status/$session" | jq

Write-Host "=== Test 4: QR Code ==="
curl -s "http://$PC_IP:5050/api/session/qr/$session" | jq -r '.qrCodeBase64' | 
  Select-String -Pattern '^data:image/png;base64,' | 
  ForEach-Object { Write-Host "QR Valid: $_" }

Write-Host "=== Test 5: Pairing (manual) ==="
# Scan QR with phone, then:
curl -s "http://$PC_IP:5050/api/session/status/$session" | jq

Write-Host "=== Test 6: Gateway Info (WebSocket test) ==="
# In Unity or separate WS client connect to ws://$PC_IP:5050/unity
# Should receive gatewayInfo with unityConnected=true
```

---

## Debugging Tips

### Gateway Logs
```powershell
# Run with verbose logging
$env:ASPNETCORE_LOGGING__LOGLEVEL__DEFAULT="Debug"
dotnet run -c Release
```

### Android Logs
```powershell
# Filter for bowling app
adb logcat -s "GatewayWS" "SensorManagerImpl" "ThrowDetector" "SensorService"
```

### Unity Logs
- Open Console window
- Filter: "GatewayConnection", "BowlingGame"

### Network Capture
```powershell
# Wireshark filter
websocket && ip.addr == 192.168.4.12
```

---

## Known Issues & Workarounds

| Issue | Workaround |
|-------|------------|
| Phone can't scan QR (too small) | Increase QR size in browser (Ctrl+), or use manual entry |
| Sensor rate drops <30Hz | Close other apps, ensure screen on, disable battery optimization |
| Throw not detected | Adjust `ThrowDetection` config in appsettings.json |
| Ball doesn't move | Check Unity Rigidbody not kinematic, mass > 0 |
| Pins don't fall | Check pin colliders, mass, layer collision matrix |
| WebSocket disconnects | Check firewall, increase `ClientTimeoutInterval` |

---

## Sign-Off Criteria

All tests must pass for MVP release:

- [ ] Test 1: Gateway starts, health OK
- [ ] Test 2: Session creates, returns token + QR
- [ ] Test 3: QR page loads, displays correctly
- [ ] Test 4: Phone scans QR, connects, shows Connected
- [ ] Test 5: Sensor data streams at 50+ Hz
- [ ] Test 6: Idle→Ready transition works
- [ ] Test 7: Backswing detected
- [ ] Test 8: Forward swing + release detected, params valid
- [ ] Test 9: Unity ball moves, pins fall physically
- [ ] Test 10: Lane resets automatically
- [ ] Test 11: Session expires after 60s
- [ ] Test 12: Clean disconnect/reconnect

**Tester:** _______________ **Date:** _______________ **Build:** _______________