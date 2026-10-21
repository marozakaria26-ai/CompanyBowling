namespace CompanyBowling.Protocol
{
    using System;
    using System.Collections.Generic;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public static class ProtocolJson
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter> { new Newtonsoft.Json.Converters.StringEnumConverter() }
        };

        private static readonly Dictionary<string, Type> TypeMap = new Dictionary<string, Type>
        {
            { "phoneconnected", typeof(PhoneConnectedMessage) },
            { "phonedisconnected", typeof(PhoneDisconnectedMessage) },
            { "sensorframe", typeof(SensorFrameMessage) },
            { "controllerready", typeof(ControllerReadyMessage) },
            { "throwstarted", typeof(ThrowStartedMessage) },
            { "throwreleased", typeof(ThrowReleasedMessage) },
            { "throwparameters", typeof(ThrowParameters) },
            { "sessioncreated", typeof(SessionCreatedMessage) },
            { "sessionexpired", typeof(SessionExpiredMessage) },
            { "error", typeof(ErrorMessage) },
            { "ping", typeof(PingMessage) },
            { "pong", typeof(PongMessage) },
            { "gatewayinfo", typeof(GatewayInfoMessage) },
            { "unityconnected", typeof(UnityConnectedMessage) },
            { "unitydisconnected", typeof(UnityDisconnectedMessage) },
            { "resetlane", typeof(ResetLaneMessage) },
            { "throwcomplete", typeof(ThrowCompleteMessage) }
        };

        public static string Serialize(MessageBase message)
        {
            return JsonConvert.SerializeObject(message, Settings);
        }

        public static T Deserialize<T>(string json) where T : MessageBase
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        public static MessageBase Deserialize(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                if (!obj.TryGetValue("type", out var typeToken))
                    return null;

                var typeName = typeToken.ToString().ToLowerInvariant();
                if (TypeMap.TryGetValue(typeName, out var type))
                {
                    return JsonConvert.DeserializeObject(json, type, Settings) as MessageBase;
                }
            }
            catch
            {
                // Ignore parse errors
            }
            return null;
        }
    }
}