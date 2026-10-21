namespace Bowling.Gateway.Configuration;

public class GatewayOptions
{
    public const string SectionName = "Gateway";
    
    public int Port { get; set; } = 5050;
    public int UnityPort { get; set; } = 5051;
    public int PairingExpirationSeconds { get; set; } = 60;
    public int ActiveExpirationMinutes { get; set; } = 30;
    public int MaxSensorFramesPerSecond { get; set; } = 100;
    public int SensorFrameBatchSize { get; set; } = 10;
    public string LogLevel { get; set; } = "Information";
}

public class ThrowDetectionOptions
{
    public const string SectionName = "ThrowDetection";
    
    public double IdleAccelerationThreshold { get; set; } = 1.5;
    public double BackswingAccelerationThreshold { get; set; } = 3.0;
    public double ForwardSwingAccelerationThreshold { get; set; } = 8.0;
    public double ReleaseGyroscopeThreshold { get; set; } = 2.5;
    public int MinBackswingDurationMs { get; set; } = 300;
    public int MaxBackswingDurationMs { get; set; } = 2000;
    public int MinForwardSwingDurationMs { get; set; } = 150;
    public int MaxForwardSwingDurationMs { get; set; } = 800;
    public int MaxTotalThrowDurationMs { get; set; } = 3000;
    public int SmoothingWindowSize { get; set; } = 5;
    public double LowPassFilterAlpha { get; set; } = 0.3;
    public double PowerScalingFactor { get; set; } = 0.12;
    public double PowerCurveFactor { get; set; } = 0.08;
    public double DirectionScalingFactor { get; set; } = 0.08;
    public double SpinScalingFactor { get; set; } = 0.15;
    public double MinReleaseAngle { get; set; } = -15.0;
    public double MaxReleaseAngle { get; set; } = 45.0;
    public double MinThrowPower { get; set; } = 0.15;
    public double MaxThrowPower { get; set; } = 1.0;
    public double MaxValidAcceleration { get; set; } = 50.0;
    public double MaxValidGyroscope { get; set; } = 20.0;
}