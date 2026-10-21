namespace Bowling.Gateway.Services;

using Bowling.Protocol;
using System.Security.Cryptography;
using System.IO;

public sealed class GameSession
    {
        public string Token { get; }
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public DateTime CreatedAt { get; }
        public DateTime PairingExpiresAt { get; }
        public DateTime ActiveExpiresAt { get; set; }
        public bool IsPaired { get; set; }
        public bool IsUsed { get; set; }
        public DateTime? PairedAt { get; set; }
        public DateTime? LastSensorFrameAt { get; set; }
        public int SensorFrameCount { get; set; }
        public string? QrCodeBase64 { get; set; }

        public GameSession(string token, TimeSpan pairingExpiration, TimeSpan activeExpiration)
        {
            Token = token;
            CreatedAt = DateTime.UtcNow;
            PairingExpiresAt = CreatedAt.Add(pairingExpiration);
            ActiveExpiresAt = CreatedAt.Add(activeExpiration);
            
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session_debug.log");
            File.AppendAllText(logPath, $"[GameSession] Created: Token={token}, CreatedAt={CreatedAt:O}, PairingExpiresAt={PairingExpiresAt:O}, ActiveExpiresAt={ActiveExpiresAt:O}\n");
        }

        public bool IsPairingExpired => DateTime.UtcNow > PairingExpiresAt;
        public bool IsActiveExpired => DateTime.UtcNow > ActiveExpiresAt;
        public bool IsPairingValid => !IsPairingExpired && !IsUsed;
        public bool IsActiveValid => IsPaired && !IsActiveExpired;
    }

public interface ISessionManager
{
    GameSession CreateSession(TimeSpan? pairingExpiration = null, TimeSpan? activeExpiration = null);
    GameSession? GetSession(string token);
    bool TryPairSession(string token, string deviceId, string deviceName);
    void UnpairSession(string token);
    void MarkSensorFrameReceived(string token);
    void CleanupExpiredSessions();
    IEnumerable<GameSession> GetActiveSessions();
}

public sealed class SessionManager : ISessionManager
{
    private readonly Dictionary<string, GameSession> _sessions = new();
    private readonly TimeSpan _pairingExpiration;
    private readonly TimeSpan _activeExpiration;
    private readonly ILogger<SessionManager> _logger;
    private readonly object _lock = new();
    private Timer? _cleanupTimer;

    public SessionManager(TimeSpan pairingExpiration, TimeSpan activeExpiration, ILogger<SessionManager> logger)
    {
        _pairingExpiration = pairingExpiration;
        _activeExpiration = activeExpiration;
        _logger = logger;
        // Disable cleanup timer for testing - sessions expire naturally
        // _cleanupTimer = new Timer(_ => CleanupExpiredSessions(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public GameSession CreateSession(TimeSpan? pairingExpiration = null, TimeSpan? activeExpiration = null)
    {
        var token = GenerateSecureToken();
        var session = new GameSession(token, pairingExpiration ?? _pairingExpiration, activeExpiration ?? _activeExpiration);
        
        lock (_lock)
        {
            _sessions[token] = session;
        }
        
        _logger.LogInformation("Session created: {Token} (pairing expires at {PairingExpiresAt}, active expires at {ActiveExpiresAt})", 
            MaskToken(token), session.PairingExpiresAt, session.ActiveExpiresAt);
        _logger.LogDebug("Session stored with key: {Token}, Dictionary count: {Count}", token, _sessions.Count);
        return session;
    }

    public GameSession? GetSession(string token)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(token, out var session) ? session : null;
        }
    }

    public bool TryPairSession(string token, string deviceId, string deviceName)
    {
        lock (_lock)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session_debug.log");
            File.AppendAllText(logPath, $"[TryPairSession] Called with token: {token}\n");
            File.AppendAllText(logPath, $"[TryPairSession] Available sessions: {_sessions.Count}\n");
            foreach (var kvp in _sessions)
            {
                File.AppendAllText(logPath, $"[TryPairSession] Session: '{kvp.Key}', PairingExpired: {kvp.Value.IsPairingExpired}, Used: {kvp.Value.IsUsed}, Paired: {kvp.Value.IsPaired}\n");
            }

            if (!_sessions.TryGetValue(token, out var session))
            {
                File.AppendAllText(logPath, $"[TryPairSession] Session not found for token: {token}. Available keys: {string.Join(", ", _sessions.Keys.Select(MaskToken))}\n");
                return false;
            }

            if (session.IsPairingExpired || session.IsUsed || session.IsPaired)
            {
                File.AppendAllText(logPath, $"[TryPairSession] Session invalid state: PairingExpired={session.IsPairingExpired}, Used={session.IsUsed}, Paired={session.IsPaired}\n");
                return false;
            }

            session.IsPaired = true;
            session.IsUsed = true;
            session.DeviceId = deviceId;
            session.DeviceName = deviceName;
            session.PairedAt = DateTime.UtcNow;
            
            File.AppendAllText(logPath, $"[TryPairSession] Session paired: {MaskToken(token)} Device: {deviceName}\n");
            _logger.LogInformation("Session paired: {Token} Device: {DeviceName}", MaskToken(token), deviceName);
            return true;
        }
    }

    public void UnpairSession(string token)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(token, out var session))
            {
                session.IsPaired = false;
                session.DeviceId = "";
                session.DeviceName = "";
                session.PairedAt = null;
                _logger.LogInformation("Session unpaired: {Token}", MaskToken(token));
            }
        }
    }

    public void MarkSensorFrameReceived(string token)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(token, out var session))
            {
                session.LastSensorFrameAt = DateTime.UtcNow;
                session.SensorFrameCount++;
                
                // Extend active session on activity
                if (session.IsPaired)
                {
                    session.ActiveExpiresAt = DateTime.UtcNow.Add(TimeSpan.FromMinutes(30));
                }
            }
        }
    }

    public void CleanupExpiredSessions()
    {
        var expiredTokens = new List<string>();
        
        lock (_lock)
        {
            _logger.LogDebug("CleanupExpiredSessions called, session count: {Count}", _sessions.Count);
            foreach (var (token, session) in _sessions)
            {
                _logger.LogDebug("Session {Token}: PairingExpired={PairingExpired}, ActiveExpired={ActiveExpired}, Paired={Paired}, LastFrame={LastFrame}", 
                    MaskToken(token), session.IsPairingExpired, session.IsActiveExpired, session.IsPaired, session.LastSensorFrameAt);
                
                // Clean up unpaired expired sessions
                if (session.IsPairingExpired && !session.IsPaired)
                {
                    expiredTokens.Add(token);
                }
                // Clean up paired but inactive sessions (no sensor data for 30 seconds)
                else if (session.IsPaired && session.LastSensorFrameAt.HasValue && 
                    DateTime.UtcNow - session.LastSensorFrameAt.Value > TimeSpan.FromSeconds(30))
                {
                    expiredTokens.Add(token);
                }
                // Clean up active expired sessions
                else if (session.IsActiveExpired)
                {
                    expiredTokens.Add(token);
                }
            }

            foreach (var token in expiredTokens)
            {
                _sessions.Remove(token);
                _logger.LogInformation("Session expired/cleaned up: {Token}", MaskToken(token));
            }
        }
    }

    public IEnumerable<GameSession> GetActiveSessions()
    {
        lock (_lock)
        {
            return _sessions.Values.Where(s => s.IsActiveValid).ToList();
        }
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private static string MaskToken(string token)
    {
        return token.Length > 8 ? token[..4] + "****" + token[^4..] : "****";
    }
}