// ============================================================
// ServerVehicleController.cs - 서버 권위적 물리 컨트롤러
// Battle Car Sumo - 1v1 Server Authoritative Physics Battle
// ============================================================

using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using BattleCarSumo.Data;
using BattleCarSumo.Parts;

namespace BattleCarSumo.Vehicle
{
    /// <summary>
    /// 서버 권위적 차량 물리 컨트롤러.
    /// 모든 물리 연산은 서버에서 수행하며, 클라이언트에 상태를 동기화합니다.
    /// 클라이언트 예측(prediction)을 위한 SimulatePhysics 메서드도 제공합니다.
    /// </summary>
    public class ServerVehicleController : NetworkBehaviour
    {
        #region Inspector References

        [SerializeField]
        private GameConfig _gameConfig;

        [SerializeField]
        private PartManager _partManager;

        #endregion

        public readonly SyncVar<WeightClass> _currentWeightClass = new SyncVar<WeightClass>();

        public readonly SyncVar<float> _totalWeight = new SyncVar<float>();

        #region Private Fields

        private Rigidbody _rigidbody;

        [SerializeField]
        private float _syncRate = 0.033f; // ~30 Hz

        [SerializeField]
        private float _downforceMultiplier = 0.5f;

        [SerializeField]
        private float _centerOfMassHeight = -0.3f;

        private float _syncTimer = 0f;
        private uint _currentTick = 0;

        #endregion

        #region Public Properties

        public WeightClass CurrentWeightClass => _currentWeightClass.Value;
        public float TotalWeight => _totalWeight.Value;

        #endregion

        #region Initialization

        private void Awake()
        {
            _currentWeightClass.Value = WeightClass.Middle;
            _totalWeight.Value = 1200f;

            _rigidbody = GetComponent<Rigidbody>();

            if (_rigidbody == null)
            {
                Debug.LogError("[ServerVehicleController] Rigidbody 컴포넌트가 필요합니다!");
                return;
            }

            if (_gameConfig == null)
                _gameConfig = Resources.Load<GameConfig>("GameConfig");

            if (_partManager == null)
                _partManager = GetComponent<PartManager>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            RecalculateTotalWeight();
            ApplyWeightClassPhysics();
            _currentTick = 0;

            Debug.Log($"[ServerVehicleController] 서버 초기화: 체급={_currentWeightClass.Value}, 무게={_totalWeight.Value}kg");
        }

        #endregion

        #region Server Update Loop

        private void FixedUpdate()
        {
            if (!IsServerInitialized)
                return;

            // 주기적으로 권위적 상태를 클라이언트에 전송
            _syncTimer += Time.fixedDeltaTime;
            if (_syncTimer >= _syncRate)
            {
                _syncTimer = 0f;
                SynchronizeStateToClients();
                _currentTick++;
            }
        }

        #endregion

        #region Physics Processing

        /// <summary>
        /// 클라이언트 입력을 받아 물리 적용 (서버에서 호출)
        /// </summary>
        [Server]
        public void ProcessInput(VehicleInputData input)
        {
            if (!IsServerInitialized || _rigidbody == null)
                return;

            SimulatePhysics(input);
        }

        /// <summary>
        /// 물리 시뮬레이션 실행. 서버와 클라이언트 예측 모두에서 사용 가능.
        /// </summary>
        /// <param name="input">적용할 차량 입력 데이터</param>
        public void SimulatePhysics(VehicleInputData input)
        {
            if (_rigidbody == null || _gameConfig == null)
                return;

            WeightClassPhysics physics = _gameConfig.GetPhysicsForClass(_currentWeightClass.Value);

            ApplyThrottleForce(input.Throttle, physics);
            ApplySteeringTorque(input.Steering, physics);
            ApplyDownforce();
            ClampVelocityToMaxSpeed(physics);
        }

        /// <summary>
        /// 스로틀 입력에 따른 전진/후진 힘 적용
        /// </summary>
        private void ApplyThrottleForce(float throttle, WeightClassPhysics physics)
        {
            if (Mathf.Approximately(throttle, 0f))
                return;

            float forceMagnitude = physics.acceleration * throttle * _rigidbody.mass;
            Vector3 force = transform.forward * forceMagnitude;
            _rigidbody.AddForce(force, ForceMode.Force);
        }

        /// <summary>
        /// 조향 입력에 따른 회전 토크 적용
        /// </summary>
        private void ApplySteeringTorque(float steering, WeightClassPhysics physics)
        {
            if (Mathf.Approximately(steering, 0f))
                return;

            float torqueMagnitude = physics.turnSpeed * steering;
            _rigidbody.AddTorque(Vector3.up * torqueMagnitude, ForceMode.Force);
        }

        /// <summary>
        /// 차량을 지면에 붙이는 하향 힘 적용
        /// </summary>
        private void ApplyDownforce()
        {
            float downforce = _rigidbody.mass * Physics.gravity.magnitude * _downforceMultiplier;
            _rigidbody.AddForce(Vector3.down * downforce, ForceMode.Force);
        }

        /// <summary>
        /// 최대 속도 제한
        /// </summary>
        private void ClampVelocityToMaxSpeed(WeightClassPhysics physics)
        {
            if (_rigidbody.linearVelocity.magnitude > physics.maxSpeed)
            {
                _rigidbody.linearVelocity = _rigidbody.linearVelocity.normalized * physics.maxSpeed;
            }
        }

        #endregion

        #region Weight & Physics Configuration

        /// <summary>
        /// 체급별 물리 파라미터를 Rigidbody에 적용
        /// </summary>
        [Server]
        public void ApplyWeightClassPhysics()
        {
            if (_rigidbody == null || _gameConfig == null)
                return;

            WeightClassPhysics physics = _gameConfig.GetPhysicsForClass(_currentWeightClass.Value);

            _rigidbody.mass = _totalWeight.Value;
            _rigidbody.linearDamping = physics.linearDrag;
            _rigidbody.angularDamping = physics.angularDrag;
            _rigidbody.centerOfMass = new Vector3(0f, _centerOfMassHeight, 0f);
        }

        /// <summary>
        /// 총 무게 재계산 (본체 + 부품)
        /// </summary>
        [Server]
        public void RecalculateTotalWeight()
        {
            if (_gameConfig == null)
                return;

            WeightClassPhysics physics = _gameConfig.GetPhysicsForClass(_currentWeightClass.Value);
            _totalWeight.Value = physics.baseMass;

            if (_partManager != null)
                _totalWeight.Value += _partManager.CalculateTotalPartsWeight();

            // 물리 값 갱신
            ApplyWeightClassPhysics();

            Debug.Log($"[ServerVehicleController] 무게 재계산: {_totalWeight.Value}kg (체급: {_currentWeightClass.Value})");
        }

        /// <summary>
        /// 체급 설정 및 물리 갱신
        /// </summary>
        [Server]
        public void SetWeightClass(WeightClass weightClass)
        {
            _currentWeightClass.Value = weightClass;
            RecalculateTotalWeight();
        }

        #endregion

        #region State Synchronization

        /// <summary>
        /// 현재 차량 상태를 모든 관찰 클라이언트에 전송
        /// </summary>
        [Server]
        private void SynchronizeStateToClients()
        {
            VehicleState state = new VehicleState(
                transform.position,
                transform.rotation,
                _rigidbody.linearVelocity,
                _rigidbody.angularVelocity,
                _currentTick
            );

            // 각 관찰자에게 TargetRpc로 전송
            foreach (NetworkConnection conn in Observers)
            {
                SendAuthoritativeStateTargetRpc(conn, state);
            }
        }

        /// <summary>
        /// 특정 클라이언트에 권위적 상태 전송 (클라이언트 재조정용)
        /// </summary>
        [TargetRpc]
        private void SendAuthoritativeStateTargetRpc(NetworkConnection conn, VehicleState state)
        {
            // ClientVehicleInput이 이 상태를 받아 재조정(reconciliation)에 사용
            ClientVehicleInput clientInput = GetComponent<ClientVehicleInput>();
            if (clientInput != null)
            {
                clientInput.ReceiveAuthoritativeState(state);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 현재 물리 상태 스냅샷 반환
        /// </summary>
        public VehicleState GetCurrentState()
        {
            return new VehicleState(
                transform.position,
                transform.rotation,
                _rigidbody != null ? _rigidbody.linearVelocity : Vector3.zero,
                _rigidbody != null ? _rigidbody.angularVelocity : Vector3.zero,
                _currentTick
            );
        }

        /// <summary>
        /// 외부 충격력 적용 (부품 효과, 충돌 등)
        /// </summary>
        [Server]
        public void ApplyImpulse(Vector3 force, Vector3 position)
        {
            if (_rigidbody != null)
                _rigidbody.AddForceAtPosition(force, position, ForceMode.Impulse);
        }

        /// <summary>
        /// 외부 충격력 적용 (중심에)
        /// </summary>
        [Server]
        public void ApplyImpulse(Vector3 force)
        {
            if (_rigidbody != null)
                _rigidbody.AddForce(force, ForceMode.Impulse);
        }

        /// <summary>
        /// 차량 위치/속도 리셋 (라운드 시작 시)
        /// </summary>
        [Server]
        public void ResetVehicle(Vector3 position, Quaternion rotation)
        {
            if (_rigidbody == null)
                return;

            transform.position = position;
            transform.rotation = rotation;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
        }

        #endregion
    }
}
