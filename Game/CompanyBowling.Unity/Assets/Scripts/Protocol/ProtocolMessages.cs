namespace CompanyBowling.Protocol
{
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

    [System.Serializable]
    public abstract class MessageBase
    {
        public MessageType type;
        public long timestamp;

        protected MessageBase(MessageType type)
        {
            this.type = type;
            this.timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    [System.Serializable]
    public class PhoneConnectedMessage : MessageBase
    {
        public string sessionToken;
        public string deviceId;
        public string deviceName;

        public PhoneConnectedMessage() : base(MessageType.PhoneConnected) { }
    }

    [System.Serializable]
    public class PhoneDisconnectedMessage : MessageBase
    {
        public string sessionToken;
        public string reason;

        public PhoneDisconnectedMessage() : base(MessageType.PhoneDisconnected) { }
    }

    [System.Serializable]
    public class SensorFrameMessage : MessageBase
    {
        public string sessionToken;
        public Vector3 acceleration;
        public Vector3 gyroscope;
        public Quaternion orientation;
        public long deviceTimestamp;

        public SensorFrameMessage() : base(MessageType.SensorFrame) { }
    }

    [System.Serializable]
    public class Vector3
    {
        public double x;
        public double y;
        public double z;

        public Vector3() { }
        public Vector3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        
        public static implicit operator UnityEngine.Vector3(Vector3 v) => new UnityEngine.Vector3((float)v.x, (float)v.y, (float)v.z);
        public static implicit operator Vector3(UnityEngine.Vector3 v) => new Vector3(v.x, v.y, v.z);
    }

    [System.Serializable]
    public class Quaternion
    {
        public double x;
        public double y;
        public double z;
        public double w;

        public Quaternion() { }
        public Quaternion(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        
        public static implicit operator UnityEngine.Quaternion(Quaternion q) => new UnityEngine.Quaternion((float)q.x, (float)q.y, (float)q.z, (float)q.w);
    }

    [System.Serializable]
    public class ControllerReadyMessage : MessageBase
    {
        public string sessionToken;

        public ControllerReadyMessage() : base(MessageType.ControllerReady) { }
    }

    [System.Serializable]
    public class ThrowStartedMessage : MessageBase
    {
        public string sessionToken;
        public long startTimestamp;

        public ThrowStartedMessage() : base(MessageType.ThrowStarted) { }
    }

    [System.Serializable]
    public class ThrowReleasedMessage : MessageBase
    {
        public string sessionToken;
        public ThrowParameters parameters;

        public ThrowReleasedMessage() : base(MessageType.ThrowReleased) { }
    }

    [System.Serializable]
    public class ThrowParameters
    {
        public double power;
        public double direction;
        public double spin;
        public double releaseAngle;
        public long durationMs;
        public double peakAcceleration;
        public Vector3 releaseVelocity;
    }

    [System.Serializable]
    public class SessionCreatedMessage : MessageBase
    {
        public string sessionToken;
        public string gatewayUrl;
        public int port;
        public long expiresAt;

        public SessionCreatedMessage() : base(MessageType.SessionCreated) { }
    }

    [System.Serializable]
    public class SessionExpiredMessage : MessageBase
    {
        public string sessionToken;

        public SessionExpiredMessage() : base(MessageType.SessionExpired) { }
    }

    [System.Serializable]
    public class ErrorMessage : MessageBase
    {
        public string code;
        public string description;

        public ErrorMessage() : base(MessageType.Error) { }
    }

    [System.Serializable]
    public class PingMessage : MessageBase
    {
        public PingMessage() : base(MessageType.Ping) { }
    }

    [System.Serializable]
    public class PongMessage : MessageBase
    {
        public PongMessage() : base(MessageType.Pong) { }
    }

    [System.Serializable]
    public class GatewayInfoMessage : MessageBase
    {
        public string ipAddress;
        public int port;
        public string version;
        public int connectedPhones;
        public bool unityConnected;

        public GatewayInfoMessage() : base(MessageType.GatewayInfo) { }
    }

    [System.Serializable]
    public class UnityConnectedMessage : MessageBase
    {
        public UnityConnectedMessage() : base(MessageType.UnityConnected) { }
    }

    [System.Serializable]
    public class UnityDisconnectedMessage : MessageBase
    {
        public UnityDisconnectedMessage() : base(MessageType.UnityDisconnected) { }
    }

    [System.Serializable]
    public class ResetLaneMessage : MessageBase
    {
        public ResetLaneMessage() : base(MessageType.ResetLane) { }
    }

    [System.Serializable]
    public class ThrowCompleteMessage : MessageBase
    {
        public int pinsKnockedDown;
        public double ballFinalPositionZ;

        public ThrowCompleteMessage() : base(MessageType.ThrowComplete) { }
    }
}