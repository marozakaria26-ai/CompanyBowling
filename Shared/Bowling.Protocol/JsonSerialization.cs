namespace Bowling.Protocol;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class ProtocolJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public static string Serialize<T>(T value) where T : MessageBase
        => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) where T : MessageBase
        => JsonSerializer.Deserialize<T>(json, Options);

    public static MessageBase? Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeElement))
            return null;

        var typeName = typeElement.GetString();
        if (string.IsNullOrEmpty(typeName))
            return null;

        return typeName.ToLowerInvariant() switch
        {
            "phoneconnected" => Deserialize<PhoneConnectedMessage>(json),
            "phonedisconnected" => Deserialize<PhoneDisconnectedMessage>(json),
            "sensorframe" => Deserialize<SensorFrameMessage>(json),
            "controllerready" => Deserialize<ControllerReadyMessage>(json),
            "throwstarted" => Deserialize<ThrowStartedMessage>(json),
            "throwreleased" => Deserialize<ThrowReleasedMessage>(json),
            "throwparameters" => Deserialize<ThrowParameters>(json),
            "sessioncreated" => Deserialize<SessionCreatedMessage>(json),
            "sessionexpired" => Deserialize<SessionExpiredMessage>(json),
            "error" => Deserialize<ErrorMessage>(json),
            "ping" => Deserialize<PingMessage>(json),
            "pong" => Deserialize<PongMessage>(json),
            "gatewayinfo" => Deserialize<GatewayInfoMessage>(json),
            "unityconnected" => Deserialize<UnityConnectedMessage>(json),
            "unitydisconnected" => Deserialize<UnityDisconnectedMessage>(json),
            "resetlane" => Deserialize<ResetLaneMessage>(json),
            "throwcomplete" => Deserialize<ThrowCompleteMessage>(json),
            _ => null
        };
    }
}