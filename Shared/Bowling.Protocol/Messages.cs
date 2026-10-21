namespace Bowling.Protocol;

public enum MessageType
{
    Unknown = 0,
    PhoneConnected = 1,
    PhoneDisconnected = 2,
    SensorFrame = 3,
    ControllerReady = 4,
    ThrowStarted = 5,
    ThrowReleased = 6,
    ThrowParameters = 7,
    SessionCreated = 8,
    SessionExpired = 9,
    Error = 10,
    Ping = 11,
    Pong = 12,
    GatewayInfo = 13,
    UnityConnected = 14,
    UnityDisconnected = 15,
    ResetLane = 16,
    ThrowComplete = 17
}

public abstract record MessageBase
{
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public record PhoneConnectedMessage(
    string SessionToken,
    string DeviceId,
    string DeviceName
) : MessageBase
{
    public MessageType Type => MessageType.PhoneConnected;
}

public record PhoneDisconnectedMessage(
    string SessionToken,
    string Reason
) : MessageBase
{
    public MessageType Type => MessageType.PhoneDisconnected;
}

public record SensorFrameMessage(
    string SessionToken,
    Vector3 Acceleration,
    Vector3 Gyroscope,
    Quaternion? Orientation = null,
    long DeviceTimestamp = 0
) : MessageBase
{
    public MessageType Type => MessageType.SensorFrame;
}

public record Vector3(
    double X,
    double Y,
    double Z
);

public record Quaternion(
    double X,
    double Y,
    double Z,
    double W
);

public record ControllerReadyMessage(
    string SessionToken
) : MessageBase
{
    public MessageType Type => MessageType.ControllerReady;
}

public record ThrowStartedMessage(
    string SessionToken,
    long StartTimestamp
) : MessageBase
{
    public MessageType Type => MessageType.ThrowStarted;
}

public record ThrowReleasedMessage(
    string SessionToken,
    ThrowParameters Parameters
) : MessageBase
{
    public MessageType Type => MessageType.ThrowReleased;
}

public record ThrowParameters(
    double Power,
    double Direction,
    double Spin,
    double ReleaseAngle,
    long DurationMs,
    double PeakAcceleration,
    Vector3 ReleaseVelocity
) : MessageBase
{
    public MessageType Type => MessageType.ThrowParameters;
};

public record SessionCreatedMessage(
    string SessionToken,
    string GatewayUrl,
    int Port,
    long PairingExpiresAt,
    long ActiveExpiresAt
) : MessageBase
{
    public MessageType Type => MessageType.SessionCreated;
}

public record SessionExpiredMessage(
    string SessionToken
) : MessageBase
{
    public MessageType Type => MessageType.SessionExpired;
}

public record ErrorMessage(
    string Code,
    string Description
) : MessageBase
{
    public MessageType Type => MessageType.Error;
}

public record PingMessage() : MessageBase
{
    public MessageType Type => MessageType.Ping;
}

public record PongMessage() : MessageBase
{
    public MessageType Type => MessageType.Pong;
}

public record GatewayInfoMessage(
    string IpAddress,
    int Port,
    string Version,
    int ConnectedPhones,
    bool UnityConnected
) : MessageBase
{
    public MessageType Type => MessageType.GatewayInfo;
}

public record UnityConnectedMessage() : MessageBase
{
    public MessageType Type => MessageType.UnityConnected;
}

public record UnityDisconnectedMessage() : MessageBase
{
    public MessageType Type => MessageType.UnityDisconnected;
}

public record ResetLaneMessage() : MessageBase
{
    public MessageType Type => MessageType.ResetLane;
}

public record ThrowCompleteMessage(
    int PinsKnockedDown,
    double BallFinalPositionZ
) : MessageBase
{
    public MessageType Type => MessageType.ThrowComplete;
}