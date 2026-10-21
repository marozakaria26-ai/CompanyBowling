using Bowling.Gateway.Services;
using Bowling.Gateway.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Bowling.Gateway.Tests;

public class ThrowDetectorCalculationTests
{
    private readonly ThrowDetectionOptions _options;

    public ThrowDetectorCalculationTests()
    {
        _options = new ThrowDetectionOptions
        {
            IdleAccelerationThreshold = 2.0,
            BackswingAccelerationThreshold = 3.0,
            ForwardSwingAccelerationThreshold = 8.0,
            ReleaseGyroscopeThreshold = 2.5,
            MinBackswingDurationMs = 300,
            MaxBackswingDurationMs = 2000,
            MinForwardSwingDurationMs = 150,
            MaxForwardSwingDurationMs = 800,
            MaxTotalThrowDurationMs = 3000,
            PowerScalingFactor = 0.12,
            DirectionScalingFactor = 0.08,
            SpinScalingFactor = 0.15,
            PowerCurveFactor = 0.5,
            MinThrowPower = 0.15,
            MaxThrowPower = 1.0,
            MinReleaseAngle = -15.0,
            MaxReleaseAngle = 45.0,
            SmoothingWindowSize = 5
        };
    }

    [Fact]
    public void PowerFormula_ExponentialCurve_ProducesCorrectRange()
    {
        // Test the power formula: power = 1 - exp(-k * rawPower)
        // rawPower = forwardAccel * PowerScalingFactor
        // For forwardAccel = 9.0 (linear), rawPower = 9.0 * 0.12 = 1.08
        // power = 1 - exp(-0.5 * 1.08) = 1 - exp(-0.54) = 1 - 0.583 = 0.417
        
        var forwardAccel = 9.0;
        var rawPower = forwardAccel * _options.PowerScalingFactor;
        var power = 1.0 - Math.Exp(-_options.PowerCurveFactor * rawPower);
        
        Assert.InRange(power, 0.3, 0.55);
    }

    [Fact]
    public void PowerFormula_StrongThrow_ApproachesMax()
    {
        // Strong throw: forwardAccel = 20.0
        var forwardAccel = 20.0;
        var rawPower = forwardAccel * _options.PowerScalingFactor;
        var power = 1.0 - Math.Exp(-_options.PowerCurveFactor * rawPower);
        
        Assert.InRange(power, 0.6, 0.95);
    }

    [Fact]
    public void PowerFormula_SoftThrow_AboveMin()
    {
        // Soft throw: forwardAccel = 3.0
        var forwardAccel = 3.0;
        var rawPower = forwardAccel * _options.PowerScalingFactor;
        var power = 1.0 - Math.Exp(-_options.PowerCurveFactor * rawPower);
        
        Assert.True(power >= _options.MinThrowPower);
    }

    [Fact]
    public void DirectionFormula_LateralAccel_Clamped()
    {
        // Direction = lateralAccel * DirectionScalingFactor, clamped to [-1, 1]
        var lateralAccel = 10.0; // Strong rightward
        var direction = Math.Clamp(lateralAccel * _options.DirectionScalingFactor, -1.0, 1.0);
        
        Assert.True(direction > 0 && direction <= 1.0);
    }

    [Fact]
    public void DirectionFormula_NegativeLateralAccel_NegativeDirection()
    {
        var lateralAccel = -10.0; // Strong leftward
        var direction = Math.Clamp(lateralAccel * _options.DirectionScalingFactor, -1.0, 1.0);
        
        Assert.True(direction < 0 && direction >= -1.0);
    }

    [Fact]
    public void SpinFormula_SignedGyroZ_Clamped()
    {
        // Spin = gyroZ * SpinScalingFactor, clamped to [-1, 1]
        var gyroZ = 8.0; // Strong right spin
        var spin = Math.Clamp(gyroZ * _options.SpinScalingFactor, -1.0, 1.0);
        
        Assert.True(spin > 0 && spin <= 1.0);
    }

    [Fact]
    public void SpinFormula_NegativeGyroZ_NegativeSpin()
    {
        var gyroZ = -8.0; // Strong left spin
        var spin = Math.Clamp(gyroZ * _options.SpinScalingFactor, -1.0, 1.0);
        
        Assert.True(spin < 0 && spin >= -1.0);
    }

    [Fact]
    public void ReleaseAngle_Atan2_CalculatedCorrectly()
    {
        var verticalAccel = 2.0;
        var forwardAccel = 9.0;
        var releaseAngle = Math.Atan2(verticalAccel, forwardAccel) * 180.0 / Math.PI;
        
        Assert.InRange(releaseAngle, 10.0, 15.0); // ~12.5 degrees
    }
}

public class ThrowDetectorStateMachineTests
{
    private readonly ThrowDetectionOptions _options;
    private readonly ThrowDetector _detector;

    public ThrowDetectorStateMachineTests()
    {
        _options = new ThrowDetectionOptions
        {
            IdleAccelerationThreshold = 2.0,
            BackswingAccelerationThreshold = 3.0,
            ForwardSwingAccelerationThreshold = 8.0,
            ReleaseGyroscopeThreshold = 2.5,
            MinBackswingDurationMs = 300,
            MaxBackswingDurationMs = 2000,
            MinForwardSwingDurationMs = 150,
            MaxForwardSwingDurationMs = 800,
            MaxTotalThrowDurationMs = 3000,
            PowerScalingFactor = 0.12,
            DirectionScalingFactor = 0.08,
            SpinScalingFactor = 0.15,
            PowerCurveFactor = 0.5,
            MinThrowPower = 0.15,
            MaxThrowPower = 1.0,
            MinReleaseAngle = -15.0,
            MaxReleaseAngle = 45.0,
            SmoothingWindowSize = 5
        };

        var loggerMock = new Mock<ILogger<ThrowDetector>>();
        _detector = new ThrowDetector(Options.Create(_options), loggerMock.Object);
    }

    [Fact]
    public void InitialState_IsIdle()
    {
        Assert.Equal(ThrowState.Idle, _detector.CurrentState);
    }

    [Fact]
    public void Reset_ReturnsToIdle()
    {
        // Feed some frames
        for (int i = 0; i < 5; i++)
        {
            _detector.ProcessFrame(CreateFrame(i * 16, 0, -9.80665, 0, 0, 0, 0));
        }
        
        _detector.Reset();
        
        Assert.Equal(ThrowState.Idle, _detector.CurrentState);
    }

    [Fact]
    public void GravityInitialization_Works()
    {
        // First frame initializes gravity
        var result = _detector.ProcessFrame(CreateFrame(0, 0, -9.80665, 0, 0, 0, 0));
        Assert.Equal(ThrowState.Idle, _detector.CurrentState); // Returns early
        
        // Second frame should process normally - stable pose -> Ready
        result = _detector.ProcessFrame(CreateFrame(16, 0, -9.80665, 0, 0, 0, 0));
        Assert.Equal(ThrowState.Ready, _detector.CurrentState); // Stable pose detected
    }

    private static SensorFrame CreateFrame(long timestamp, double ax, double ay, double az, double gx, double gy, double gz)
    {
        return new SensorFrame
        {
            Timestamp = timestamp,
            Acceleration = new Bowling.Protocol.Vector3(ax, ay, az),
            Gyroscope = new Bowling.Protocol.Vector3(gx, gy, gz)
        };
    }
}