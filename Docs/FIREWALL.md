# Firewall Configuration Reference

> **IMPORTANT**: This document describes the network requirements. 
> **DO NOT EXECUTE ANY FIREWALL COMMANDS** - Company security policy prohibits firewall modifications.
> This is for documentation and IT request purposes only.

## Required Inbound Access

| Component | Protocol | Port | Scope | Purpose |
|-----------|----------|------|-------|---------|
| Bowling.Gateway | TCP | 5050 | Private LAN only | Phone WebSocket (/ws), Unity WebSocket (/unity), HTTP API |

## Network Architecture

```
┌─────────────┐         ┌──────────────────┐         ┌─────────────┐
│  Android    │  Wi-Fi  │  Windows PC      │  Local  │  Unity Game │
│  Phone      │◄───────►│  Bowling.Gateway │◄───────►│  (Physics)  │
│  :5050/ws   │  LAN    │  0.0.0.0:5050    │  loop   │  :5050/unity│
└─────────────┘         └──────────────────┘         └─────────────┘
                              ▲
                              │ HTTP API
                              ▼
                        ┌─────────────┐
                        │  Browser    │
                        │  (QR page)  │
                        └─────────────┘
```

## Required Firewall Rule (for IT to create)

```powershell
# Run as Administrator - FOR IT USE ONLY
New-NetFirewallRule `
    -DisplayName "Company Bowling Gateway" `
    -Description "Inbound access for bowling controller phone and Unity game" `
    -Direction Inbound `
    -LocalPort 5050 `
    -Protocol TCP `
    -Action Allow `
    -Profile Private `
    -Program "C:\Path\To\Bowling.Gateway.exe" `
    -Enabled True
```

## Verification Commands (read-only, safe to run)

```powershell
# Check if rule exists
Get-NetFirewallRule -DisplayName "Company Bowling Gateway"

# Test local connectivity
Test-NetConnection -ComputerName localhost -Port 5050

# Test from another machine on same LAN
Test-NetConnection -ComputerName <PC_IP> -Port 5050
```

## Current Status

| Environment | Firewall Status | Notes |
|-------------|-----------------|-------|
| Development PC | **Unknown** | Requires IT approval |
| Corporate LAN | **Blocked by default** | Private profile only if approved |
| Isolated Test Wi-Fi | **Depends on router** | No Internet = no cloud firewall |

## Alternative Architectures (if firewall cannot be opened)

1. **Localhost-only testing**: Use FakeController on same PC
2. **VPN/Tunnel**: Company-approved VPN with port forwarding
3. **IT-managed test VLAN**: Request isolated VLAN with firewall exception
4. **Reverse proxy**: Company-approved reverse proxy (e.g., nginx, IIS ARR)

## Security Notes

- Gateway binds to `0.0.0.0:5050` (all interfaces)
- No authentication on WebSocket endpoints (session token in message)
- No encryption (plain WebSocket) - intended for isolated LAN only
- Session tokens expire after 60 seconds unused
- Tokens are single-use (invalidated after pairing)
- No persistent data storage
- No external network calls

## For IT Security Review

- **Application**: Bowling.Gateway (ASP.NET Core 8)
- **Protocol**: WebSocket (RFC 6455) + HTTP REST
- **Data**: Accelerometer/gyroscope sensor frames (~60 Hz)
- **PII**: None (random device ID only)
- **Network**: Single port 5050, inbound only
- **Dependencies**: .NET 8 runtime, no database, no external services