namespace CompanyBowling.Network
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnityEngine;
    using WebSocketSharp;
    using CompanyBowling.Protocol;

    public class GatewayConnection : MonoBehaviour
    {
        [Header("Connection Settings")]
        [SerializeField] private string gatewayHost = "127.0.0.1";
        [SerializeField] private int gatewayPort = 5050;
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private float reconnectInterval = 5f;
        [SerializeField] private float pingInterval = 10f;

        [Header("Status")]
        [SerializeField] private bool isConnected = false;
        [SerializeField] private bool isPhoneConnected = false;
        [SerializeField] private string lastPhoneDeviceName = "";
        [SerializeField] private int sensorFrameRate = 0;
        [SerializeField] private ThrowParameters lastThrowParameters;
        [SerializeField] private long lastFrameTime = 0;
        [SerializeField] private int frameCount = 0;

        private WebSocket _webSocket;
        private readonly ConcurrentQueue<MessageBase> _messageQueue = new ConcurrentQueue<MessageBase>();
        private readonly List<double> _frameTimestamps = new List<double>();
        private float _reconnectTimer = 0f;
        private float _pingTimer = 0f;
        private bool _shouldReconnect = false;

        public event Action<PhoneConnectedMessage> OnPhoneConnected;
        public event Action<PhoneDisconnectedMessage> OnPhoneDisconnected;
        public event Action<SensorFrameMessage> OnSensorFrame;
        public event Action<ControllerReadyMessage> OnControllerReady;
        public event Action<ThrowStartedMessage> OnThrowStarted;
        public event Action<ThrowReleasedMessage> OnThrowReleased;
        public event Action<GatewayInfoMessage> OnGatewayInfo;
        public event Action<ErrorMessage> OnError;
        public event Action<bool> OnConnectionChanged;
        public event Action<ThrowCompleteMessage> OnThrowComplete;

        public bool IsConnected => isConnected;
        public bool IsPhoneConnected => isPhoneConnected;
        public string LastPhoneDeviceName => lastPhoneDeviceName;
        public int SensorFrameRate => sensorFrameRate;
        public ThrowParameters LastThrowParameters => lastThrowParameters;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (autoConnect)
            {
                Connect();
            }
        }

        private void Update()
        {
            ProcessMessageQueue();
            UpdateFrameRate();
            HandleReconnect();
            HandlePing();
        }

        public void Connect(string host = null, int port = -1)
        {
            if (host != null) gatewayHost = host;
            if (port > 0) gatewayPort = port;

            var url = $"ws://{gatewayHost}:{gatewayPort}/unity";
            Debug.Log($"[GatewayConnection] Connecting to {url}");

            _webSocket = new WebSocket(url);
            _webSocket.OnOpen += OnWebSocketOpen;
            _webSocket.OnMessage += OnWebSocketMessage;
            _webSocket.OnError += OnWebSocketError;
            _webSocket.OnClose += OnWebSocketClose;
            _webSocket.ConnectAsync();
        }

        public void Disconnect()
        {
            _shouldReconnect = false;
            _webSocket?.Close();
            _webSocket = null;
            SetConnected(false);
        }

        private void OnWebSocketOpen(object sender, EventArgs e)
        {
            Debug.Log("[GatewayConnection] Connected to gateway");
            SetConnected(true);
            _shouldReconnect = true;
            SendRegisterUnity();
        }

        private void OnWebSocketMessage(object sender, MessageEventArgs e)
        {
            try
            {
                var message = ProtocolJson.Deserialize(e.Data);
                if (message != null)
                {
                    _messageQueue.Enqueue(message);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GatewayConnection] Failed to parse message: {ex.Message}");
            }
        }

        private void OnWebSocketError(object sender, ErrorEventArgs e)
        {
            Debug.LogError($"[GatewayConnection] WebSocket error: {e.Message}");
        }

        private void OnWebSocketClose(object sender, CloseEventArgs e)
        {
            Debug.Log($"[GatewayConnection] Disconnected: {e.Reason}");
            SetConnected(false);
        }

        private void ProcessMessageQueue()
        {
            while (_messageQueue.TryDequeue(out var message))
            {
                HandleMessage(message);
            }
        }

        private void HandleMessage(MessageBase message)
        {
            switch (message)
            {
                case PhoneConnectedMessage msg:
                    isPhoneConnected = true;
                    lastPhoneDeviceName = msg.deviceName;
                    OnPhoneConnected?.Invoke(msg);
                    Debug.Log($"[GatewayConnection] Phone connected: {msg.deviceName}");
                    break;

                case PhoneDisconnectedMessage msg:
                    isPhoneConnected = false;
                    lastPhoneDeviceName = "";
                    OnPhoneDisconnected?.Invoke(msg);
                    Debug.Log($"[GatewayConnection] Phone disconnected: {msg.reason}");
                    break;

                case SensorFrameMessage msg:
                    OnSensorFrame?.Invoke(msg);
                    lastFrameTime = msg.timestamp;
                    frameCount++;
                    _frameTimestamps.Add(Time.realtimeSinceStartup);
                    break;

                case ControllerReadyMessage msg:
                    OnControllerReady?.Invoke(msg);
                    Debug.Log("[GatewayConnection] Controller ready");
                    break;

                case ThrowStartedMessage msg:
                    OnThrowStarted?.Invoke(msg);
                    Debug.Log("[GatewayConnection] Throw started");
                    break;

                case ThrowReleasedMessage msg:
                    lastThrowParameters = msg.parameters;
                    OnThrowReleased?.Invoke(msg);
                    Debug.Log($"[GatewayConnection] Throw released: Power={msg.parameters.power:F2}, Direction={msg.parameters.direction:F2}, Spin={msg.parameters.spin:F2}, Angle={msg.parameters.releaseAngle:F1}");
                    break;

                case GatewayInfoMessage msg:
                    OnGatewayInfo?.Invoke(msg);
                    break;

                case ErrorMessage msg:
                    OnError?.Invoke(msg);
                    Debug.LogWarning($"[GatewayConnection] Error: {msg.code} - {msg.description}");
                    break;

                case ThrowCompleteMessage msg:
                    OnThrowComplete?.Invoke(msg);
                    Debug.Log($"[GatewayConnection] Throw complete: {msg.pinsKnockedDown} pins, pos={msg.ballFinalPositionZ:F1}");
                    break;
            }
        }

        private void UpdateFrameRate()
        {
            var now = Time.realtimeSinceStartup;
            _frameTimestamps.RemoveAll(t => now - t > 1f);
            sensorFrameRate = _frameTimestamps.Count;
        }

        private void HandleReconnect()
        {
            if (!_shouldReconnect || isConnected) return;

            _reconnectTimer += Time.deltaTime;
            if (_reconnectTimer >= reconnectInterval)
            {
                _reconnectTimer = 0f;
                Debug.Log("[GatewayConnection] Attempting reconnect...");
                Connect();
            }
        }

        private void HandlePing()
        {
            if (!isConnected) return;

            _pingTimer += Time.deltaTime;
            if (_pingTimer >= pingInterval)
            {
                _pingTimer = 0f;
                SendPing();
            }
        }

        private void SendRegisterUnity()
        {
            var msg = new UnityConnectedMessage();
            SendMessage(msg);
        }

        private void SendPing()
        {
            var msg = new PingMessage();
            SendMessage(msg);
        }

        public void SendThrowComplete(ThrowCompleteMessage message)
        {
            SendMessage(message);
        }

        public void SendResetLane()
        {
            var msg = new ResetLaneMessage();
            SendMessage(msg);
        }

        private void SendMessage(MessageBase message)
        {
            if (!isConnected || _webSocket == null) return;

            try
            {
                var json = ProtocolJson.Serialize(message);
                _webSocket.Send(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GatewayConnection] Failed to send message: {ex.Message}");
            }
        }

        private void SetConnected(bool connected)
        {
            isConnected = connected;
            OnConnectionChanged?.Invoke(connected);
        }

        private void OnDestroy()
        {
            Disconnect();
        }
    }
}