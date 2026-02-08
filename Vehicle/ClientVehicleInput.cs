using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using BattleCarSumo.Data;

namespace BattleCarSumo.Vehicle
{
    /// <summary>
    /// Handles client-side input collection and local prediction for vehicle movement.
    /// Sends input to the server and reconciles when receiving authoritative state updates.
    /// </summary>
    public class ClientVehicleInput : NetworkBehaviour
    {
        [SerializeField]
        private ServerVehicleController _serverController;

        [SerializeField]
        private Vector2 _joystickInput = Vector2.zero;

        [SerializeField]
        private float _inputSampleRate = 0.016f; // 60 ticks per second

        /// <summary>
        /// Maximum number of input states to store in the prediction buffer.
        /// </summary>
        [SerializeField]
        private int _inputBufferSize = 256;

        /// <summary>
        /// Circular buffer for storing input history for client-side prediction.
        /// </summary>
        private VehicleInputData[] _inputBuffer;

        /// <summary>
        /// Current index in the circular input buffer.
        /// </summary>
        private int _bufferIndex = 0;

        /// <summary>
        /// Current network tick for input ordering.
        /// </summary>
        private uint _currentTick = 0;

        /// <summary>
        /// Time accumulator for input sending.
        /// </summary>
        private float _inputTimer = 0f;

        /// <summary>
        /// Last received authoritative state for reconciliation.
        /// </summary>
        private VehicleState _lastReceivedState;

        /// <summary>
        /// Whether a reconciliation is currently in progress.
        /// </summary>
        private bool _isReconciling = false;

        /// <summary>
        /// Tolerance for position mismatch before triggering reconciliation (in meters).
        /// </summary>
        [SerializeField]
        private float _reconciliationThreshold = 0.1f;

        /// <summary>
        /// Reference to the Rigidbody for applying predicted movement.
        /// </summary>
        private Rigidbody _rigidbody;

        /// <summary>
        /// Last applied input for the current frame.
        /// </summary>
        private VehicleInputData _lastAppliedInput;

        private void OnEnable()
        {
            _inputBuffer = new VehicleInputData[_inputBufferSize];
            for (int i = 0; i < _inputBuffer.Length; i++)
            {
                _inputBuffer[i] = VehicleInputData.Zero;
            }

            _rigidbody = GetComponent<Rigidbody>();
            _lastReceivedState = new VehicleState();
            _lastAppliedInput = VehicleInputData.Zero;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (!base.Owner.IsLocalClient)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (!IsOwner)
                return;

            // Collect input from appropriate source
            CollectInput();

            // Accumulate time and send input at fixed rate
            _inputTimer += Time.deltaTime;
            while (_inputTimer >= _inputSampleRate)
            {
                _inputTimer -= _inputSampleRate;
                SendInputServerRpc(_lastAppliedInput);
            }
        }

        private void FixedUpdate()
        {
            if (!IsOwner)
                return;

            // Apply predicted movement locally
            if (_serverController != null)
            {
                _serverController.SimulatePhysics(_lastAppliedInput);
            }
        }

        /// <summary>
        /// Collects input from either virtual joystick or keyboard.
        /// </summary>
        private void CollectInput()
        {
#if UNITY_ANDROID || UNITY_IOS
            // Mobile: Use virtual joystick input
            float throttle = _joystickInput.y;
            float steering = _joystickInput.x;
#else
            // Editor/PC: Use keyboard input for testing
            float throttle = 0f;
            float steering = 0f;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
                throttle = 1f;
            else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
                throttle = -1f;

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
                steering = -1f;
            else if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
                steering = 1f;
#endif

            _lastAppliedInput = new VehicleInputData(throttle, steering, _currentTick);
            _currentTick++;

            // Store in history buffer for reconciliation
            _inputBuffer[_bufferIndex] = _lastAppliedInput;
            _bufferIndex = (_bufferIndex + 1) % _inputBufferSize;
        }

        /// <summary>
        /// Sets the virtual joystick input for mobile controls.
        /// </summary>
        /// <param name="input">Joystick input normalized to -1 to 1 range.</param>
        public void SetJoystickInput(Vector2 input)
        {
            _joystickInput = Vector2.ClampMagnitude(input, 1f);
        }

        /// <summary>
        /// Sends input to the server for processing.
        /// </summary>
        /// <param name="input">The input data to send.</param>
        [ServerRpc(RequireOwnership = true)]
        private void SendInputServerRpc(VehicleInputData input)
        {
            if (!IsServerInitialized)
                return;

            _serverController.ProcessInput(input);
        }

        /// <summary>
        /// 서버로부터 권위적 상태를 수신하여 재조정(reconciliation)을 수행합니다.
        /// ServerVehicleController의 TargetRpc에서 호출됩니다.
        /// </summary>
        /// <param name="state">서버의 권위적 차량 상태</param>
        public void ReceiveAuthoritativeState(VehicleState state)
        {
            if (!IsOwner)
                return;

            _lastReceivedState = state;

            // 위치 차이가 임계값을 초과하면 재조정 수행
            float positionDelta = Vector3.Distance(transform.position, state.Position);
            if (positionDelta > _reconciliationThreshold)
            {
                PerformReconciliation(state);
            }
        }

        /// <summary>
        /// Performs client-side reconciliation by replaying inputs from the server state.
        /// </summary>
        /// <param name="authoritativeState">The authoritative state received from the server.</param>
        private void PerformReconciliation(VehicleState authoritativeState)
        {
            if (_isReconciling)
                return;

            _isReconciling = true;

            // Reset to authoritative state
            transform.position = authoritativeState.Position;
            transform.rotation = authoritativeState.Rotation;
            _rigidbody.linearVelocity = authoritativeState.Velocity;
            _rigidbody.angularVelocity = authoritativeState.AngularVelocity;

            // Replay inputs after the server's last processed tick
            for (int i = 0; i < _inputBuffer.Length; i++)
            {
                VehicleInputData bufferedInput = _inputBuffer[i];
                if (bufferedInput.Tick > authoritativeState.Tick)
                {
                    if (_serverController != null)
                    {
                        _serverController.SimulatePhysics(bufferedInput);
                    }
                }
            }

            _isReconciling = false;
        }

        /// <summary>
        /// Gets the current prediction state based on the last applied input.
        /// </summary>
        /// <returns>A VehicleState representing the current predicted state.</returns>
        public VehicleState GetPredictedState()
        {
            return new VehicleState
            {
                Position = transform.position,
                Rotation = transform.rotation,
                Velocity = _rigidbody.linearVelocity,
                AngularVelocity = _rigidbody.angularVelocity,
                Tick = _currentTick - 1
            };
        }
    }
}
