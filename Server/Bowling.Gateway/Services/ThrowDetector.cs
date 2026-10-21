namespace Bowling.Gateway.Services;

using Bowling.Protocol;
using Bowling.Gateway.Configuration;
using Microsoft.Extensions.Options;
using System.IO;

public enum ThrowState
{
    Idle,
    Ready,
    Backswing,
    ForwardSwing,
    Release,
    Cooldown
}

public sealed class SensorFrame
{
    public long Timestamp { get; set; }
    public Vector3 Acceleration { get; set; } = new(0, 0, 0);
    public Vector3 Gyroscope { get; set; } = new(0, 0, 0);
    public Quaternion? Orientation { get; set; }
    public Vector3 LinearAcceleration { get; set; } = new(0, 0, 0);
    public double AccelerationMagnitude { get; set; }
    public double GyroscopeMagnitude { get; set; }
}

public sealed class ThrowResult
{
    public bool IsValid { get; set; }
    public ThrowParameters? Parameters { get; set; }
    public string? RejectionReason { get; set; }
    public ThrowState FinalState { get; set; }
    public long DurationMs { get; set; }
}

public interface IThrowDetector
{
    ThrowResult ProcessFrame(SensorFrame frame);
    void Reset();
    ThrowState CurrentState { get; }
}

public sealed class ThrowDetector : IThrowDetector
{
    private readonly ThrowDetectionOptions _options;
    private readonly ILogger<ThrowDetector> _logger;
    
    private ThrowState _state = ThrowState.Idle;
    private readonly Queue<SensorFrame> _frameBuffer = new();
    private readonly List<SensorFrame> _throwFrames = new();
    private long _stateStartTime = 0;
    private long _throwStartTime = 0;
    private Vector3 _gravityVector = new(0, -1, 0);
    private bool _gravityInitialized = false;
    private double _peakAcceleration = 0;
    private Vector3 _peakAccelerationVector = new(0, 0, 0);
    private double _peakGyroscope = 0;
    private double _peakGyroscopeZ = 0;  // Signed peak gyroscope Z for spin direction
    private long _lastReleaseTime = 0;

    public ThrowState CurrentState => _state;

    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "detector_debug.log");

    public ThrowDetector(IOptions<ThrowDetectionOptions> options, ILogger<ThrowDetector> logger)
    {
        _options = options.Value;
        _logger = logger;
        File.WriteAllText(LogPath, $"[Detector] Started at {DateTime.UtcNow:O}\n");
    }

    public ThrowResult ProcessFrame(SensorFrame frame)
    {
        var result = new ThrowResult { FinalState = _state };
        
        File.AppendAllText(LogPath, $"[Detector] ProcessFrame: state={_state}, timestamp={frame.Timestamp}\n");

        if (!_gravityInitialized)
        {
            InitializeGravity(frame);
            return result;
        }

        frame.LinearAcceleration = SubtractVectors(frame.Acceleration, MultiplyVector(_gravityVector, ProtocolConstants.Gravity));
        frame.AccelerationMagnitude = Magnitude(frame.LinearAcceleration);
        frame.GyroscopeMagnitude = Magnitude(frame.Gyroscope);

        if (frame.AccelerationMagnitude > _peakAcceleration)
        {
            _peakAcceleration = frame.AccelerationMagnitude;
            _peakAccelerationVector = frame.LinearAcceleration;
        }

        if (frame.GyroscopeMagnitude > _peakGyroscope)
        {
            _peakGyroscope = frame.GyroscopeMagnitude;
        }

        // Track signed peak gyroscope Z for spin direction
        if (Math.Abs(frame.Gyroscope.Z) > Math.Abs(_peakGyroscopeZ))
        {
            _peakGyroscopeZ = frame.Gyroscope.Z;
        }

        _frameBuffer.Enqueue(frame);
        if (_frameBuffer.Count > _options.SmoothingWindowSize)
            _frameBuffer.Dequeue();

        var smoothedAccel = SmoothAcceleration();
        var smoothedGyro = SmoothGyroscope();
        
        File.AppendAllText(LogPath, $"[Detector] smoothed: accel=({smoothedAccel.X:F3},{smoothedAccel.Y:F3},{smoothedAccel.Z:F3}), gyro=({smoothedGyro.X:F3},{smoothedGyro.Y:F3},{smoothedGyro.Z:F3})\n");

        var now = frame.Timestamp;
        var timeInState = now - _stateStartTime;
        var totalThrowTime = _throwStartTime > 0 ? now - _throwStartTime : 0;

        switch (_state)
        {
            case ThrowState.Idle:
                result = HandleIdleState(smoothedAccel, smoothedGyro, timeInState, now);
                break;
            case ThrowState.Ready:
                result = HandleReadyState(smoothedAccel, smoothedGyro, timeInState, now);
                break;
            case ThrowState.Backswing:
                result = HandleBackswingState(smoothedAccel, smoothedGyro, timeInState, totalThrowTime, now);
                break;
            case ThrowState.ForwardSwing:
                result = HandleForwardSwingState(smoothedAccel, smoothedGyro, timeInState, totalThrowTime, now);
                break;
            case ThrowState.Release:
                result = HandleReleaseState(smoothedAccel, smoothedGyro, timeInState, totalThrowTime, now);
                break;
            case ThrowState.Cooldown:
                result = HandleCooldownState(timeInState, now);
                break;
        }

        result.FinalState = _state;
        return result;
    }

    private ThrowResult HandleIdleState(Vector3 smoothedAccel, Vector3 smoothedGyro, long timeInState, long now)
    {
        var result = new ThrowResult { FinalState = _state };
        
        // Ready detection: phone held upright with stable orientation
        // Check for stable near-zero linear acceleration (phone at rest) + low gyro
        var accelMagnitude = Magnitude(smoothedAccel);
        var gyroMagnitude = Magnitude(smoothedGyro);
        
        _logger.LogDebug("IDLE: accelMag={AccelMag:F3}, gyroMag={GyroMag:F3}, smoothedAccel=({X:F3},{Y:F3},{Z:F3}), threshold={Threshold:F3}",
            accelMagnitude, gyroMagnitude, smoothedAccel.X, smoothedAccel.Y, smoothedAccel.Z, _options.IdleAccelerationThreshold);
        
        if (accelMagnitude < _options.IdleAccelerationThreshold && gyroMagnitude < 0.5)
        {
            TransitionTo(ThrowState.Ready, now);
            _logger.LogInformation("Throw detector: IDLE -> READY (stable pose, accelMag={AccelMag:F2}, gyroMag={GyroMag:F2})", 
                accelMagnitude, gyroMagnitude);
        }
        
        return result;
    }

    private ThrowResult HandleReadyState(Vector3 smoothedAccel, Vector3 smoothedGyro, long timeInState, long now)
    {
        var result = new ThrowResult { FinalState = _state };
        
        // Backswing detection: negative Y linear acceleration (phone moving backward)
        if (smoothedAccel.Y < -_options.BackswingAccelerationThreshold)
        {
            TransitionTo(ThrowState.Backswing, now);
            _throwStartTime = now;
            _peakAcceleration = 0;
            _peakGyroscope = 0;
            _throwFrames.Clear();
            _logger.LogInformation("Throw detector: READY -> BACKSWING (accelY={AccelY:F2})", smoothedAccel.Y);
        }
        // Lost ready pose: acceleration magnitude too high or orientation changed
        else if (Magnitude(smoothedAccel) > _options.IdleAccelerationThreshold * 2 || Magnitude(smoothedGyro) > 1.0)
        {
            TransitionTo(ThrowState.Idle, now);
            _logger.LogInformation("Throw detector: READY -> IDLE (lost stable pose)");
        }
        
        return result;
    }

    private ThrowResult HandleBackswingState(Vector3 smoothedAccel, Vector3 smoothedGyro, long timeInState, long totalThrowTime, long now)
    {
        var result = new ThrowResult { FinalState = _state };
        _throwFrames.Add(new SensorFrame 
        { 
            Timestamp = now, 
            Acceleration = smoothedAccel, 
            Gyroscope = smoothedGyro,
            LinearAcceleration = smoothedAccel,
            AccelerationMagnitude = Magnitude(smoothedAccel),
            GyroscopeMagnitude = Magnitude(smoothedGyro)
        });

        if (timeInState > _options.MaxBackswingDurationMs)
        {
            TransitionTo(ThrowState.Idle, now);
            _logger.LogInformation("Throw detector: BACKSWING -> IDLE (timeout)");
            return result;
        }

        if (smoothedAccel.Y > _options.ForwardSwingAccelerationThreshold && timeInState >= _options.MinBackswingDurationMs)
        {
            TransitionTo(ThrowState.ForwardSwing, now);
            _logger.LogInformation("Throw detector: BACKSWING -> FORWARD_SWING");
        }
        
        return result;
    }

    private ThrowResult HandleForwardSwingState(Vector3 smoothedAccel, Vector3 smoothedGyro, long timeInState, long totalThrowTime, long now)
    {
        var result = new ThrowResult { FinalState = _state };
        _throwFrames.Add(new SensorFrame 
        { 
            Timestamp = now, 
            Acceleration = smoothedAccel, 
            Gyroscope = smoothedGyro,
            LinearAcceleration = smoothedAccel,
            AccelerationMagnitude = Magnitude(smoothedAccel),
            GyroscopeMagnitude = Magnitude(smoothedGyro)
        });

        if (totalThrowTime > _options.MaxTotalThrowDurationMs)
        {
            TransitionTo(ThrowState.Idle, now);
            _logger.LogInformation("Throw detector: FORWARD_SWING -> IDLE (total timeout)");
            return result;
        }

        if (timeInState > _options.MaxForwardSwingDurationMs)
        {
            TransitionTo(ThrowState.Idle, now);
            _logger.LogInformation("Throw detector: FORWARD_SWING -> IDLE (phase timeout)");
            return result;
        }

        var forwardAccel = smoothedAccel.Y;
        var gyroMagnitude = Magnitude(smoothedGyro);
        
        if (forwardAccel < _options.ForwardSwingAccelerationThreshold * 0.3 && 
            gyroMagnitude > _options.ReleaseGyroscopeThreshold &&
            timeInState >= _options.MinForwardSwingDurationMs)
        {
            TransitionTo(ThrowState.Release, now);
            _logger.LogInformation("Throw detector: FORWARD_SWING -> RELEASE");
        }
        
        return result;
    }

    private ThrowResult HandleReleaseState(Vector3 smoothedAccel, Vector3 smoothedGyro, long timeInState, long totalThrowTime, long now)
    {
        var result = new ThrowResult { FinalState = _state };
        
        var parameters = CalculateThrowParameters();
        
        if (parameters.Power >= _options.MinThrowPower && parameters.Power <= _options.MaxThrowPower)
        {
            result.IsValid = true;
            result.Parameters = parameters;
            result.DurationMs = totalThrowTime;
            _logger.LogInformation("Throw detected: Power={Power:F2}, Direction={Direction:F2}, Spin={Spin:F2}, Angle={Angle:F1}",
                parameters.Power, parameters.Direction, parameters.Spin, parameters.ReleaseAngle);
        }
        else
        {
            result.IsValid = false;
            result.RejectionReason = $"Invalid power: {parameters.Power:F2} (min: {_options.MinThrowPower})";
            _logger.LogInformation("Throw rejected: {Reason}", result.RejectionReason);
        }

        TransitionTo(ThrowState.Cooldown, now);
        _lastReleaseTime = now;
        return result;
    }

    private ThrowResult HandleCooldownState(long timeInState, long now)
    {
        var result = new ThrowResult { FinalState = _state };
        
        if (timeInState > 1000)
        {
            TransitionTo(ThrowState.Idle, now);
            _logger.LogInformation("Throw detector: COOLDOWN -> IDLE");
        }
        
        return result;
    }

    private void TransitionTo(ThrowState newState, long now)
    {
        _state = newState;
        _stateStartTime = now;
    }

    private void InitializeGravity(SensorFrame frame)
    {
        var mag = Magnitude(frame.Acceleration);
        if (mag > ProtocolConstants.Gravity * 0.8 && mag < ProtocolConstants.Gravity * 1.2)
        {
            _gravityVector = Normalize(frame.Acceleration);
            _gravityInitialized = true;
            _logger.LogDebug("Gravity initialized: ({X:F2}, {Y:F2}, {Z:F2})", _gravityVector.X, _gravityVector.Y, _gravityVector.Z);
        }
    }

    private Vector3 SmoothAcceleration()
    {
        if (_frameBuffer.Count == 0) return new Vector3(0, 0, 0);
        
        var sum = new Vector3(0, 0, 0);
        foreach (var frame in _frameBuffer)
        {
            sum = AddVectors(sum, frame.LinearAcceleration);
        }
        return DivideVector(sum, _frameBuffer.Count);
    }

    private Vector3 SmoothGyroscope()
    {
        if (_frameBuffer.Count == 0) return new Vector3(0, 0, 0);
        
        var sum = new Vector3(0, 0, 0);
        foreach (var frame in _frameBuffer)
        {
            sum = AddVectors(sum, frame.Gyroscope);
        }
        return DivideVector(sum, _frameBuffer.Count);
    }

    private ThrowParameters CalculateThrowParameters()
    {
        var forwardAccel = _peakAccelerationVector.Y;
        var lateralAccel = _peakAccelerationVector.X;
        var verticalAccel = _peakAccelerationVector.Z;

        // Power: based on forward acceleration magnitude with better dynamic range
        // Use a non-linear scaling that maps typical bowling swings to 0.2-1.0 range
        // Avoid saturation at 1.0 for normal throws
        var rawPower = forwardAccel * _options.PowerScalingFactor;
        // Apply smooth curve: power = 1 - exp(-k * rawPower) gives better distribution
        var power = 1.0 - Math.Exp(-_options.PowerCurveFactor * rawPower);
        power = Math.Clamp(power, _options.MinThrowPower, _options.MaxThrowPower);
        
        // Direction: launch direction from lateral acceleration (left/right)
        var direction = Math.Clamp(lateralAccel * _options.DirectionScalingFactor, -1.0, 1.0);
        
        // Spin: hook/wrist rotation from peak gyroscope Z (signed)
        var spin = Math.Clamp(_peakGyroscopeZ * _options.SpinScalingFactor, -1.0, 1.0);

        var releaseAngle = Math.Atan2(verticalAccel, forwardAccel) * 180.0 / Math.PI;
        releaseAngle = Math.Clamp(releaseAngle, _options.MinReleaseAngle, _options.MaxReleaseAngle);

        var releaseVelocity = new Vector3(
            lateralAccel * _options.PowerScalingFactor,
            forwardAccel * _options.PowerScalingFactor,
            verticalAccel * _options.PowerScalingFactor
        );

        return new ThrowParameters(
            Power: power,
            Direction: direction,
            Spin: spin,
            ReleaseAngle: releaseAngle,
            DurationMs: 0,
            PeakAcceleration: _peakAcceleration,
            ReleaseVelocity: releaseVelocity
        );
    }

    public void Reset()
    {
        _state = ThrowState.Idle;
        _frameBuffer.Clear();
        _throwFrames.Clear();
        _stateStartTime = 0;
        _throwStartTime = 0;
        _peakAcceleration = 0;
        _peakGyroscope = 0;
        _peakGyroscopeZ = 0;
        _peakAccelerationVector = new Vector3(0, 0, 0);
        _gravityInitialized = false;
        _logger.LogDebug("Throw detector reset");
    }

    private static double Magnitude(Vector3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    
    private static Vector3 Normalize(Vector3 v)
    {
        var mag = Magnitude(v);
        return mag > 0 ? new Vector3(v.X / mag, v.Y / mag, v.Z / mag) : new Vector3(0, -1, 0);
    }

    private static Vector3 AddVectors(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static Vector3 SubtractVectors(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static Vector3 DivideVector(Vector3 a, double d) => d > 0 ? new Vector3(a.X / d, a.Y / d, a.Z / d) : new Vector3(0, 0, 0);
    private static Vector3 MultiplyVector(Vector3 a, double d) => new(a.X * d, a.Y * d, a.Z * d);
}