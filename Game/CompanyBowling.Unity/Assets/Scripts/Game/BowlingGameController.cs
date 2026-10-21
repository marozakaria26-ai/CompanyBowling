namespace CompanyBowling.Game
{
    using UnityEngine;
    using CompanyBowling.Network;
    using CompanyBowling.Protocol;

    public class BowlingGameController : MonoBehaviour
    {
        [Header("Game Objects")]
        [SerializeField] private Rigidbody bowlingBall;
        [SerializeField] private GameObject[] bowlingPins;
        [SerializeField] private Transform ballStartPosition;
        [SerializeField] private Transform laneTransform;
        [SerializeField] private Camera mainCamera;

        [Header("Physics Settings")]
        [SerializeField] private float maxBallSpeed = 25f;
        [SerializeField] private float maxAngularSpeed = 50f;
        [SerializeField] private float directionScale = 1.0f;
        [SerializeField] private float spinScale = 1.0f;
        [SerializeField] private float powerScale = 1.0f;
        [SerializeField] private float releaseAngleScale = 1.0f;

        [Header("Game State")]
        [SerializeField] private bool canThrow = true;
        [SerializeField] private float throwCooldown = 3f;
        [SerializeField] private float laneLength = 18.3f;
        [SerializeField] private float resetDelay = 5f;

        [Header("References")]
        [SerializeField] private GatewayConnection gatewayConnection;

        private Vector3 _originalBallPosition;
        private Quaternion _originalBallRotation;
        private Vector3[] _originalPinPositions;
        private Quaternion[] _originalPinRotations;
        private Rigidbody[] _pinRigidbodies;
        private bool _ballInPlay = false;
        private float _throwTimer = 0f;

        public bool CanThrow => canThrow && !_ballInPlay;
        public bool IsBallInPlay => _ballInPlay;

        private void Awake()
        {
            if (gatewayConnection == null)
                gatewayConnection = FindObjectOfType<GatewayConnection>();

            CacheOriginalPositions();
            SubscribeToGatewayEvents();
        }

        private void Start()
        {
            ResetLane();
        }

        private void Update()
        {
            if (!_ballInPlay) return;

            _throwTimer += Time.deltaTime;

            if (bowlingBall.velocity.magnitude < 0.1f && bowlingBall.angularVelocity.magnitude < 0.1f)
            {
                _throwTimer += Time.deltaTime;
                if (_throwTimer >= resetDelay)
                {
                    ResetLane();
                }
            }

            if (bowlingBall.position.z > laneLength + 2f)
            {
                ResetLane();
            }
        }

        private void CacheOriginalPositions()
        {
            _originalBallPosition = bowlingBall.position;
            _originalBallRotation = bowlingBall.rotation;

            _originalPinPositions = new Vector3[bowlingPins.Length];
            _originalPinRotations = new Quaternion[bowlingPins.Length];
            _pinRigidbodies = new Rigidbody[bowlingPins.Length];

            for (int i = 0; i < bowlingPins.Length; i++)
            {
                _originalPinPositions[i] = bowlingPins[i].transform.position;
                _originalPinRotations[i] = bowlingPins[i].transform.rotation;
                _pinRigidbodies[i] = bowlingPins[i].GetComponent<Rigidbody>();
            }
        }

        private void SubscribeToGatewayEvents()
        {
            if (gatewayConnection == null) return;

            gatewayConnection.OnThrowReleased += HandleThrowReleased;
            gatewayConnection.OnThrowComplete += HandleThrowComplete;
            gatewayConnection.OnResetLane += HandleResetLane;
            gatewayConnection.OnPhoneConnected += HandlePhoneConnected;
            gatewayConnection.OnPhoneDisconnected += HandlePhoneDisconnected;
        }

        private void HandlePhoneConnected(PhoneConnectedMessage msg)
        {
            Debug.Log($"[BowlingGame] Phone connected: {msg.deviceName}");
            canThrow = true;
        }

        private void HandlePhoneDisconnected(PhoneDisconnectedMessage msg)
        {
            Debug.Log($"[BowlingGame] Phone disconnected: {msg.reason}");
            canThrow = false;
        }

        private void HandleThrowReleased(ThrowReleasedMessage msg)
        {
            if (!CanThrow) return;

            var parameters = msg.parameters;
            ExecuteThrow(parameters);
        }

        private void ExecuteThrow(ThrowParameters parameters)
        {
            canThrow = false;
            _ballInPlay = true;
            _throwTimer = 0f;

            bowlingBall.isKinematic = false;
            bowlingBall.position = ballStartPosition.position;
            bowlingBall.rotation = ballStartPosition.rotation;
            bowlingBall.linearVelocity = Vector3.zero;
            bowlingBall.angularVelocity = Vector3.zero;

            var forwardForce = (float)(parameters.power * maxBallSpeed * powerScale);
            var lateralForce = (float)(parameters.direction * maxBallSpeed * directionScale);
            var spinForce = (float)(parameters.spin * maxAngularSpeed * spinScale);
            var launchAngle = (float)(parameters.releaseAngle * Mathf.Deg2Rad * releaseAngleScale);

            var velocity = new Vector3(lateralForce, 0, forwardForce);
            
            if (launchAngle != 0)
            {
                velocity.y = Mathf.Sin(launchAngle) * forwardForce;
                velocity.z = Mathf.Cos(launchAngle) * forwardForce;
            }

            bowlingBall.linearVelocity = velocity;
            bowlingBall.angularVelocity = new Vector3(0, spinForce, 0);

            Debug.Log($"[BowlingGame] Throw executed: Velocity={velocity}, AngularVelocity={bowlingBall.angularVelocity}");
        }

        private void HandleThrowComplete(ThrowCompleteMessage msg)
        {
            Debug.Log($"[BowlingGame] Throw complete received: {msg.pinsKnockedDown} pins");
        }

        private void HandleResetLane(ResetLaneMessage msg)
        {
            ResetLane();
        }

        public void ResetLane()
        {
            bowlingBall.isKinematic = true;
            bowlingBall.position = _originalBallPosition;
            bowlingBall.rotation = _originalBallRotation;
            bowlingBall.linearVelocity = Vector3.zero;
            bowlingBall.angularVelocity = Vector3.zero;

            for (int i = 0; i < bowlingPins.Length; i++)
            {
                var pin = bowlingPins[i];
                var rb = _pinRigidbodies[i];
                
                pin.transform.position = _originalPinPositions[i];
                pin.transform.rotation = _originalPinRotations[i];
                
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.Sleep();
                }
            }

            _ballInPlay = false;
            _throwTimer = 0f;
            
            Invoke(nameof(EnableThrow), throwCooldown);
        }

        private void EnableThrow()
        {
            canThrow = true;
        }

        public void ForceReset()
        {
            CancelInvoke(nameof(EnableThrow));
            ResetLane();
        }

        private void OnDestroy()
        {
            if (gatewayConnection != null)
            {
                gatewayConnection.OnThrowReleased -= HandleThrowReleased;
                gatewayConnection.OnThrowComplete -= HandleThrowComplete;
                gatewayConnection.OnResetLane -= HandleResetLane;
                gatewayConnection.OnPhoneConnected -= HandlePhoneConnected;
                gatewayConnection.OnPhoneDisconnected -= HandlePhoneDisconnected;
            }
        }

        private void OnValidate()
        {
            if (bowlingBall == null)
                bowlingBall = FindObjectOfType<Rigidbody>();
        }
    }
}