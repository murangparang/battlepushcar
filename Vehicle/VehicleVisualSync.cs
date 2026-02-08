using FishNet.Object;
using UnityEngine;

namespace BattleCarSumo.Vehicle
{
    /// <summary>
    /// Handles smooth visual interpolation of vehicle state on non-authoritative clients.
    /// Separates visual representation from physics body for smooth gameplay experience.
    /// </summary>
    public class VehicleVisualSync : NetworkBehaviour
    {
        /// <summary>
        /// Transform component for visual representation of the vehicle.
        /// This is kept separate from the physics body for smooth interpolation.
        /// </summary>
        [SerializeField]
        private Transform _visualTransform;

        /// <summary>
        /// Speed at which to interpolate between network states.
        /// Higher values result in faster interpolation to the target position.
        /// </summary>
        [SerializeField]
        private float _interpolationSpeed = 8f;

        /// <summary>
        /// Maximum number of buffered states for interpolation.
        /// </summary>
        [SerializeField]
        private int _stateBufferSize = 32;

        /// <summary>
        /// Tolerance for position difference before snapping to target (in meters).
        /// </summary>
        [SerializeField]
        private float _snapThreshold = 5f;

        /// <summary>
        /// Circular buffer for storing received network states with timestamps.
        /// </summary>
        private struct BufferedState
        {
            public VehicleState State;
            public float ReceiveTime;
        }

        /// <summary>
        /// Buffer of network states for smooth interpolation.
        /// </summary>
        private BufferedState[] _stateBuffer;

        /// <summary>
        /// Current index in the circular state buffer.
        /// </summary>
        private int _bufferIndex = 0;

        /// <summary>
        /// Number of valid states currently in the buffer.
        /// </summary>
        private int _bufferCount = 0;

        /// <summary>
        /// Target state being interpolated towards.
        /// </summary>
        private VehicleState _targetState;

        /// <summary>
        /// Previous state for interpolation calculations.
        /// </summary>
        private VehicleState _previousState;

        /// <summary>
        /// Time of the last received network update.
        /// </summary>
        private float _lastUpdateTime = 0f;

        /// <summary>
        /// Time of the previous network update.
        /// </summary>
        private float _previousUpdateTime = 0f;

        /// <summary>
        /// Current interpolation progress (0 to 1).
        /// </summary>
        private float _interpolationFactor = 0f;

        /// <summary>
        /// Reference to the local input controller if this is the owner.
        /// </summary>
        private ClientVehicleInput _clientInput;

        /// <summary>
        /// Reference to the physics rigidbody.
        /// </summary>
        private Rigidbody _rigidbody;

        private void OnEnable()
        {
            _stateBuffer = new BufferedState[_stateBufferSize];
            _rigidbody = GetComponent<Rigidbody>();
            _clientInput = GetComponent<ClientVehicleInput>();

            if (_visualTransform == null)
            {
                _visualTransform = transform;
            }

            _targetState = new VehicleState();
            _previousState = new VehicleState();
            _lastUpdateTime = Time.time;
            _previousUpdateTime = Time.time;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (base.Owner.IsLocalClient)
            {
                // Owner shows predicted visual state
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (IsOwner)
                return;

            // Non-owning clients interpolate between received states
            UpdateVisualInterpolation();
        }

        /// <summary>
        /// Updates the visual transform with interpolated state.
        /// </summary>
        private void UpdateVisualInterpolation()
        {
            if (_bufferCount == 0)
                return;

            // Calculate time delta since last update
            float timeSinceLastUpdate = Time.time - _lastUpdateTime;

            // If we have multiple states, interpolate between them
            if (_bufferCount > 1)
            {
                float timeBetweenUpdates = _lastUpdateTime - _previousUpdateTime;
                if (timeBetweenUpdates > 0)
                {
                    _interpolationFactor = Mathf.Clamp01(timeSinceLastUpdate / timeBetweenUpdates);
                }
                else
                {
                    _interpolationFactor = 1f;
                }

                VehicleState interpolatedState = VehicleState.Lerp(_previousState, _targetState, _interpolationFactor);
                ApplyVisualState(interpolatedState);
            }
            else
            {
                // Only one state, smoothly move towards it
                ApplyVisualState(_targetState);
            }
        }

        /// <summary>
        /// Applies a vehicle state to the visual transform.
        /// </summary>
        /// <param name="state">The state to apply visually.</param>
        private void ApplyVisualState(VehicleState state)
        {
            float positionDistance = Vector3.Distance(_visualTransform.position, state.Position);

            // Snap if the distance is too large (indicates teleport or major desync)
            if (positionDistance > _snapThreshold)
            {
                _visualTransform.position = state.Position;
                _visualTransform.rotation = state.Rotation;
            }
            else
            {
                // Smoothly interpolate position
                Vector3 targetPosition = Vector3.Lerp(
                    _visualTransform.position,
                    state.Position,
                    Time.deltaTime * _interpolationSpeed
                );
                _visualTransform.position = targetPosition;

                // Smoothly interpolate rotation
                Quaternion targetRotation = Quaternion.Lerp(
                    _visualTransform.rotation,
                    state.Rotation,
                    Time.deltaTime * _interpolationSpeed
                );
                _visualTransform.rotation = targetRotation;
            }
        }

        /// <summary>
        /// Receives a network state update and buffers it for interpolation.
        /// Called when the server sends state updates to clients.
        /// </summary>
        /// <param name="newState">The new vehicle state from the network.</param>
        public void ReceiveNetworkState(VehicleState newState)
        {
            if (IsOwner)
                return;

            // Store previous state
            _previousState = _targetState;
            _previousUpdateTime = _lastUpdateTime;

            // Update target state
            _targetState = newState;
            _lastUpdateTime = Time.time;
            _interpolationFactor = 0f;

            // Add to circular buffer
            BufferedState bufferedState = new BufferedState
            {
                State = newState,
                ReceiveTime = Time.time
            };

            _stateBuffer[_bufferIndex] = bufferedState;
            _bufferIndex = (_bufferIndex + 1) % _stateBufferSize;

            if (_bufferCount < _stateBufferSize)
            {
                _bufferCount++;
            }

            // Sync rigidbody with network state for physics calculations on non-owner clients
            if (_rigidbody != null && !IsOwner)
            {
                _rigidbody.linearVelocity = newState.Velocity;
                _rigidbody.angularVelocity = newState.AngularVelocity;
            }
        }

        /// <summary>
        /// Gets the number of buffered states currently available for interpolation.
        /// </summary>
        /// <returns>The count of buffered states.</returns>
        public int GetBufferCount()
        {
            return _bufferCount;
        }

        /// <summary>
        /// Clears the state buffer and resets interpolation.
        /// </summary>
        public void ClearBuffer()
        {
            _bufferCount = 0;
            _bufferIndex = 0;
            _interpolationFactor = 0f;
        }

        /// <summary>
        /// Sets the interpolation speed for visual smoothness.
        /// </summary>
        /// <param name="speed">New interpolation speed value.</param>
        public void SetInterpolationSpeed(float speed)
        {
            _interpolationSpeed = Mathf.Max(0.1f, speed);
        }

        /// <summary>
        /// Sets the snap threshold for detecting major desynchronization.
        /// </summary>
        /// <param name="threshold">Distance threshold in meters.</param>
        public void SetSnapThreshold(float threshold)
        {
            _snapThreshold = Mathf.Max(0.1f, threshold);
        }

        /// <summary>
        /// Gets the current visual position (may be interpolated or predicted).
        /// </summary>
        /// <returns>The visual transform position.</returns>
        public Vector3 GetVisualPosition()
        {
            return _visualTransform.position;
        }

        /// <summary>
        /// Gets the current visual rotation (may be interpolated or predicted).
        /// </summary>
        /// <returns>The visual transform rotation.</returns>
        public Quaternion GetVisualRotation()
        {
            return _visualTransform.rotation;
        }

        /// <summary>
        /// Instantly snaps the visual representation to the provided state.
        /// Useful for addressing major desynchronization.
        /// </summary>
        /// <param name="state">The state to snap to.</param>
        public void SnapToState(VehicleState state)
        {
            _visualTransform.position = state.Position;
            _visualTransform.rotation = state.Rotation;
            _targetState = state;
            _previousState = state;
            _interpolationFactor = 1f;
            _lastUpdateTime = Time.time;
        }
    }
}
