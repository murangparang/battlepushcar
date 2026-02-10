// ============================================================
// OfflineVehicleController.cs - 오프라인 테스트용 차량 컨트롤러
// 네트워크 의존성 없이 물리 기반 차량 이동 + 부품 액션 테스트
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using BattleCarSumo.Data;
using BattleCarSumo.Audio;

namespace BattleCarSumo.Test
{
    [RequireComponent(typeof(Rigidbody))]
    public class OfflineVehicleController : MonoBehaviour
    {
        #region Inspector Settings

        [Header("=== 물리 설정 ===")]
        [SerializeField] private WeightClass _weightClass = WeightClass.Middle;
        [SerializeField] private float _downforceMultiplier = 0.5f;
        [SerializeField] private float _centerOfMassHeight = -0.3f;

        [Header("=== 부품 액션 ===")]
        [SerializeField] private float _frontActionForce = 15f;
        [SerializeField] private float _frontActionCooldown = 2f;
        [SerializeField] private float _rooftopActionForce = 12f;
        [SerializeField] private float _rooftopActionCooldown = 3f;
        [SerializeField] private float _rearBoostForce = 20f;
        [SerializeField] private float _rearBoostCooldown = 4f;

        [Header("=== 참조 ===")]
        [SerializeField] private GameConfig _gameConfig;
        [SerializeField] private Transform _frontAttachPoint;
        [SerializeField] private Transform _rooftopAttachPoint;
        [SerializeField] private Transform _rearAttachPoint;

        [Header("=== 입력 설정 ===")]
        [SerializeField] private bool _isPlayer1 = true;

        #endregion

        #region Private Fields

        private Rigidbody _rigidbody;
        private float _lastFrontActionTime = -100f;
        private float _lastRooftopActionTime = -100f;
        private float _lastRearActionTime = -100f;

        // 입력값 저장
        private float _inputThrottle = 0f;
        private float _inputSteering = 0f;

        // ★ OnGUI Event 기반 키 추적 (Input System 폴백)
        private static HashSet<KeyCode> _heldKeys = new HashSet<KeyCode>();
        private static bool _guiInputReady = false;

        // 부품 비주얼 효과용
        private Renderer _frontPartRenderer;
        private Renderer _rooftopPartRenderer;
        private Renderer _rearPartRenderer;
        private Color _frontOriginalColor;
        private Color _rooftopOriginalColor;
        private Color _rearOriginalColor;

        // 폴백 물리값
        private static readonly WeightClassPhysics FALLBACK_PHYSICS = new WeightClassPhysics
        {
            maxSpeed = 15f,
            acceleration = 25f,
            turnSpeed = 8f,
            baseMass = 100f,
            linearDrag = 2f,
            angularDrag = 5f
        };

        // 디버그
        public string LastInputDebug { get; private set; } = "none";

        #endregion

        #region Properties

        public WeightClass CurrentWeightClass => _weightClass;
        public Rigidbody VehicleRigidbody => _rigidbody;
        public bool IsPlayer1 => _isPlayer1;
        public bool IsFrozen { get; private set; } = true;
        public bool HasGameConfig => _gameConfig != null;

        /// <summary>
        /// ★ 매니저가 물리를 직접 제어할 때 true → 차량 자체 FixedUpdate 비활성화
        /// </summary>
        public bool ControlledByManager { get; set; } = false;
        public float InputThrottle => _inputThrottle;
        public float InputSteering => _inputSteering;

        #endregion

        #region Initialization

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_gameConfig == null)
                _gameConfig = Resources.Load<GameConfig>("GameConfig");

            ApplyWeightClassPhysics();
            CachePartRenderers();
        }

        private void CachePartRenderers()
        {
            if (_frontAttachPoint != null)
            {
                _frontPartRenderer = _frontAttachPoint.GetComponentInChildren<Renderer>();
                if (_frontPartRenderer != null)
                    _frontOriginalColor = _frontPartRenderer.material.color;
            }
            if (_rooftopAttachPoint != null)
            {
                _rooftopPartRenderer = _rooftopAttachPoint.GetComponentInChildren<Renderer>();
                if (_rooftopPartRenderer != null)
                    _rooftopOriginalColor = _rooftopPartRenderer.material.color;
            }
            if (_rearAttachPoint != null)
            {
                _rearPartRenderer = _rearAttachPoint.GetComponentInChildren<Renderer>();
                if (_rearPartRenderer != null)
                    _rearOriginalColor = _rearPartRenderer.material.color;
            }
        }

        #endregion

        #region OnGUI 키 추적 (Input System 보완)

        /// <summary>
        /// OnGUI에서 Event.current로 키 상태를 추적합니다.
        /// Input System이 작동하지 않을 때의 폴백입니다.
        /// </summary>
        private void OnGUI()
        {
            Event e = Event.current;
            if (e == null) return;

            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                _heldKeys.Add(e.keyCode);
                _guiInputReady = true;
            }
            else if (e.type == EventType.KeyUp && e.keyCode != KeyCode.None)
            {
                _heldKeys.Remove(e.keyCode);
            }
        }

        /// <summary>
        /// OnGUI 키 추적으로 키가 눌려있는지 확인
        /// </summary>
        private static bool IsKeyHeld(KeyCode key)
        {
            return _heldKeys.Contains(key);
        }

        #endregion

        #region Update Loop

        private void Update()
        {
            ReadInput();
            if (IsFrozen) return;
            HandlePartActions();
        }

        private void FixedUpdate()
        {
            if (IsFrozen) return;
            // ★ 매니저가 제어 중이면 차량 자체 물리 비활성화 (간섭 방지)
            if (ControlledByManager) return;
            SimulatePhysics(_inputThrottle, _inputSteering);
        }

        /// <summary>
        /// 입력 읽기: Input System 우선 → OnGUI Event 폴백
        /// </summary>
        private void ReadInput()
        {
            _inputThrottle = 0f;
            _inputSteering = 0f;

            bool usedInputSystem = false;

            // 방법 1: Input System (Keyboard.current)
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (_isPlayer1)
                {
                    if (kb.wKey.isPressed) { _inputThrottle += 1f; usedInputSystem = true; }
                    if (kb.sKey.isPressed) { _inputThrottle -= 1f; usedInputSystem = true; }
                    if (kb.dKey.isPressed) { _inputSteering += 1f; usedInputSystem = true; }
                    if (kb.aKey.isPressed) { _inputSteering -= 1f; usedInputSystem = true; }
                }
                else
                {
                    if (kb.upArrowKey.isPressed) { _inputThrottle += 1f; usedInputSystem = true; }
                    if (kb.downArrowKey.isPressed) { _inputThrottle -= 1f; usedInputSystem = true; }
                    if (kb.rightArrowKey.isPressed) { _inputSteering += 1f; usedInputSystem = true; }
                    if (kb.leftArrowKey.isPressed) { _inputSteering -= 1f; usedInputSystem = true; }
                }
            }

            // 방법 2: OnGUI Event 기반 폴백 (Input System이 입력을 못 잡을 때)
            if (!usedInputSystem && _guiInputReady)
            {
                if (_isPlayer1)
                {
                    if (IsKeyHeld(KeyCode.W)) _inputThrottle += 1f;
                    if (IsKeyHeld(KeyCode.S)) _inputThrottle -= 1f;
                    if (IsKeyHeld(KeyCode.D)) _inputSteering += 1f;
                    if (IsKeyHeld(KeyCode.A)) _inputSteering -= 1f;
                }
                else
                {
                    if (IsKeyHeld(KeyCode.UpArrow)) _inputThrottle += 1f;
                    if (IsKeyHeld(KeyCode.DownArrow)) _inputThrottle -= 1f;
                    if (IsKeyHeld(KeyCode.RightArrow)) _inputSteering += 1f;
                    if (IsKeyHeld(KeyCode.LeftArrow)) _inputSteering -= 1f;
                }
            }

            // 디버그 정보
            string method = usedInputSystem ? "InputSys" : (_guiInputReady ? "OnGUI" : "NONE");
            if (_inputThrottle != 0f || _inputSteering != 0f)
                LastInputDebug = $"{method} T={_inputThrottle:F0} S={_inputSteering:F0}";
            else
                LastInputDebug = $"{method} (대기)";
        }

        #endregion

        #region Physics

        private WeightClassPhysics GetCurrentPhysics()
        {
            if (_gameConfig != null)
                return _gameConfig.GetPhysicsForClass(_weightClass);
            return FALLBACK_PHYSICS;
        }

        private void SimulatePhysics(float throttle, float steering)
        {
            if (_rigidbody == null) return;

            WeightClassPhysics physics = GetCurrentPhysics();
            ApplyThrottleForce(throttle, physics);
            ApplySteeringTorque(steering, physics);
            ApplyDownforce();
            ClampVelocityToMaxSpeed(physics);
        }

        private void ApplyThrottleForce(float throttle, WeightClassPhysics physics)
        {
            if (Mathf.Approximately(throttle, 0f)) return;
            float forceMagnitude = physics.acceleration * throttle * _rigidbody.mass;
            _rigidbody.AddForce(transform.forward * forceMagnitude, ForceMode.Force);
        }

        private void ApplySteeringTorque(float steering, WeightClassPhysics physics)
        {
            if (Mathf.Approximately(steering, 0f)) return;
            _rigidbody.AddTorque(Vector3.up * (physics.turnSpeed * steering), ForceMode.Force);
        }

        private void ApplyDownforce()
        {
            float downforce = _rigidbody.mass * Physics.gravity.magnitude * _downforceMultiplier;
            _rigidbody.AddForce(Vector3.down * downforce, ForceMode.Force);
        }

        private void ClampVelocityToMaxSpeed(WeightClassPhysics physics)
        {
            if (_rigidbody.linearVelocity.magnitude > physics.maxSpeed)
                _rigidbody.linearVelocity = _rigidbody.linearVelocity.normalized * physics.maxSpeed;
        }

        #endregion

        #region Weight Class

        public void ApplyWeightClassPhysics()
        {
            if (_rigidbody == null) return;
            WeightClassPhysics physics = GetCurrentPhysics();
            _rigidbody.mass = physics.baseMass;
            _rigidbody.linearDamping = physics.linearDrag;
            _rigidbody.angularDamping = physics.angularDrag;
            _rigidbody.centerOfMass = new Vector3(0f, _centerOfMassHeight, 0f);
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public void SetWeightClass(WeightClass weightClass)
        {
            _weightClass = weightClass;
            ApplyWeightClassPhysics();
        }

        public void InjectGameConfig(GameConfig config)
        {
            if (config != null)
            {
                _gameConfig = config;
                ApplyWeightClassPhysics();
            }
        }

        #endregion

        #region Part Actions

        private void HandlePartActions()
        {
            bool qPressed = false, ePressed = false, rPressed = false;

            // Input System
            var kb = Keyboard.current;
            if (kb != null && _isPlayer1)
            {
                qPressed = kb.qKey.wasPressedThisFrame;
                ePressed = kb.eKey.wasPressedThisFrame;
                rPressed = kb.rKey.wasPressedThisFrame;
            }
            else if (kb != null && !_isPlayer1)
            {
                qPressed = kb.numpad1Key.wasPressedThisFrame;
                ePressed = kb.numpad2Key.wasPressedThisFrame;
                rPressed = kb.numpad3Key.wasPressedThisFrame;
            }

            if (qPressed) { TryFrontAction(); if (AudioManager.Instance != null) AudioManager.Instance.PlayPunchHit(); }
            if (ePressed) { TryRooftopAction(); if (AudioManager.Instance != null) AudioManager.Instance.PlayLift(); }
            if (rPressed) { TryRearAction(); if (AudioManager.Instance != null) AudioManager.Instance.PlayBoost(); }
        }

        private void TryFrontAction()
        {
            if (Time.time - _lastFrontActionTime < _frontActionCooldown) return;
            _lastFrontActionTime = Time.time;

            Collider[] hits = Physics.OverlapSphere(transform.position + transform.forward * 2f, 3f);
            foreach (var hit in hits)
            {
                OfflineVehicleController other = hit.GetComponentInParent<OfflineVehicleController>();
                if (other != null && other != this)
                {
                    Vector3 dir = (other.transform.position - transform.position).normalized;
                    other.VehicleRigidbody.linearVelocity = Vector3.zero;
                    other.VehicleRigidbody.AddForce(dir * _frontActionForce, ForceMode.Impulse);
                }
            }
            FlashPartColor(_frontPartRenderer, Color.yellow, _frontOriginalColor);
        }

        private void TryRooftopAction()
        {
            if (Time.time - _lastRooftopActionTime < _rooftopActionCooldown) return;
            _lastRooftopActionTime = Time.time;

            Collider[] hits = Physics.OverlapSphere(transform.position + transform.forward * 1.5f, 2.5f);
            foreach (var hit in hits)
            {
                OfflineVehicleController other = hit.GetComponentInParent<OfflineVehicleController>();
                if (other != null && other != this)
                    other.VehicleRigidbody.AddForce(Vector3.up * _rooftopActionForce, ForceMode.Impulse);
            }
            FlashPartColor(_rooftopPartRenderer, Color.cyan, _rooftopOriginalColor);
        }

        private void TryRearAction()
        {
            if (Time.time - _lastRearActionTime < _rearBoostCooldown) return;
            _lastRearActionTime = Time.time;
            _rigidbody.AddForce(transform.forward * _rearBoostForce, ForceMode.Impulse);
            FlashPartColor(_rearPartRenderer, Color.red, _rearOriginalColor);
        }

        #endregion

        #region Visual Effects

        private void FlashPartColor(Renderer renderer, Color flashColor, Color originalColor)
        {
            if (renderer == null) return;
            renderer.material.color = flashColor;
            StartCoroutine(ResetColorCoroutine(renderer, originalColor, 0.3f));
        }

        private System.Collections.IEnumerator ResetColorCoroutine(Renderer renderer, Color originalColor, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (renderer != null)
                renderer.material.color = originalColor;
        }

        #endregion

        #region Public Methods

        public void Freeze()
        {
            IsFrozen = true;
            if (_rigidbody == null)
                _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody != null)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
            }
        }

        public void Unfreeze()
        {
            IsFrozen = false;
            // ★ _rigidbody가 null이면 다시 가져오기
            if (_rigidbody == null)
                _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = false;
                Debug.Log($"[Vehicle] Unfreeze: isKinematic={_rigidbody.isKinematic}");
            }
            else
            {
                Debug.LogError("[Vehicle] Unfreeze: Rigidbody를 찾을 수 없습니다!");
            }
        }

        public void ResetVehicle(Vector3 position, Quaternion rotation)
        {
            if (_rigidbody == null)
                _rigidbody = GetComponent<Rigidbody>();
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            transform.position = position;
            transform.rotation = rotation;
            IsFrozen = true;
        }

        public void ApplyExternalImpulse(Vector3 force)
        {
            if (_rigidbody != null)
                _rigidbody.AddForce(force, ForceMode.Impulse);
        }

        #endregion
    }
}
