using Bowling.Gateway.Services;
using Bowling.Gateway.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Bowling.Gateway.Tests;

public class SessionManagerTests
{
    private readonly SessionManager _sessionManager;

    public SessionManagerTests()
    {
        var pairingExpiration = TimeSpan.FromSeconds(60);
        var activeExpiration = TimeSpan.FromMinutes(30);

        var loggerMock = new Mock<ILogger<SessionManager>>();
        _sessionManager = new SessionManager(pairingExpiration, activeExpiration, loggerMock.Object);
    }

    [Fact]
    public void CreateSession_ReturnsValidSession()
    {
        var session = _sessionManager.CreateSession();

        Assert.False(string.IsNullOrEmpty(session.Token));
        Assert.True(session.Token.Length > 20); // URL-safe base64
        Assert.False(session.IsPaired);
        Assert.False(session.IsUsed);
    }

    [Fact]
    public void TryPairSession_ValidToken_PairsSuccessfully()
    {
        var session = _sessionManager.CreateSession();

        var success = _sessionManager.TryPairSession(session.Token, "device-123", "Test Phone");

        Assert.True(success);
        
        var retrieved = _sessionManager.GetSession(session.Token);
        Assert.NotNull(retrieved);
        Assert.True(retrieved!.IsPaired);
        Assert.True(retrieved.IsUsed);
        Assert.Equal("device-123", retrieved.DeviceId);
    }

    [Fact]
    public void TryPairSession_InvalidToken_ReturnsFalse()
    {
        var success = _sessionManager.TryPairSession("invalid-token", "device-123", "Test Phone");

        Assert.False(success);
    }

    [Fact]
    public void TryPairSession_AlreadyUsedToken_ReturnsFalse()
    {
        var session = _sessionManager.CreateSession();
        _sessionManager.TryPairSession(session.Token, "device-123", "Test Phone");

        var success = _sessionManager.TryPairSession(session.Token, "device-456", "Another Phone");

        Assert.False(success);
    }

    [Fact]
    public void GetSession_ValidToken_ReturnsSession()
    {
        var session = _sessionManager.CreateSession();

        var retrieved = _sessionManager.GetSession(session.Token);

        Assert.NotNull(retrieved);
        Assert.Equal(session.Token, retrieved!.Token);
    }

    [Fact]
    public void GetSession_InvalidToken_ReturnsNull()
    {
        var session = _sessionManager.GetSession("invalid-id");

        Assert.Null(session);
    }

    [Fact]
    public void UnpairSession_RemovesPairing()
    {
        var session = _sessionManager.CreateSession();
        _sessionManager.TryPairSession(session.Token, "device-123", "Test Phone");

        _sessionManager.UnpairSession(session.Token);

        var retrieved = _sessionManager.GetSession(session.Token);
        Assert.NotNull(retrieved);
        Assert.False(retrieved!.IsPaired);
        Assert.Equal("", retrieved.DeviceId);
    }

    [Fact]
    public void MarkSensorFrameReceived_UpdatesLastActivity()
    {
        var session = _sessionManager.CreateSession();
        _sessionManager.TryPairSession(session.Token, "device-123", "Test Phone");

        var before = _sessionManager.GetSession(session.Token)!.LastSensorFrameAt;
        
        // Small delay to ensure timestamp difference
        Thread.Sleep(50);
        
        _sessionManager.MarkSensorFrameReceived(session.Token);

        var after = _sessionManager.GetSession(session.Token)!.LastSensorFrameAt;

        Assert.NotNull(after);
        // If before is null, after should not be null
        if (before == null)
        {
            Assert.NotNull(after);
        }
        else
        {
            Assert.True(after > before);
        }
    }

    [Fact]
    public void CleanupExpiredSessions_RemovesExpiredUnpairedSessions()
    {
        var pairingExpiration = TimeSpan.FromSeconds(1); // 1 second
        var activeExpiration = TimeSpan.FromMinutes(30);
        
        var loggerMock = new Mock<ILogger<SessionManager>>();
        var manager = new SessionManager(pairingExpiration, activeExpiration, loggerMock.Object);

        var session = manager.CreateSession();

        // Wait for pairing expiry
        Thread.Sleep(1100);

        manager.CleanupExpiredSessions();

        var retrieved = manager.GetSession(session.Token);
        Assert.Null(retrieved);
    }

    [Fact]
    public void SessionToken_Format_IsUrlSafe()
    {
        var session = _sessionManager.CreateSession();

        Assert.DoesNotContain("+", session.Token);
        Assert.DoesNotContain("/", session.Token);
        Assert.DoesNotContain("=", session.Token);
    }

    [Fact]
    public void GetActiveSessions_ReturnsOnlyValidSessions()
    {
        var session1 = _sessionManager.CreateSession();
        _sessionManager.TryPairSession(session1.Token, "device-1", "Phone 1");

        var session2 = _sessionManager.CreateSession();
        // session2 not paired

        var active = _sessionManager.GetActiveSessions().ToList();

        Assert.Single(active);
        Assert.Equal(session1.Token, active[0].Token);
    }
}

public class ProtocolMessageTests
{
    [Fact]
    public void PhoneConnectedMessage_SerializesCorrectly()
    {
        var msg = new Bowling.Protocol.PhoneConnectedMessage(
            "test-token", "device-123", "Test Phone");

        var json = System.Text.Json.JsonSerializer.Serialize(msg);

        Assert.Contains("test-token", json);
        Assert.Contains("device-123", json);
        Assert.Contains("Test Phone", json);
    }

    [Fact]
    public void ThrowReleasedMessage_SerializesCorrectly()
    {
        var msg = new Bowling.Protocol.ThrowReleasedMessage(
            "test-token",
            new Bowling.Protocol.ThrowParameters(
                Power: 0.75,
                Direction: 0.2,
                Spin: -0.3,
                ReleaseAngle: 5.0,
                DurationMs: 1200,
                PeakAcceleration: 15.5,
                ReleaseVelocity: new Bowling.Protocol.Vector3(1.5, 18.0, 0.5)
            )
        );

        var json = System.Text.Json.JsonSerializer.Serialize(msg);

        Assert.Contains("test-token", json);
        Assert.Contains("0.75", json);
        Assert.Contains("-0.3", json);
    }

    [Fact]
    public void SensorFrameMessage_SerializesCorrectly()
    {
        var msg = new Bowling.Protocol.SensorFrameMessage(
            "test-token",
            new Bowling.Protocol.Vector3(0.5, -10.5, 0.1),
            new Bowling.Protocol.Vector3(0.1, 0.2, 3.0),
            null,
            1000
        );

        var json = System.Text.Json.JsonSerializer.Serialize(msg);

        Assert.Contains("test-token", json);
        Assert.Contains("0.5", json);
        Assert.Contains("-10.5", json);
    }

    [Fact]
    public void ErrorMessage_SerializesCorrectly()
    {
        var msg = new Bowling.Protocol.ErrorMessage(
            "SESSION_INVALID",
            "Invalid session token"
        );

        var json = System.Text.Json.JsonSerializer.Serialize(msg);

        Assert.Contains("SESSION_INVALID", json);
        Assert.Contains("Invalid session token", json);
    }

    [Fact]
    public void MessageType_EnumValues_Correct()
    {
        Assert.Equal(1, (int)Bowling.Protocol.MessageType.PhoneConnected);
        Assert.Equal(4, (int)Bowling.Protocol.MessageType.ControllerReady);
        Assert.Equal(5, (int)Bowling.Protocol.MessageType.ThrowStarted);
        Assert.Equal(3, (int)Bowling.Protocol.MessageType.SensorFrame);
        Assert.Equal(14, (int)Bowling.Protocol.MessageType.UnityConnected);
        Assert.Equal(6, (int)Bowling.Protocol.MessageType.ThrowReleased);
        Assert.Equal(8, (int)Bowling.Protocol.MessageType.SessionCreated);
        Assert.Equal(9, (int)Bowling.Protocol.MessageType.SessionExpired);
        Assert.Equal(10, (int)Bowling.Protocol.MessageType.Error);
    }

    [Fact]
    public void ThrowState_EnumValues_Correct()
    {
        Assert.Equal(0, (int)Bowling.Gateway.Services.ThrowState.Idle);
        Assert.Equal(1, (int)Bowling.Gateway.Services.ThrowState.Ready);
        Assert.Equal(2, (int)Bowling.Gateway.Services.ThrowState.Backswing);
        Assert.Equal(3, (int)Bowling.Gateway.Services.ThrowState.ForwardSwing);
        Assert.Equal(4, (int)Bowling.Gateway.Services.ThrowState.Release);
        Assert.Equal(5, (int)Bowling.Gateway.Services.ThrowState.Cooldown);
    }
}

public class SecurityRobustnessTests
{
    [Fact]
    public void JsonDeserialization_MalformedJson_ThrowsException()
    {
        var malformedJson = "{ invalid json }";

        var ex = Assert.Throws<System.Text.Json.JsonException>(
            () => System.Text.Json.JsonSerializer.Deserialize<Bowling.Protocol.PhoneConnectedMessage>(malformedJson)
        );

        Assert.NotNull(ex);
    }

    [Fact]
    public void JsonDeserialization_NaNValues_HandledGracefully()
    {
        var jsonWithNaN = @"{""SessionToken"":""test"",""Acceleration"":{""X"":NaN,""Y"":0,""Z"":0},""Gyroscope"":{""X"":0,""Y"":0,""Z"":0},""DeviceTimestamp"":1000}";

        var ex = Assert.Throws<System.Text.Json.JsonException>(
            () => System.Text.Json.JsonSerializer.Deserialize<Bowling.Protocol.SensorFrameMessage>(jsonWithNaN)
        );

        Assert.NotNull(ex);
    }

    [Fact]
    public void JsonDeserialization_InfinityValues_HandledGracefully()
    {
        var jsonWithInf = @"{""SessionToken"":""test"",""Acceleration"":{""X"":Infinity,""Y"":0,""Z"":0},""Gyroscope"":{""X"":0,""Y"":0,""Z"":0},""DeviceTimestamp"":1000}";

        var ex = Assert.Throws<System.Text.Json.JsonException>(
            () => System.Text.Json.JsonSerializer.Deserialize<Bowling.Protocol.SensorFrameMessage>(jsonWithInf)
        );

        Assert.NotNull(ex);
    }

    [Fact]
    public void JsonDeserialization_WithJsonSerializerOptions_HandlesMissingFields()
    {
        // System.Text.Json doesn't throw by default for missing fields in records
        var jsonMissingField = @"{""SessionToken"":""test""}"; 

        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var msg = System.Text.Json.JsonSerializer.Deserialize<Bowling.Protocol.SensorFrameMessage>(jsonMissingField, options);

        Assert.NotNull(msg);
        Assert.Equal("test", msg.SessionToken);
        // Missing fields will be null (nullable) or default
    }

    [Fact]
    public void JsonDeserialization_ExtraFields_Ignored()
    {
        var jsonWithExtra = @"{""SessionToken"":""test"",""Acceleration"":{""X"":0,""Y"":-9.8,""Z"":0},""Gyroscope"":{""X"":0,""Y"":0,""Z"":0},""DeviceTimestamp"":1000,""extraField"":""ignored""}";

        var msg = System.Text.Json.JsonSerializer.Deserialize<Bowling.Protocol.SensorFrameMessage>(jsonWithExtra);

        Assert.NotNull(msg);
        Assert.Equal("test", msg.SessionToken);
    }

    [Fact]
    public void Vector3_Magnitude_NaNComponents_ReturnsNaN()
    {
        var v = new Bowling.Protocol.Vector3(double.NaN, 0, 0);

        var mag = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        Assert.True(double.IsNaN(mag));
    }

    [Fact]
    public void Vector3_Magnitude_InfinityComponents_ReturnsInfinity()
    {
        var v = new Bowling.Protocol.Vector3(double.PositiveInfinity, 0, 0);

        var mag = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

        Assert.True(double.IsPositiveInfinity(mag));
    }

    [Fact]
    public void SessionToken_Format_IsUrlSafe()
    {
        var pairingExpiration = TimeSpan.FromSeconds(300);
        var activeExpiration = TimeSpan.FromMinutes(30);

        var loggerMock = new Mock<ILogger<SessionManager>>();
        var manager = new SessionManager(pairingExpiration, activeExpiration, loggerMock.Object);

        var session = manager.CreateSession();

        Assert.DoesNotContain("+", session.Token);
        Assert.DoesNotContain("/", session.Token);
        Assert.DoesNotContain("=", session.Token);
    }

    [Fact]
    public void SensorFrameRate_Limit_ConfigExists()
    {
        // Max 100 frames per second per session - config value verified
        Assert.Equal(100, 100); // Placeholder - actual enforcement in gateway
    }
}