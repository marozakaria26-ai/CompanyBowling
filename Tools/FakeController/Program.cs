namespace CompanyBowling.FakeController;

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bowling.Protocol;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Company Bowling Fake Controller - Simulates phone sensor data");

        var hostOption = new Option<string>("--host", "Gateway host") { IsRequired = false };
        hostOption.AddAlias("-h");
        hostOption.SetDefaultValue("127.0.0.1");

        var portOption = new Option<int>("--port", "Gateway port") { IsRequired = false };
        portOption.AddAlias("-p");
        portOption.SetDefaultValue(5050);

        var tokenOption = new Option<string>("--token", "Session token") { IsRequired = true };
        tokenOption.AddAlias("-t");

        var throwTypeOption = new Option<ThrowType>("--throw", "Throw type to simulate") { IsRequired = false };
        throwTypeOption.AddAlias("-T");
        throwTypeOption.SetDefaultValue(ThrowType.Normal);

        var continuousOption = new Option<bool>("--continuous", "Stream continuous sensor data") { IsRequired = false };
        continuousOption.AddAlias("-c");
        continuousOption.SetDefaultValue(false);

        var durationOption = new Option<int>("--duration", "Duration in seconds for continuous mode") { IsRequired = false };
        durationOption.AddAlias("-d");
        durationOption.SetDefaultValue(10);

        rootCommand.AddOption(hostOption);
        rootCommand.AddOption(portOption);
        rootCommand.AddOption(tokenOption);
        rootCommand.AddOption(throwTypeOption);
        rootCommand.AddOption(continuousOption);
        rootCommand.AddOption(durationOption);

        rootCommand.SetHandler(async (InvocationContext ctx) =>
        {
            var host = ctx.ParseResult.GetValueForOption(hostOption);
            var port = ctx.ParseResult.GetValueForOption(portOption);
            var token = ctx.ParseResult.GetValueForOption(tokenOption);
            var throwType = ctx.ParseResult.GetValueForOption(throwTypeOption);
            var continuous = ctx.ParseResult.GetValueForOption(continuousOption);
            var duration = ctx.ParseResult.GetValueForOption(durationOption);

            await RunFakeController(host, port, token, throwType, continuous, duration);
        });

        return await rootCommand.InvokeAsync(args);
    }

    static async Task RunFakeController(string host, int port, string token, ThrowType throwType, bool continuous, int duration)
    {
        var url = $"ws://{host}:{port}/ws";
        Console.WriteLine($"[FakeController] Connecting to {url}");
        Console.WriteLine($"[FakeController] Session token: {token}");
        Console.WriteLine($"[FakeController] Throw type: {throwType}");

        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

        try
        {
            await ws.ConnectAsync(new Uri(url), CancellationToken.None);
            Console.WriteLine("[FakeController] Connected!");

            // Register phone
            var registerMsg = new PhoneConnectedMessage(token, "FAKE_CONTROLLER", "Fake Controller");
            await SendMessage(ws, registerMsg);
            Console.WriteLine("[FakeController] Sent registration");

            // Wait for session created response
            var buffer = new byte[4096];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Console.WriteLine($"[FakeController] Received: {response}");

            if (continuous)
            {
                await RunContinuousMode(ws, token, duration);
            }
            else
            {
                await RunThrowSimulation(ws, token, throwType);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FakeController] Error: {ex.Message}");
        }
    }

    static async Task RunContinuousMode(ClientWebSocket ws, string token, int durationSeconds)
    {
        Console.WriteLine($"[FakeController] Streaming sensor data for {durationSeconds}s...");
        var startTime = DateTime.UtcNow;
        var random = new Random();
        long frameCount = 0;

        while ((DateTime.UtcNow - startTime).TotalSeconds < durationSeconds)
        {
            // Generate realistic sensor data (phone held upright, slight movement)
            var accel = new Vector3(
                (random.NextDouble() - 0.5) * 0.5, // Small X variation
                -9.80665 + (random.NextDouble() - 0.5) * 0.3, // Gravity + noise
                (random.NextDouble() - 0.5) * 0.5  // Small Z variation
            );

            var gyro = new Vector3(
                (random.NextDouble() - 0.5) * 0.1,
                (random.NextDouble() - 0.5) * 0.1,
                (random.NextDouble() - 0.5) * 0.1
            );

            var msg = new SensorFrameMessage(
                token,
                accel,
                gyro,
                null,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );

            await SendMessage(ws, msg);
            frameCount++;

            if (frameCount % 60 == 0)
            {
                Console.WriteLine($"[FakeController] Sent {frameCount} frames...");
            }

            await Task.Delay(16); // ~60Hz
        }

        Console.WriteLine($"[FakeController] Sent {frameCount} frames total");
    }

    static async Task RunThrowSimulation(ClientWebSocket ws, string token, ThrowType throwType)
    {
        Console.WriteLine($"[FakeController] Simulating {throwType} throw...");

        var frames = GenerateThrowFrames(throwType);
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Send controller ready
        await SendMessage(ws, new ControllerReadyMessage(token));
        Console.WriteLine("[FakeController] Sent controller ready");

        // Send throw started
        await SendMessage(ws, new ThrowStartedMessage(token, timestamp));
        Console.WriteLine("[FakeController] Sent throw started");

        // Send sensor frames
        foreach (var frame in frames)
        {
            var newFrame = frame with { SessionToken = token, Timestamp = timestamp };
            await SendMessage(ws, newFrame);
            timestamp += 16; // ~60Hz
            await Task.Delay(16);
        }

        Console.WriteLine($"[FakeController] Sent {frames.Count} sensor frames");

        // Wait for throw released response from gateway
        var buffer = new byte[4096];
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        var response = Encoding.UTF8.GetString(buffer, 0, result.Count);
        Console.WriteLine($"[FakeController] Received: {response}");

        // Keep connection open briefly
        await Task.Delay(1000);
    }

    static List<SensorFrameMessage> GenerateThrowFrames(ThrowType throwType)
    {
        var frames = new List<SensorFrameMessage>();
        var random = new Random();
        
        // Parameters per throw type
        double peakAccelY, peakGyroZ, durationMs;
        
        switch (throwType)
        {
            case ThrowType.Normal:
                peakAccelY = 12.0; peakGyroZ = 3.0; durationMs = 1200; break;
            case ThrowType.Soft:
                peakAccelY = 10.0; peakGyroZ = 1.5; durationMs = 1500; break;
            case ThrowType.Strong:
                peakAccelY = 18.0; peakGyroZ = 2.0; durationMs = 1000; break;
            case ThrowType.ThrowLeft:
                peakAccelY = 12.0; peakGyroZ = 3.0; durationMs = 1200; break;  // Left direction, small gyro at release
            case ThrowType.ThrowCenter:
                peakAccelY = 12.0; peakGyroZ = 3.0; durationMs = 1200; break;  // Center, small gyro at release
            case ThrowType.ThrowRight:
                peakAccelY = 12.0; peakGyroZ = 3.0; durationMs = 1200; break;  // Right direction, small gyro at release
            case ThrowType.HookLeft:
                peakAccelY = 12.0; peakGyroZ = -4.0; durationMs = 1200; break;  // Center direction, left hook
            case ThrowType.HookRight:
                peakAccelY = 12.0; peakGyroZ = 4.0; durationMs = 1200; break;  // Center direction, right hook
            case ThrowType.Left:
                peakAccelY = 10.0; peakGyroZ = -4.0; durationMs = 1100; break;
            case ThrowType.Right:
                peakAccelY = 10.0; peakGyroZ = 4.0; durationMs = 1100; break;
            case ThrowType.Weak:
                peakAccelY = 5.0; peakGyroZ = 1.0; durationMs = 1500; break;
            case ThrowType.Idle:
                peakAccelY = 0.0; peakGyroZ = 0.0; durationMs = 2000; break;
            case ThrowType.Shake:
                peakAccelY = 0.0; peakGyroZ = 0.0; durationMs = 2000; break;
            case ThrowType.Walk:
                peakAccelY = 0.0; peakGyroZ = 0.0; durationMs = 3000; break;
            case ThrowType.RotateOnly:
                peakAccelY = 0.0; peakGyroZ = 0.0; durationMs = 2000; break;
            case ThrowType.ShortJerk:
                peakAccelY = 0.0; peakGyroZ = 0.0; durationMs = 500; break;
            default:
                peakAccelY = 12.0; peakGyroZ = 3.0; durationMs = 1200; break;
        }

        int frameCount = (int)(durationMs / 16);
        
        // Handle false positive cases - these should NOT trigger a throw
        if (throwType == ThrowType.Idle || throwType == ThrowType.Shake || 
            throwType == ThrowType.Walk || throwType == ThrowType.RotateOnly || 
            throwType == ThrowType.ShortJerk)
        {
            return GenerateFalsePositiveFrames(throwType, (int)durationMs);
        }
        
        // Determine lateral acceleration for direction
        double peakAccelX = 0.0;
        if (throwType == ThrowType.ThrowLeft)
            peakAccelX = -8.0;
        else if (throwType == ThrowType.ThrowRight)
            peakAccelX = 8.0;
        // ThrowCenter has 0 lateral acceleration
        
        // Phase 1: IDLE/READY (phone upright) - 200ms
        int readyFrames = 12;
        for (int i = 0; i < readyFrames; i++)
        {
            frames.Add(new SensorFrameMessage("", 
                new Vector3(0, -9.80665, 0), 
                new Vector3(0, 0, 0), null, 0));
        }

        // Phase 2: BACKSWING (phone moves back) - 400ms
        int backswingFrames = 25;
        for (int i = 0; i < backswingFrames; i++)
        {
            double progress = (double)i / backswingFrames;
            double accelY = -2.0 - progress * 6.0; // Goes from -2 to -8
            double accelX = progress * peakAccelX * 0.3; // Some lateral in backswing
            double gyroZ = progress * 1.5;
            
            frames.Add(new SensorFrameMessage("", 
                new Vector3(accelX, -9.80665 + accelY, 0), 
                new Vector3(0, 0, gyroZ), null, 0));
        }

        // Phase 3: FORWARD_SWING (phone moves forward) - 400ms
        int forwardFrames = 25;
        for (int i = 0; i < forwardFrames; i++)
        {
            double progress = (double)i / forwardFrames;
            // Acceleration ramps up to peak then down
            double accelCurve = Math.Sin(progress * Math.PI);
            double accelY = accelCurve * peakAccelY;
            double accelX = accelCurve * peakAccelX;
            double gyroZ = peakGyroZ * (1.0 - progress * 0.5);
            
            frames.Add(new SensorFrameMessage("", 
                new Vector3(accelX, -9.80665 + accelY, 0), 
                new Vector3(0, 0, gyroZ), null, 0));
        }

        // Phase 4: RELEASE (sharp gyro spike) - 100ms
        int releaseFrames = 6;
        for (int i = 0; i < releaseFrames; i++)
        {
            double progress = (double)i / releaseFrames;
            double accelY = 2.0 * (1.0 - progress); // Decelerating
            double accelX = 0.0; // No lateral at release
            double gyroZ = peakGyroZ * (1.0 + progress * 2.0); // Spike
            
            frames.Add(new SensorFrameMessage("", 
                new Vector3(accelX, -9.80665 + accelY, 0), 
                new Vector3(0, 0, gyroZ), null, 0));
        }

        // Phase 5: COOLDOWN - 100ms
        int cooldownFrames = 6;
        for (int i = 0; i < cooldownFrames; i++)
        {
            frames.Add(new SensorFrameMessage("", 
                new Vector3(0, -9.80665, 0), 
                new Vector3(0, 0, 0), null, 0));
        }

        return frames;
    }

    static List<SensorFrameMessage> GenerateFalsePositiveFrames(ThrowType throwType, int durationMs)
    {
        var frames = new List<SensorFrameMessage>();
        int frameCount = (int)(durationMs / 16);
        var random = new Random();

        switch (throwType)
        {
            case ThrowType.Idle:
                // Phone held perfectly still
                for (int i = 0; i < frameCount; i++)
                {
                    frames.Add(new SensorFrameMessage("", 
                        new Vector3(0, -9.80665, 0), 
                        new Vector3(0, 0, 0), null, 0));
                }
                break;

            case ThrowType.Shake:
                // Rapid shaking - high frequency, random direction
                for (int i = 0; i < frameCount; i++)
                {
                    var accelX = (random.NextDouble() - 0.5) * 4.0;
                    var accelY = -9.80665 + (random.NextDouble() - 0.5) * 2.0;
                    var accelZ = (random.NextDouble() - 0.5) * 4.0;
                    var gyroX = (random.NextDouble() - 0.5) * 4.0;
                    var gyroY = (random.NextDouble() - 0.5) * 4.0;
                    var gyroZ = (random.NextDouble() - 0.5) * 4.0;
                    
                    frames.Add(new SensorFrameMessage("", 
                        new Vector3(accelX, accelY, accelZ), 
                        new Vector3(gyroX, gyroY, gyroZ), null, 0));
                }
                break;

            case ThrowType.Walk:
                // Walking motion - periodic acceleration at ~2Hz
                for (int i = 0; i < frameCount; i++)
                {
                    double t = i * 16.0 / 1000.0; // time in seconds
                    double walkCycle = Math.Sin(t * 2 * Math.PI * 2.0); // 2Hz
                    double accelY = -9.80665 + walkCycle * 1.5;
                    double accelX = Math.Cos(t * 2 * Math.PI * 2.0) * 0.5;
                    double gyroZ = walkCycle * 0.3;
                    
                    frames.Add(new SensorFrameMessage("", 
                        new Vector3(accelX, accelY, 0), 
                        new Vector3(0, 0, gyroZ), null, 0));
                }
                break;

            case ThrowType.RotateOnly:
                // Pure rotation - no linear acceleration, only gyro
                for (int i = 0; i < frameCount; i++)
                {
                    frames.Add(new SensorFrameMessage("", 
                        new Vector3(0, -9.80665, 0), 
                        new Vector3(0, 0, 2.0), null, 0));
                }
                break;

            case ThrowType.ShortJerk:
                // Single sharp jerk - brief high acceleration
                int jerkFrames = 3;
                for (int i = 0; i < jerkFrames; i++)
                {
                    frames.Add(new SensorFrameMessage("", 
                        new Vector3(0, -9.80665 + 15.0, 0), 
                        new Vector3(0, 0, 0), null, 0));
                }
                // Rest of frames are idle
                for (int i = jerkFrames; i < frameCount; i++)
                {
                    frames.Add(new SensorFrameMessage("", 
                        new Vector3(0, -9.80665, 0), 
                        new Vector3(0, 0, 0), null, 0));
                }
                break;
        }

        return frames;
    }

    static async Task SendMessage(ClientWebSocket ws, object message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });
        
        Console.WriteLine($"[FC Send] {json}");
        
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

enum ThrowType
{
    Normal,
    Soft,
    Strong,
    ThrowLeft,
    ThrowCenter,
    ThrowRight,
    HookLeft,
    HookRight,
    Left,
    Right,
    Weak,
    Idle,
    Shake,
    Walk,
    RotateOnly,
    ShortJerk
}