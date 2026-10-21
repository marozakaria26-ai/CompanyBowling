namespace Bowling.Protocol;

public static class ProtocolConstants
{
    public const int DefaultGatewayPort = 5050;
    public const int DefaultUnityPort = 5051;
    
    public const string WebSocketPath = "/ws";
    public const string UnityWebSocketPath = "/unity";
    
    public const long SessionExpirationMs = 60_000;
    public const long ThrowCooldownMs = 3_000;
    
    public const int TargetSensorFrequencyHz = 60;
    public const int SensorFrameBatchSize = 10;
    
    public const double MinThrowPower = 0.15;
    public const double MaxThrowPower = 1.0;
    public const double MaxValidAcceleration = 50.0;
    public const double MaxValidGyroscope = 20.0;
    
    public const double Gravity = 9.80665;
}

public static class ThrowDetectionConstants
{
    public const double IdleAccelerationThreshold = 1.5;
    public const double BackswingAccelerationThreshold = 3.0;
    public const double ForwardSwingAccelerationThreshold = 8.0;
    public const double ReleaseGyroscopeThreshold = 2.5;
    
    public const int MinBackswingDurationMs = 300;
    public const int MaxBackswingDurationMs = 2_000;
    public const int MinForwardSwingDurationMs = 150;
    public const int MaxForwardSwingDurationMs = 800;
    public const int MaxTotalThrowDurationMs = 3_000;
    
    public const int SmoothingWindowSize = 5;
    public const double LowPassFilterAlpha = 0.3;
    
    public const double PowerScalingFactor = 0.12;
    public const double DirectionScalingFactor = 0.08;
    public const double SpinScalingFactor = 0.15;
    
    public const double MinReleaseAngle = -15.0;
    public const double MaxReleaseAngle = 45.0;
}