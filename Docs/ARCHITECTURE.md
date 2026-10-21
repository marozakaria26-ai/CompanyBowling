# Company Bowling MVP - Architecture

## System Overview

```
┌─────────────────┐     WebSocket      ┌──────────────────┐     WebSocket      ┌─────────────────┐
│  Android Phone  │ ◄─────────────────► │  Bowling.Gateway │ ◄─────────────────► │  Unity Game     │
│  (Controller)   │   Sensor Frames    │  (ASP.NET Core)  │   Throw Events     │  (Physics)      │
└─────────────────┘                    └──────────────────┘                    └─────────────────┘
        │                                      │                                      │
        │ Sensors                              │ Session Mgmt                         │ Rigidbody
        │ - Accelerometer                      │ - Token Gen                          │ - Ball
        │ - Gyroscope                          │ - QR Code                            │ - Pins
        │ - Orientation                        │ - Throw Detect                       │ - Lane
        ▼                                      ▼                                      ▼
   Throw Detect                          Forward to                              Physics Sim
   (Local UI)                            Unity                                    (Authoritative)
```

---

## Components

### 1. Bowling.Protocol (Shared Library)
**Location:** `Shared/Bowling.Protocol/`

Strongly-typed message definitions for all components:
- `MessageType` enum (17 types)
- `MessageBase` abstract record with timestamp
- Concrete messages: `SensorFrameMessage`, `ThrowReleasedMessage`, etc.
- JSON serialization via `ProtocolJson`
- **Target:** .NET Standard 2.0 / .NET 8

### 2. Bowling.Gateway (ASP.NET Core Server)
**Location:** `Server/Bowling.Gateway/`

#### Responsibilities
- HTTP REST API for session management
- SignalR WebSocket hubs for real-time communication
- Session lifecycle (create, pair, expire)
- Authoritative throw detection
- QR code generation
- Sensor validation & rate limiting

#### Key Services
| Service | Purpose |
|---------|---------|
| `ISessionManager` | Token generation, pairing, expiration |
| `IThrowDetector` | Server-side gesture recognition |
| `IQrCodeService` | PNG QR code generation (Base64) |
| `IUnityNotifier` | Bridge Phone Hub → Unity Hub |

#### SignalR Hubs
| Hub | Path | Clients |
|-----|------|---------|
| `PhoneHub` | `/ws` | Android app |
| `UnityHub` | `/unity` | Unity game |

#### Configuration (`appsettings.json`)
```json
{
  "Gateway": { "Port": 5050, "SessionExpirationSeconds": 60 },
  "ThrowDetection": { 
    "IdleAccelerationThreshold": 1.5,
    "ForwardSwingAccelerationThreshold": 8.0,
    "PowerScalingFactor": 0.12,
    ...
  }
}
```

### 3. Android Controller (Kotlin)
**Location:** `Mobile/Bowling.Controller/`

#### Architecture
```
MainActivity → ScanActivity → ControllerActivity
                    ↓              ↓
              ZXing QR          SensorService (Foreground)
                                    ↓
                          SensorManagerImpl (60Hz)
                                    ↓
                          ThrowDetector (State Machine)
                                    ↓
                          GatewayWebSocketClient
```

#### Key Classes
| Class | Purpose |
|-------|---------|
| `SensorManagerImpl` | Accelerometer + Gyroscope + Rotation Vector @ 60Hz |
| `ThrowDetector` | 6-state machine (IDLE→READY→BACKSWING→FORWARD→RELEASE→COOLDOWN) |
| `GatewayWebSocketClient` | Java-WebSocket client with auto-reconnect |
| `SensorService` | Foreground service (wake lock, notification) |

#### Throw Detection Algorithm
```
IDLE: Wait for phone held upright (Y accel > 1.5g)
READY: Wait for backswing (Y accel < -3g, duration 300-2000ms)
BACKSWING: Track peak acceleration, wait for forward swing (Y accel > 8g)
FORWARD_SWING: Track peak gyro, detect release (Y accel drops, gyro > 2.5)
RELEASE: Calculate parameters, send to gateway
COOLDOWN: 1s lockout, return to IDLE
```

#### Parameters Calculated
```kotlin
ThrowParameters(
  power: 0.0-1.0 (forward accel × 0.12)
  direction: -1.0 to +1.0 (lateral accel × 0.08)
  spin: -1.0 to +1.0 (peak gyro × 0.15)
  releaseAngle: -15° to +45° (atan2(vertical, forward))
)
```

### 4. Unity Game (C#)
**Location:** `Game/CompanyBowling.Unity/Assets/Scripts/`

#### Scripts
| Script | Purpose |
|--------|---------|
| `GatewayConnection` | SignalR WebSocket client (WebSocketSharp) |
| `BowlingGameController` | Physics mapping, lane reset |
| `GameUIController` | Debug UI (status, sensor rate, throw params) |

#### Physics Mapping
```csharp
// ThrowReleasedMessage → Rigidbody
velocity = new Vector3(
  direction * maxSpeed * directionScale,
  sin(releaseAngle) * forwardSpeed,
  cos(releaseAngle) * forwardSpeed
);
angularVelocity = new Vector3(0, spin * maxAngularSpeed * spinScale, 0);
```

#### Scene Hierarchy
```
BowlingScene
├── Lane (MeshCollider)
├── Ball (Rigidbody, SphereCollider)
├── Pins[10] (Rigidbody, MeshCollider)
├── Camera
├── Lighting
├── GameManager (BowlingGameController)
├── NetworkManager (GatewayConnection)
└── Canvas (GameUIController)
```

---

## Communication Protocols

### Phone → Gateway (WebSocket `/ws`)
```json
// Register
{"type":"phoneConnected","sessionToken":"abc","deviceId":"...","deviceName":"..."}

// Sensor Frame (60Hz)
{"type":"sensorFrame","sessionToken":"abc","timestamp":123456789,
 "acceleration":{"x":0.1,"y":2.8,"z":8.4},
 "gyroscope":{"x":0.2,"y":-0.4,"z":1.7}}

// Controller Ready
{"type":"controllerReady","sessionToken":"abc"}
```

### Gateway → Unity (WebSocket `/unity`)
```json
// Phone Connected
{"type":"phoneConnected","sessionToken":"abc","deviceId":"...","deviceName":"..."}

// Sensor Frame (forwarded)
{"type":"sensorFrame",...}

// Throw Released (authoritative)
{"type":"throwReleased","sessionToken":"abc",
 "parameters":{"power":0.82,"direction":-0.07,"spin":0.14,"releaseAngle":6.8,...}}

// Gateway Info
{"type":"gatewayInfo","ipAddress":"192.168.x.x","port":5050,"connectedPhones":1,"unityConnected":true}
```

### Unity → Gateway
```json
// Throw Complete
{"type":"throwComplete","pinsKnockedDown":7,"ballFinalPositionZ":18.2}

// Reset Lane
{"type":"resetLane"}
```

### HTTP API (Gateway)
```
POST /api/session/create → SessionCreatedMessage + QR Base64
GET  /api/session/join/{token} → HTML page with QR
GET  /api/session/status/{token} → Pairing status JSON
GET  /api/session/qr/{token} → QR Base64 JSON
GET  /health → {"status":"healthy"}
```

---

## Data Flow

### Session Creation
1. Unity/Game calls `POST /api/session/create`
2. Gateway generates cryptographically secure token (32 bytes → Base64URL)
3. Gateway creates `GameSession` with 60s expiration
4. Gateway generates QR code (PNG → Base64)
5. Returns `SessionCreatedMessage` + QR

### Pairing
1. Phone scans QR → extracts token + gateway URL
2. Phone connects WebSocket `/ws`
3. Phone sends `RegisterPhone` with token + device info
4. Gateway validates: exists, not expired, not used, not paired
5. Gateway marks session paired, notifies Unity via `UnityHub`
6. Phone shows "CONNECTED", starts sensor streaming

### Sensor Streaming
1. Phone reads sensors @ 60Hz (SensorManager.SENSOR_DELAY_GAME)
2. Phone sends `SensorFrameMessage` per frame (batched if needed)
3. Gateway validates ranges (accel ≤ 100, gyro ≤ 50)
4. Gateway runs **authoritative** `ThrowDetector.ProcessFrame()`
4. Gateway forwards frame to Unity via `UnityHub`
5. Unity displays real-time sensor rate

### Throw Detection (Server-Authoritative)
1. Gateway `ThrowDetector` processes each frame
2. State machine tracks: IDLE→READY→BACKSWING→FORWARD→RELEASE
3. On RELEASE: calculates `ThrowParameters`
4. If valid (power ≥ 0.15): sends `ThrowReleasedMessage` to Unity
5. Phone also runs local detector for UI feedback

### Physics Execution
1. Unity receives `ThrowReleasedMessage`
2. `BowlingGameController.ExecuteThrow()` maps params to Rigidbody
3. Ball launched with velocity + angular velocity
4. Physics sim runs until ball stops or exits lane
5. Unity sends `ThrowCompleteMessage` with pins knocked down
6. Lane auto-resets after 5s (or manual Reset button)

---

## Security Model

| Aspect | Implementation |
|--------|---------------|
| Network | Isolated LAN only, no Internet |
| Authentication | Single-use session tokens (32-byte random) |
| Authorization | Token must match active session |
| Token Expiry | 60s unused, invalidated after pairing |
| Data Validation | Sensor range checks, rate limiting |
| Transport | Plain WebSocket (local trusted network) |

---

## Configuration Reference

### Gateway (Server)
| Setting | Default | Description |
|---------|---------|-------------|
| `Gateway:Port` | 5050 | HTTP + WebSocket port |
| `Gateway:UnityPort` | 5051 | (Reserved) |
| `Gateway:SessionExpirationSeconds` | 60 | Pairing window |
| `ThrowDetection:PowerScalingFactor` | 0.12 | Accel → Power |
| `ThrowDetection:DirectionScalingFactor` | 0.08 | Lateral → Direction |
| `ThrowDetection:SpinScalingFactor` | 0.15 | Gyro → Spin |

### Android
| Setting | Default | Description |
|---------|---------|-------------|
| `targetFrequencyHz` | 60 | Sensor polling rate |
| `ThrowDetectorConfig` | (see class) | Gesture thresholds |

### Unity
| Setting | Default | Description |
|---------|---------|-------------|
| `maxBallSpeed` | 25 | Max forward velocity |
| `maxAngularSpeed` | 50 | Max spin (deg/s) |
| `directionScale` | 1.0 | Direction multiplier |
| `spinScale` | 1.0 | Spin multiplier |
| `throwCooldown` | 3s | Min time between throws |

---

## Extensibility Points

1. **New Sensor Types** - Add to `SensorFrameMessage`, update detectors
2. **Multi-Phone** - `ISessionManager` supports multiple sessions
3. **Tournaments** - Add `SessionController` endpoints for scoring
4. **Different Games** - New `ThrowDetector` implementations
5. **Corporate Backend** - Gateway can forward to external API (future)