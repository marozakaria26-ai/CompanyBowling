namespace Bowling.Gateway;

using Bowling.Gateway.Configuration;
using Bowling.Gateway.Controllers;
using Bowling.Gateway.Services;
using Bowling.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
        builder.Services.Configure<ThrowDetectionOptions>(builder.Configuration.GetSection(ThrowDetectionOptions.SectionName));

        builder.Services.AddSingleton<ISessionManager, SessionManager>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GatewayOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<SessionManager>>();
            return new SessionManager(
                TimeSpan.FromSeconds(options.PairingExpirationSeconds),
                TimeSpan.FromMinutes(options.ActiveExpirationMinutes),
                logger);
        });

        builder.Services.AddSingleton<IThrowDetector, ThrowDetector>();
        builder.Services.AddSingleton<IQrCodeService, QrCodeService>();
        builder.Services.AddSingleton<IWebSocketConnectionManager, WebSocketConnectionManager>();

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        var app = builder.Build();

        app.UseWebSockets();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseRouting();

        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }));

        // WebSocket endpoints
        app.Map("/ws", async context =>
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("WS request: {Path}, IsWS: {IsWS}", context.Request.Path, context.WebSockets.IsWebSocketRequest);
            
            if (context.WebSockets.IsWebSocketRequest)
            {
                logger.LogInformation("Accepting WebSocket connection from {RemoteIp}", context.Connection.RemoteIpAddress);
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var connectionManager = context.RequestServices.GetRequiredService<IWebSocketConnectionManager>();
                var sessionManager = context.RequestServices.GetRequiredService<ISessionManager>();
                var throwDetector = context.RequestServices.GetRequiredService<IThrowDetector>();
                
                await WebSocketHandler.HandlePhoneConnectionAsync(webSocket, connectionManager, sessionManager, throwDetector, logger);
            }
            else
            {
                logger.LogWarning("Non-WebSocket request to /ws: {Headers}", context.Request.Headers);
                context.Response.StatusCode = 400;
            }
        });

        app.Map("/unity", async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var connectionManager = context.RequestServices.GetRequiredService<IWebSocketConnectionManager>();
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                
                await WebSocketHandler.HandleUnityConnectionAsync(webSocket, connectionManager, logger);
            }
            else
            {
                context.Response.StatusCode = 400;
            }
        });

        app.MapGet("/", () => Results.Redirect("/api/session/create"));

        var port = builder.Configuration.GetValue<int>("Gateway:Port", 5050);
        var urls = new[] { $"http://0.0.0.0:{port}" };
        app.Urls.Clear();
        foreach (var url in urls)
        {
            app.Urls.Add(url);
        }

        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        var ip = GetLocalIpAddress();
        logger.LogInformation("========================================");
        logger.LogInformation("[BOWLING] Company Bowling Gateway Starting");
        logger.LogInformation("========================================");
        logger.LogInformation("Listening on: http://{Ip}:{Port}", ip, port);
        logger.LogInformation("Phone WebSocket: ws://{Ip}:{Port}/ws", ip, port);
        logger.LogInformation("Unity WebSocket: ws://{Ip}:{Port}/unity", ip, port);
        logger.LogInformation("Session API: http://{Ip}:{Port}/api/session/create", ip, port);
        logger.LogInformation("========================================");

        app.Run();
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }
}

public interface IWebSocketConnectionManager
{
    void RegisterPhone(string sessionToken, WebSocket webSocket);
    void RegisterUnity(WebSocket webSocket);
    void UnregisterPhone(string sessionToken);
    void UnregisterUnity(WebSocket webSocket);
    void BroadcastToUnity(string message);
    WebSocket? GetPhoneSocket(string sessionToken);
    IEnumerable<WebSocket> GetUnitySockets();
}

public sealed class WebSocketConnectionManager : IWebSocketConnectionManager
{
    private readonly Dictionary<string, WebSocket> _phoneSockets = new();
    private readonly HashSet<WebSocket> _unitySockets = new();
    private readonly object _lock = new();
    private readonly ILogger<WebSocketConnectionManager> _logger;

    public WebSocketConnectionManager(ILogger<WebSocketConnectionManager> logger)
    {
        _logger = logger;
    }

    public void RegisterPhone(string sessionToken, WebSocket webSocket)
    {
        lock (_lock)
        {
            _phoneSockets[sessionToken] = webSocket;
            _logger.LogDebug("Phone registered: {Token}", MaskToken(sessionToken));
        }
    }

    public void RegisterUnity(WebSocket webSocket)
    {
        lock (_lock)
        {
            _unitySockets.Add(webSocket);
            _logger.LogDebug("Unity registered. Total: {Count}", _unitySockets.Count);
        }
    }

    public void UnregisterPhone(string sessionToken)
    {
        lock (_lock)
        {
            _phoneSockets.Remove(sessionToken);
            _logger.LogDebug("Phone unregistered: {Token}", MaskToken(sessionToken));
        }
    }

    public void UnregisterUnity(WebSocket webSocket)
    {
        lock (_lock)
        {
            _unitySockets.Remove(webSocket);
            _logger.LogDebug("Unity unregistered. Total: {Count}", _unitySockets.Count);
        }
    }

    public void BroadcastToUnity(string message)
    {
        var sockets = new List<WebSocket>();
        lock (_lock)
        {
            sockets.AddRange(_unitySockets);
        }

        foreach (var socket in sockets)
        {
            if (socket.State == WebSocketState.Open)
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(message);
                    socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch
                {
                    // Ignore send failures
                }
            }
        }
    }

    public WebSocket? GetPhoneSocket(string sessionToken)
    {
        lock (_lock)
        {
            return _phoneSockets.TryGetValue(sessionToken, out var ws) ? ws : null;
        }
    }

    public IEnumerable<WebSocket> GetUnitySockets()
    {
        lock (_lock)
        {
            return _unitySockets.ToList();
        }
    }

    private static string MaskToken(string token)
    {
        return token.Length > 8 ? token[..4] + "****" + token[^4..] : "****";
    }
}

public static class WebSocketHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task HandlePhoneConnectionAsync(
        WebSocket webSocket,
        IWebSocketConnectionManager connectionManager,
        ISessionManager sessionManager,
        IThrowDetector throwDetector,
        ILogger logger)
    {
        string? sessionToken = null;
        var buffer = new byte[4096];

        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = ParseMessage(json);

                    if (message == null)
                    {
                        await SendError(webSocket, "PARSE_ERROR", "Invalid message format");
                        continue;
                    }

                    var msgType = GetMessageType(message);
                    switch (msgType)
                    {
                        case MessageType.PhoneConnected:
                            sessionToken = HandlePhoneRegister(json, webSocket, connectionManager, sessionManager, logger);
                            break;

                        case MessageType.SensorFrame:
                            if (sessionToken != null)
                            {
                                await HandleSensorFrame(json, sessionToken, webSocket, connectionManager, sessionManager, throwDetector, logger);
                            }
                            else
                            {
                                await SendError(webSocket, "NOT_REGISTERED", "Register first");
                            }
                            break;

                        case MessageType.ControllerReady:
                            if (sessionToken != null)
                            {
                                await BroadcastToUnity(json);
                            }
                            break;

                        case MessageType.ThrowStarted:
                            if (sessionToken != null)
                            {
                                await BroadcastToUnity(json);
                            }
                            break;

                        case MessageType.Ping:
                            await SendPong(webSocket);
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Phone WebSocket error");
        }
        finally
        {
            if (sessionToken != null)
            {
                connectionManager.UnregisterPhone(sessionToken);
                sessionManager.UnpairSession(sessionToken);
                await BroadcastToUnity(CreateMessage(new PhoneDisconnectedMessage(sessionToken, "Disconnected")));
            }
        }
    }

    private static string? HandlePhoneRegister(string json, WebSocket webSocket, IWebSocketConnectionManager connectionManager, ISessionManager sessionManager, ILogger logger)
    {
        var msg = JsonSerializer.Deserialize<PhoneConnectedMessage>(json, JsonOptions);
        if (msg == null)
        {
            _ = SendError(webSocket, "INVALID_MESSAGE", "Invalid registration");
            return null;
        }

        if (!sessionManager.TryPairSession(msg.SessionToken, msg.DeviceId, msg.DeviceName))
        {
            var session = sessionManager.GetSession(msg.SessionToken);
            var reason = session == null ? "Invalid session" : 
                        session.IsPairingExpired ? "Session expired" :
                        session.IsUsed ? "Session already used" : "Session not available";
            
            _ = SendError(webSocket, "SESSION_INVALID", reason);
            _ = SendMessage(webSocket, new SessionExpiredMessage(msg.SessionToken));
            return null;
        }

        connectionManager.RegisterPhone(msg.SessionToken, webSocket);

        var sessionInfo = sessionManager.GetSession(msg.SessionToken)!;
        var response = new SessionCreatedMessage(
            msg.SessionToken,
            $"ws://{GetLocalIpAddress()}:5050/ws",
            5050,
            sessionInfo.PairingExpiresAt.Ticks,
            sessionInfo.ActiveExpiresAt.Ticks
        );

        _ = SendMessage(webSocket, response);
        _ = BroadcastToUnity(CreateMessage(new PhoneConnectedMessage(msg.SessionToken, msg.DeviceId, msg.DeviceName)));
        
        logger.LogInformation("Phone registered: {Token}", MaskToken(msg.SessionToken));
        return msg.SessionToken;
    }

    private static async Task HandleSensorFrame(string json, string sessionToken, WebSocket webSocket, IWebSocketConnectionManager connectionManager, ISessionManager sessionManager, IThrowDetector throwDetector, ILogger logger)
    {
        var msg = JsonSerializer.Deserialize<SensorFrameMessage>(json, JsonOptions);
        if (msg == null || msg.SessionToken != sessionToken)
        {
            await SendError(webSocket, "INVALID_SESSION", "Session mismatch");
            return;
        }

        var session = sessionManager.GetSession(sessionToken);
        if (session == null || session.IsPairingExpired)
        {
            await SendMessage(webSocket, new SessionExpiredMessage(sessionToken));
            return;
        }

        // Validate sensor values
        var accelMag = Math.Sqrt(msg.Acceleration.X * msg.Acceleration.X + msg.Acceleration.Y * msg.Acceleration.Y + msg.Acceleration.Z * msg.Acceleration.Z);
        var gyroMag = Math.Sqrt(msg.Gyroscope.X * msg.Gyroscope.X + msg.Gyroscope.Y * msg.Gyroscope.Y + msg.Gyroscope.Z * msg.Gyroscope.Z);
        
        if (accelMag > 100 || gyroMag > 50)
        {
            await SendError(webSocket, "INVALID_SENSOR_DATA", "Values out of range");
            return;
        }

        sessionManager.MarkSensorFrameReceived(sessionToken);

        // Process through throw detector
        var sensorFrame = new SensorFrame
        {
            Timestamp = msg.Timestamp,
            Acceleration = new Vector3(msg.Acceleration.X, msg.Acceleration.Y, msg.Acceleration.Z),
            Gyroscope = new Vector3(msg.Gyroscope.X, msg.Gyroscope.Y, msg.Gyroscope.Z),
            Orientation = msg.Orientation != null ? new Quaternion(msg.Orientation.X, msg.Orientation.Y, msg.Orientation.Z, msg.Orientation.W) : null
        };

        var throwResult = throwDetector.ProcessFrame(sensorFrame);

        if (throwResult.IsValid && throwResult.Parameters != null)
        {
            var throwReleased = new ThrowReleasedMessage(sessionToken, throwResult.Parameters);
            await BroadcastToUnity(CreateMessage(throwReleased));
            await SendMessage(webSocket, throwReleased);
            logger.LogInformation("Throw detected: Power={Power:F2}, Direction={Direction:F2}, Spin={Spin:F2}, Angle={Angle:F1}",
                throwResult.Parameters.Power, throwResult.Parameters.Direction, throwResult.Parameters.Spin, throwResult.Parameters.ReleaseAngle);
        }
        else if (throwResult.FinalState == ThrowState.Release && !throwResult.IsValid)
        {
            logger.LogDebug("Throw rejected: {Reason}", throwResult.RejectionReason);
        }

        // Forward sensor frame to Unity
        await BroadcastToUnity(json);
    }

    public static async Task HandleUnityConnectionAsync(WebSocket webSocket, IWebSocketConnectionManager connectionManager, ILogger logger)
    {
        connectionManager.RegisterUnity(webSocket);
        
        // Send current gateway info
        var info = new GatewayInfoMessage(GetLocalIpAddress(), 5050, "1.0.0", 0, true);
        await SendMessage(webSocket, info);

        var buffer = new byte[4096];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = ParseMessage(json);

                    if (message != null)
                    {
                        var msgType = GetMessageType(message);
                        switch (msgType)
                        {
                            case MessageType.ThrowComplete:
                                logger.LogInformation("Throw complete: Pins={Pins}", 
                                    JsonSerializer.Deserialize<ThrowCompleteMessage>(json, JsonOptions)?.PinsKnockedDown);
                                break;
                            case MessageType.ResetLane:
                                // Broadcast reset to all
                                await BroadcastToUnity(json);
                                break;
                            case MessageType.Ping:
                                await SendPong(webSocket);
                                break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unity WebSocket error");
        }
        finally
        {
            connectionManager.UnregisterUnity(webSocket);
        }
    }

    private static MessageBase? ParseMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeElement))
            {
                File.AppendAllText("gateway_debug.log", $"[ParseMessage] No 'type' property in: {json}\n");
                return null;
            }

            var typeName = typeElement.GetString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(typeName))
            {
                File.AppendAllText("gateway_debug.log", $"[ParseMessage] Empty type name in: {json}\n");
                return null;
            }

            File.AppendAllText("gateway_debug.log", $"[ParseMessage] Parsing type: {typeName}\n");

            switch (typeName)
            {
                case "phoneconnected":
                    return JsonSerializer.Deserialize<PhoneConnectedMessage>(json, JsonOptions);
                case "sensorframe":
                    return JsonSerializer.Deserialize<SensorFrameMessage>(json, JsonOptions);
                case "controllerready":
                    return JsonSerializer.Deserialize<ControllerReadyMessage>(json, JsonOptions);
                case "throwstarted":
                    return JsonSerializer.Deserialize<ThrowStartedMessage>(json, JsonOptions);
                case "throwreleased":
                    return JsonSerializer.Deserialize<ThrowReleasedMessage>(json, JsonOptions);
                case "throwcomplete":
                    return JsonSerializer.Deserialize<ThrowCompleteMessage>(json, JsonOptions);
                case "resetlane":
                    return JsonSerializer.Deserialize<ResetLaneMessage>(json, JsonOptions);
                case "ping":
                    return JsonSerializer.Deserialize<PingMessage>(json, JsonOptions);
                default:
                    File.AppendAllText("gateway_debug.log", $"[ParseMessage] Unknown type: {typeName}\n");
                    return null;
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText("gateway_debug.log", $"[ParseMessage] Error: {ex.Message} for JSON: {json}\n");
            return null;
        }
    }

    private static async Task SendMessage(WebSocket webSocket, object message)
    {
        if (webSocket.State != WebSocketState.Open) return;
        
        try
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
    }

    private static async Task BroadcastToUnity(string json)
    {
        // This will be called from connection manager
    }

    private static string CreateMessage(object message)
    {
        return JsonSerializer.Serialize(message, JsonOptions);
    }

    private static async Task SendError(WebSocket webSocket, string code, string description)
    {
        await SendMessage(webSocket, new ErrorMessage(code, description));
    }

    private static async Task SendPong(WebSocket webSocket)
    {
        await SendMessage(webSocket, new PongMessage());
    }

    private static MessageType GetMessageType(MessageBase message)
    {
        return message switch
        {
            PhoneConnectedMessage => MessageType.PhoneConnected,
            PhoneDisconnectedMessage => MessageType.PhoneDisconnected,
            SensorFrameMessage => MessageType.SensorFrame,
            ControllerReadyMessage => MessageType.ControllerReady,
            ThrowStartedMessage => MessageType.ThrowStarted,
            ThrowReleasedMessage => MessageType.ThrowReleased,
            ThrowCompleteMessage => MessageType.ThrowComplete,
            SessionCreatedMessage => MessageType.SessionCreated,
            SessionExpiredMessage => MessageType.SessionExpired,
            ErrorMessage => MessageType.Error,
            PingMessage => MessageType.Ping,
            PongMessage => MessageType.Pong,
            GatewayInfoMessage => MessageType.GatewayInfo,
            UnityConnectedMessage => MessageType.UnityConnected,
            UnityDisconnectedMessage => MessageType.UnityDisconnected,
            ResetLaneMessage => MessageType.ResetLane,
            ThrowParameters => MessageType.ThrowParameters,
            _ => MessageType.Unknown
        };
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return "127.0.0.1";
    }

    private static string MaskToken(string token)
    {
        return token.Length > 8 ? token[..4] + "****" + token[^4..] : "****";
    }
}