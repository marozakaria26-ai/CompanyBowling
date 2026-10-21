namespace CompanyBowling.UI
{
    using UnityEngine;
    using UnityEngine.UI;
    using CompanyBowling.Network;
    using CompanyBowling.Game;
    using CompanyBowling.Protocol;

    public class GameUIController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Text gatewayStatusText;
        [SerializeField] private Text phoneStatusText;
        [SerializeField] private Text controllerStatusText;
        [SerializeField] private Text sensorRateText;
        [SerializeField] private Text lastThrowText;
        [SerializeField] private Button resetButton;
        [SerializeField] private Button createSessionButton;
        [SerializeField] private RawImage qrCodeImage;

        [Header("References")]
        [SerializeField] private GatewayConnection gatewayConnection;
        [SerializeField] private BowlingGameController gameController;

        private void Awake()
        {
            if (gatewayConnection == null)
                gatewayConnection = FindObjectOfType<GatewayConnection>();
            
            if (gameController == null)
                gameController = FindObjectOfType<BowlingGameController>();

            if (resetButton != null)
                resetButton.onClick.AddListener(() => gameController?.ForceReset());

            if (createSessionButton != null)
                createSessionButton.onClick.AddListener(CreateSession);
        }

        private void Start()
        {
            SubscribeToEvents();
            UpdateUI();
        }

        private void SubscribeToEvents()
        {
            if (gatewayConnection != null)
            {
                gatewayConnection.OnConnectionChanged += OnGatewayConnectionChanged;
                gatewayConnection.OnPhoneConnected += OnPhoneConnected;
                gatewayConnection.OnPhoneDisconnected += OnPhoneDisconnected;
                gatewayConnection.OnSensorFrame += OnSensorFrame;
                gatewayConnection.OnControllerReady += OnControllerReady;
                gatewayConnection.OnThrowReleased += OnThrowReleased;
                gatewayConnection.OnGatewayInfo += OnGatewayInfo;
            }
        }

        private void OnGatewayConnectionChanged(bool connected)
        {
            UpdateGatewayStatus(connected);
        }

        private void OnPhoneConnected(PhoneConnectedMessage msg)
        {
            UpdatePhoneStatus(true, msg.deviceName);
        }

        private void OnPhoneDisconnected(PhoneDisconnectedMessage msg)
        {
            UpdatePhoneStatus(false, msg.reason);
        }

        private void OnSensorFrame(SensorFrameMessage msg)
        {
            // Update handled in UpdateUI via sensorRateText
        }

        private void OnControllerReady(ControllerReadyMessage msg)
        {
            UpdateControllerStatus(true);
        }

        private void OnThrowReleased(ThrowReleasedMessage msg)
        {
            var p = msg.parameters;
            UpdateLastThrow(p.power, p.direction, p.spin, p.releaseAngle);
        }

        private void OnGatewayInfo(GatewayInfoMessage msg)
        {
            UpdateGatewayStatus(msg.unityConnected);
        }

        private void Update()
        {
            UpdateUI();
        }

        private void UpdateUI()
        {
            if (gatewayConnection != null)
            {
                if (sensorRateText != null)
                    sensorRateText.text = $"Sensor Rate: {gatewayConnection.SensorFrameRate} Hz";
            }
        }

        private void UpdateGatewayStatus(bool connected)
        {
            if (gatewayStatusText != null)
            {
                gatewayStatusText.text = $"Gateway: {(connected ? "Connected" : "Disconnected")}";
                gatewayStatusText.color = connected ? Color.green : Color.red;
            }
        }

        private void UpdatePhoneStatus(bool connected, string info)
        {
            if (phoneStatusText != null)
            {
                phoneStatusText.text = $"Phone: {(connected ? "Connected" : "Disconnected")} - {info}";
                phoneStatusText.color = connected ? Color.green : Color.red;
            }
        }

        private void UpdateControllerStatus(bool ready)
        {
            if (controllerStatusText != null)
            {
                controllerStatusText.text = $"Controller: {(ready ? "Ready" : "Not Ready")}";
                controllerStatusText.color = ready ? Color.green : Color.yellow;
            }
        }

        private void UpdateLastThrow(double power, double direction, double spin, double angle)
        {
            if (lastThrowText != null)
            {
                lastThrowText.text = $"Last Throw:\nPower: {power:F2}\nDirection: {direction:F2}\nSpin: {spin:F2}\nAngle: {angle:F1}°";
            }
        }

        private async void CreateSession()
        {
            if (gatewayConnection == null) return;

            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    var url = $"http://{gatewayConnection.GetType().GetField("gatewayHost", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(gatewayConnection) ?? "127.0.0.1"}:5050/api/session/create";
                    var response = await client.PostAsync(url, null);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var session = JsonUtility.FromJson<SessionCreatedResponse>(json);
                        if (session != null && qrCodeImage != null && !string.IsNullOrEmpty(session.qrCodeBase64))
                        {
                            var texture = new Texture2D(256, 256);
                            var bytes = System.Convert.FromBase64String(session.qrCodeBase64.Replace("data:image/png;base64,", ""));
                            texture.LoadImage(bytes);
                            qrCodeImage.texture = texture;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to create session: {e.Message}");
            }
        }

        [Serializable]
        private class SessionCreatedResponse
        {
            public string sessionToken;
            public string gatewayUrl;
            public int port;
            public long expiresAt;
            public string qrCodeBase64;
        }
    }
}