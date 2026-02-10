// ============================================================
// OfflineTestManager.cs - 오프라인 테스트 씬 관리자
// 네트워크 없이 로컬에서 1v1 테스트 가능
// ============================================================

using UnityEngine;
using UnityEngine.InputSystem;
using BattleCarSumo.Data;
using BattleCarSumo.Audio;

namespace BattleCarSumo.Test
{
    /// <summary>
    /// 오프라인 테스트용 게임 매니저.
    /// 아레나 범위 체크, 라운드 관리, 점수 계산을 로컬에서 처리합니다.
    /// </summary>
    public class OfflineTestManager : MonoBehaviour
    {
        #region Inspector Settings

        [Header("=== 참조 ===")]
        [SerializeField] private GameConfig _gameConfig;
        [SerializeField] private OfflineVehicleController _player1;
        [SerializeField] private OfflineVehicleController _player2;
        [SerializeField] private Transform _arenaCenter;

        [Header("=== 테스트 설정 ===")]
        [SerializeField] private bool _enableAI = true;
        [SerializeField] private float _aiReactionDelay = 0.3f;

        #endregion

        #region State

        private int _player1Score = 0;
        private int _player2Score = 0;
        private int _currentRound = 1;
        private float _roundTimer = 0f;
        private bool _roundActive = false;
        private float _countdownTimer = 0f;
        private GameState _currentState = GameState.WaitingForPlayers;

        // 라운드 시작 후 유예 기간 (미끄러짐 등 방지)
        private float _roundStartGraceTimer = 0f;
        private const float ROUND_START_GRACE = 1.5f; // 시작 후 1.5초 유예

        // 오디오
        private AudioManager _audioManager;
        private int _lastCountdownNumber = -1;

        // ★ 부품 시스템
        private OfflinePartSystem _partSystem;
        private bool _partUIOpen = false;
        private PartSlot _partUISelectedSlot = PartSlot.Front;
        private float _intermissionTimer = 0f;

        // AI
        private float _aiNextActionTime = 0f;
        private float _aiSteeringTarget = 0f;

        // ★ UI 캐시 (매 프레임 Texture2D 생성 방지)
        private Texture2D _uiTexWhite;
        private readonly System.Collections.Generic.Dictionary<uint, Texture2D> _uiTexCache
            = new System.Collections.Generic.Dictionary<uint, Texture2D>();
        private Font _uiFont;
        private bool _uiFontLoaded = false;

        // ★ 아레나 타입
        private ArenaType _selectedArenaType = ArenaType.Classic;
        private GameObject _envRoot;

        // ★ 랜딩 페이지 단계 (0=경기장, 1=체급, 2=부품/준비)
        private int _landingStep = 0;

        // ★ 펀칭머신 (Punching 아레나)
        private GameObject[] _punchingMachines;
        private GameObject[] _punchPistons;
        private Vector3[] _punchPositions;
        private float[] _punchCooldowns = new float[4];
        private float[] _punchAnimTimers = new float[4];
        private const float PUNCH_FORCE = 35f;
        private const float PUNCH_RANGE = 4f;
        private const float PUNCH_COOLDOWN = 3f;
        private const float PUNCH_ANIM_DURATION = 0.3f;
        private const float PUNCHING_ARENA_RADIUS = 45f;
        private const float SQUARE_ARENA_HALF_SIZE = 26f;

        // ★ 동적 카메라
        private Vector3 _cameraOffset = new Vector3(0f, 25f, -20f);
        private float _cameraCurrentZoom = 1f;  // 부드럽게 보간되는 현재 줌
        private Vector3 _cameraCurrentLookAt = Vector3.zero;  // 부드럽게 보간되는 시점
        private const float CAM_MIN_ZOOM = 0.8f;   // 최소 줌 (가까이)
        private const float CAM_MAX_ZOOM = 2.5f;    // 최대 줌 (멀리)
        private const float CAM_ZOOM_SPEED = 2.5f;  // 줌 보간 속도
        private const float CAM_LOOK_SPEED = 4f;    // 시점 보간 속도
        private const float CAM_PLAYER_DIST_MIN = 8f;   // 이 거리 이하 → 최소 줌
        private const float CAM_PLAYER_DIST_MAX = 40f;   // 이 거리 이상 → 최대 줌

        #endregion

        #region Initialization

        private void Start()
        {
            if (_gameConfig == null)
                _gameConfig = Resources.Load<GameConfig>("GameConfig");

            // 차량에 GameConfig 주입 (Inspector 미설정 시 대비)
            if (_gameConfig != null)
            {
                if (_player1 != null) _player1.InjectGameConfig(_gameConfig);
                if (_player2 != null) _player2.InjectGameConfig(_gameConfig);
            }
            else
            {
                Debug.LogWarning("[TestManager] GameConfig 없음! 폴백 값 사용");
            }

            // ★ Player 1은 매니저가 직접 제어 (차량 자체 FixedUpdate 비활성화)
            if (_player1 != null)
            {
                _player1.ControlledByManager = true;

                // ★★★ GameConfig 물리값 전체 출력 (디버그)
                if (_gameConfig != null)
                {
                    WeightClassPhysics p = _gameConfig.GetPhysicsForClass(_player1.CurrentWeightClass);
                    Debug.Log($"[Manager] ★ GameConfig Physics ({_player1.CurrentWeightClass}): " +
                              $"accel={p.acceleration}, turnSpd={p.turnSpeed}, maxSpd={p.maxSpeed}, " +
                              $"mass={p.baseMass}, linDrag={p.linearDrag}, angDrag={p.angularDrag}");
                }

                Rigidbody rb1 = _player1.GetComponent<Rigidbody>();
                if (rb1 != null)
                {
                    Debug.Log($"[Manager] P1 RB: mass={rb1.mass}, drag={rb1.linearDamping}, " +
                              $"angDrag={rb1.angularDamping}, constraints={rb1.constraints}, " +
                              $"kin={rb1.isKinematic}, gravity={rb1.useGravity}, " +
                              $"interp={rb1.interpolation}, collDet={rb1.collisionDetectionMode}");
                }
            }

            // P2도 매니저 제어
            if (_player2 != null)
                _player2.ControlledByManager = true;

            // ★ 오디오 매니저 생성
            if (AudioManager.Instance == null)
            {
                GameObject audioObj = new GameObject("AudioManager");
                _audioManager = audioObj.AddComponent<AudioManager>();
            }
            else
            {
                _audioManager = AudioManager.Instance;
            }

            // ★ 부품 시스템 초기화
            _partSystem = new OfflinePartSystem();

            BuildEnvironment();

            // 랜딩 페이지로 시작 (부품 UI는 버튼으로 열기)
            _partUIOpen = false;
            _currentState = GameState.WaitingForPlayers;
        }

        // ★ 자체 물리 시스템 (AddForce 대신 직접 속도 제어)
        private Vector3 _p1Velocity = Vector3.zero;
        private float _p1AngularVel = 0f;
        private string _fixedUpdateDebug = "waiting";
        private float _lastAppliedForce = 0f;
        private float _p1Speed = 0f;

        // 캐시된 Rigidbody (매 프레임 GetComponent 방지)
        private Rigidbody _p1Rb;
        private Rigidbody _p2Rb;

        /// <summary>
        /// ★★★ FixedUpdate: 자체 물리 시스템 (AddForce 미사용) ★★★
        /// rb.MovePosition으로 충돌 감지 유지 + 직접 속도 제어
        /// </summary>
        private void FixedUpdate()
        {
            // P1 물리
            if (_player1 != null && !_player1.IsFrozen)
            {
                if (_p1Rb == null) _p1Rb = _player1.GetComponent<Rigidbody>();
                if (_p1Rb != null)
                    SimulateVehiclePhysics(_player1, _p1Rb, _p1Throttle, _p1Steering,
                                            ref _p1Velocity, ref _p1AngularVel);
            }

            // P2 AI 물리
            FixedUpdateAI();

            // 디버그
            _p1Speed = _p1Velocity.magnitude;
            if (_player1 == null)
                _fixedUpdateDebug = "P1=null";
            else if (_player1.IsFrozen)
                _fixedUpdateDebug = "FROZEN";
            else if (_p1Throttle == 0f && _p1Steering == 0f)
                _fixedUpdateDebug = $"idle spd={_p1Speed:F1}";
            else
                _fixedUpdateDebug = $"spd={_p1Speed:F1} pos={_player1.transform.position.x:F1},{_player1.transform.position.z:F1}";

            // ★ 엔진 사운드 업데이트
            if (_audioManager != null)
                _audioManager.UpdateEngineSpeed(_p1Velocity.magnitude, _p2Velocity.magnitude);
        }

        /// <summary>
        /// 차량 물리 시뮬레이션 (AddForce 미사용, rb.MovePosition으로 충돌 유지)
        /// </summary>
        private void SimulateVehiclePhysics(OfflineVehicleController vehicle, Rigidbody rb,
                                             float throttle, float steering,
                                             ref Vector3 velocity, ref float angularVel)
        {
            float dt = Time.fixedDeltaTime;

            // --- 물리 파라미터 ---
            float maxSpeed = 12f;
            float accelRate = 20f;    // 가속도 (m/s²)
            float brakeRate = 30f;    // 감속도 (m/s²)
            float friction = 8f;      // 마찰 감속 (m/s²)
            float turnSpeed = 180f;   // 회전 속도 (deg/s)

            // GameConfig에서 값 가져오기 (있으면)
            if (_gameConfig != null)
            {
                WeightClassPhysics phys = _gameConfig.GetPhysicsForClass(vehicle.CurrentWeightClass);
                if (phys.maxSpeed > 0f) maxSpeed = phys.maxSpeed;
                if (phys.acceleration > 0f) accelRate = phys.acceleration;
                if (phys.turnSpeed > 0f) turnSpeed = phys.turnSpeed;
            }

            // --- 1. 회전 ---
            if (steering != 0f)
            {
                // 속도에 비례하여 회전 (정지 시에도 최소 회전)
                float speedFactor = Mathf.Max(0.3f, Mathf.Clamp01(velocity.magnitude / 3f));
                float rotAmount = turnSpeed * steering * speedFactor * dt;
                vehicle.transform.Rotate(Vector3.up, rotAmount, Space.World);

                // 속도 벡터도 회전에 맞춰 변환
                if (velocity.magnitude > 0.1f)
                {
                    velocity = Quaternion.AngleAxis(rotAmount, Vector3.up) * velocity;
                }
            }

            // --- 2. 가속 / 감속 ---
            Vector3 forward = vehicle.transform.forward;
            forward.y = 0f;
            forward.Normalize();

            if (throttle != 0f)
            {
                // 가속 방향이 현재 속도 방향과 반대면 브레이크로 처리
                float forwardSpeed = Vector3.Dot(velocity, forward);
                bool isBraking = (throttle > 0f && forwardSpeed < -0.5f) ||
                                 (throttle < 0f && forwardSpeed > 0.5f);

                float rate = isBraking ? brakeRate : accelRate;
                velocity += forward * (throttle * rate * dt);
                _lastAppliedForce = rate * throttle;
            }

            // --- 3. 마찰 (입력 없을 때 감속) ---
            if (Mathf.Approximately(throttle, 0f) && velocity.magnitude > 0.01f)
            {
                float decel = friction * dt;
                if (decel >= velocity.magnitude)
                    velocity = Vector3.zero;
                else
                    velocity -= velocity.normalized * decel;
            }

            // --- 4. 속도 제한 ---
            if (velocity.magnitude > maxSpeed)
                velocity = velocity.normalized * maxSpeed;

            // --- 5. Y축 속도 제거 (수평 이동만) ---
            velocity.y = 0f;

            // --- 6. 이동 적용 (rb.MovePosition = 충돌 감지) ---
            if (velocity.magnitude > 0.01f)
            {
                // isKinematic이면 MovePosition 사용, 아니면 직접 위치 설정
                if (rb.isKinematic)
                {
                    rb.MovePosition(rb.position + velocity * dt);
                }
                else
                {
                    // 비 kinematic: linearVelocity 직접 설정 (충돌 자동 처리)
                    rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);
                }
            }
            else if (!rb.isKinematic)
            {
                // 정지 시 수평 속도만 0으로
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
            }
        }

        #endregion

        #region Game Loop

        // ★ Player 1 입력값 (매니저에서 직접 처리)
        private float _p1Throttle = 0f;
        private float _p1Steering = 0f;
        private string _inputMethod = "N/A";

        private void Update()
        {
            // ★ 매니저에서 직접 입력 읽기 (차량 Update가 안 돌아가므로)
            ReadPlayerInput();

            // ★ 부품 선택 UI 입력 처리
            if (_partUIOpen)
            {
                HandlePartSelectionInput();
                return; // UI 열려있으면 게임 로직 중단
            }

            switch (_currentState)
            {
                case GameState.WaitingForPlayers:
                    HandleLandingPageInput();
                    break;
                case GameState.Countdown:
                    UpdateCountdown();
                    break;
                case GameState.Playing:
                    UpdatePlaying();
                    HandlePartActions(); // ★ 부품 액션 처리
                    UpdatePunchingMachines(); // ★ 펀칭머신 업데이트
                    break;
                case GameState.RoundEnd:
                    UpdateRoundEnd();
                    break;
                case GameState.Intermission:
                    UpdateIntermission();
                    break;
            }

            if (_enableAI && _player2 != null)
                UpdateAI();
        }

        /// <summary>
        /// ★ Player 1 입력을 매니저에서 직접 읽기
        /// </summary>
        private void ReadPlayerInput()
        {
            _p1Throttle = 0f;
            _p1Steering = 0f;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) _p1Throttle += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) _p1Throttle -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) _p1Steering += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) _p1Steering -= 1f;
                _inputMethod = "InputSys";
            }
            else
            {
                _inputMethod = "KB=null";
            }
        }

        private void StartCountdown()
        {
            _currentState = GameState.Countdown;
            _countdownTimer = _gameConfig != null ? _gameConfig.countdownDuration : 3f;
            _lastCountdownNumber = Mathf.CeilToInt(_countdownTimer) + 1;

            // 차량 위치 리셋
            float defaultSpawnDist = _gameConfig != null ? _gameConfig.spawnDistanceFromCenter : 8f;
            float spawnDist = _selectedArenaType == ArenaType.Punching ? 22f : defaultSpawnDist;
            Vector3 center = _arenaCenter != null ? _arenaCenter.position : Vector3.zero;
            // 아레나 타입별 바닥 표면 높이 계산
            float arenaY = 1f; // BuildEnvironment와 동일
            float arenaSurfaceY;
            if (_selectedArenaType == ArenaType.Classic)
                arenaSurfaceY = center.y + 0.5f; // 씬 오브젝트 기준
            else
                arenaSurfaceY = arenaY + 0.5f + 0.1f; // Cube 바닥 상단 (arenaY + 0.5) + 여유
            float spawnY = arenaSurfaceY + 0.5f;   // 차량 중심이 표면 위 0.5m

            if (_player1 != null)
                _player1.ResetVehicle(new Vector3(center.x + spawnDist, spawnY, center.z), Quaternion.LookRotation(Vector3.left));

            if (_player2 != null)
                _player2.ResetVehicle(new Vector3(center.x - spawnDist, spawnY, center.z), Quaternion.LookRotation(Vector3.right));

            // ★ 매치 시작 시 호른 (첫 라운드만)
            if (_audioManager != null && _currentRound == 1)
                _audioManager.PlayMatchStart();

            Debug.Log($"=== Round {_currentRound} 카운트다운 시작 ===");
        }

        private void UpdateCountdown()
        {
            _countdownTimer -= Time.deltaTime;

            // ★ 카운트다운 숫자 변경 시 비프음
            int currentNumber = Mathf.CeilToInt(_countdownTimer);
            if (currentNumber != _lastCountdownNumber && currentNumber > 0 && currentNumber <= 3)
            {
                _lastCountdownNumber = currentNumber;
                if (_audioManager != null) _audioManager.PlayCountdownBeep();
            }

            if (_countdownTimer <= 0f)
            {
                _currentState = GameState.Playing;
                _roundTimer = _gameConfig != null ? _gameConfig.roundDuration : 180f;
                _roundActive = true;
                _roundStartGraceTimer = ROUND_START_GRACE; // 유예 기간 시작
                _aiStateTimer = 0f; // AI 상태 즉시 새로 결정
                _aiPartActionTimer = 1.5f; // 시작 후 약간 대기

                // ★ GO! 사운드 + BGM + 엔진
                if (_audioManager != null)
                {
                    _audioManager.PlayGoSound();
                    _audioManager.StartBGM();
                    _audioManager.StartEngineSound();
                }

                // 차량 해동! ★ 매니저에서도 직접 isKinematic 해제 (안전장치)
                if (_player1 != null)
                {
                    _player1.Unfreeze();
                    Rigidbody rb1 = _player1.GetComponent<Rigidbody>();
                    if (rb1 != null)
                    {
                        rb1.isKinematic = false;
                        Debug.Log($"[Manager] P1 Unfreeze 완료: isKinematic={rb1.isKinematic}, IsFrozen={_player1.IsFrozen}");
                    }
                    else
                    {
                        Debug.LogError("[Manager] P1에 Rigidbody가 없습니다!");
                    }
                }
                if (_player2 != null)
                {
                    _player2.Unfreeze();
                    Rigidbody rb2 = _player2.GetComponent<Rigidbody>();
                    if (rb2 != null) rb2.isKinematic = false;
                }

                Debug.Log($"=== Round {_currentRound} 시작! (유예: {ROUND_START_GRACE}초) ===");
            }
        }

        private void UpdatePlaying()
        {
            _roundTimer -= Time.deltaTime;

            // 라운드 시작 유예 기간 (콜라이더 안정화 대기)
            if (_roundStartGraceTimer > 0f)
            {
                _roundStartGraceTimer -= Time.deltaTime;
                return; // 유예 중에는 범위 체크 안 함
            }

            // 아레나 범위 체크
            CheckArenaBounds();

            // 타임아웃
            if (_roundTimer <= 0f)
            {
                Debug.Log("=== 시간 초과! 무승부 ===");
                EndRound(RoundResult.Draw);
            }
        }

        private void UpdateRoundEnd()
        {
            _countdownTimer -= Time.deltaTime;
            if (_countdownTimer <= 0f)
            {
                // 승리 조건 체크
                int winsNeeded = _gameConfig != null ? _gameConfig.roundsToWin : 2;

                if (_player1Score >= winsNeeded)
                {
                    _currentState = GameState.MatchEnd;
                    if (_audioManager != null) { _audioManager.StopBGM(); _audioManager.PlayMatchEnd(); }
                    Debug.Log("========== Player 1 매치 승리! ==========");
                    return;
                }
                if (_player2Score >= winsNeeded)
                {
                    _currentState = GameState.MatchEnd;
                    if (_audioManager != null) { _audioManager.StopBGM(); _audioManager.PlayMatchEnd(); }
                    Debug.Log("========== Player 2 매치 승리! ==========");
                    return;
                }

                int maxRounds = _gameConfig != null ? _gameConfig.maxRounds : 3;
                if (_currentRound >= maxRounds)
                {
                    _currentState = GameState.MatchEnd;
                    if (_audioManager != null) { _audioManager.StopBGM(); _audioManager.PlayMatchEnd(); }
                    string winner = _player1Score > _player2Score ? "Player 1" :
                                    _player2Score > _player1Score ? "Player 2" : "무승부";
                    Debug.Log($"========== 매치 종료! {winner} ==========");
                    return;
                }

                _currentRound++;
                // ★ 인터미션으로 전환 (부품 선택 시간)
                StartIntermission();
            }
        }

        #endregion

        #region Arena Bounds

        private void CheckArenaBounds()
        {
            if (!_roundActive) return;

            float radius = GetCurrentArenaRadius();
            Vector3 center = _arenaCenter != null ? _arenaCenter.position : Vector3.zero;
            // 비Classic 아레나는 arenaY=1 기준 바닥 계산
            float fallThreshold = (_selectedArenaType == ArenaType.Classic)
                ? center.y - 0.5f
                : -1f;  // 바닥(arenaY=1) 아래로 충분히 떨어지면 탈락

            // Player 1 체크
            if (_player1 != null)
            {
                if (IsOutOfBounds(_player1.transform.position, center, radius, fallThreshold))
                {
                    Debug.Log("Player 1 탈락!");
                    EndRound(RoundResult.Player2Win);
                    return;
                }
            }

            // Player 2 체크
            if (_player2 != null)
            {
                if (IsOutOfBounds(_player2.transform.position, center, radius, fallThreshold))
                {
                    Debug.Log("Player 2 탈락!");
                    EndRound(RoundResult.Player1Win);
                    return;
                }
            }
        }

        private bool IsOutOfBounds(Vector3 pos, Vector3 center, float radius, float fallThreshold)
        {
            if (pos.y < fallThreshold) return true;

            if (_selectedArenaType == ArenaType.Square)
            {
                // AABB 체크
                return Mathf.Abs(pos.x - center.x) > SQUARE_ARENA_HALF_SIZE ||
                       Mathf.Abs(pos.z - center.z) > SQUARE_ARENA_HALF_SIZE;
            }
            else
            {
                // 원형 체크
                float dist = Vector3.Distance(
                    new Vector3(pos.x, 0, pos.z),
                    new Vector3(center.x, 0, center.z));
                return dist > radius;
            }
        }

        private void EndRound(RoundResult result)
        {
            _roundActive = false;

            // 차량 즉시 정지 + 자체 물리 속도 리셋
            _p1Velocity = Vector3.zero;
            _p2Velocity = Vector3.zero;
            _aiThrottle = 0f;
            _aiSteering = 0f;
            if (_player1 != null) _player1.Freeze();
            if (_player2 != null) _player2.Freeze();

            // ★ 엔진 사운드 정지
            if (_audioManager != null)
            {
                _audioManager.StopEngineSound();
                _audioManager.PlayRoundEnd();
            }

            switch (result)
            {
                case RoundResult.Player1Win:
                    _player1Score++;
                    if (_audioManager != null) _audioManager.PlayRoundWin();
                    Debug.Log($"Round {_currentRound}: Player 1 승리! (스코어: {_player1Score}-{_player2Score})");
                    break;
                case RoundResult.Player2Win:
                    _player2Score++;
                    if (_audioManager != null) _audioManager.PlayRoundLose();
                    Debug.Log($"Round {_currentRound}: Player 2 승리! (스코어: {_player1Score}-{_player2Score})");
                    break;
                case RoundResult.Draw:
                    Debug.Log($"Round {_currentRound}: 무승부! (스코어: {_player1Score}-{_player2Score})");
                    break;
            }

            _currentState = GameState.RoundEnd;
            _countdownTimer = _gameConfig != null ? _gameConfig.roundResultDisplayDuration : 3f;
        }

        #endregion

        #region Smart AI

        // AI 물리
        private Vector3 _p2Velocity = Vector3.zero;
        private float _p2AngularVel = 0f;
        private float _aiThrottle = 0f;
        private float _aiSteering = 0f;

        // AI 전략 상태
        private enum AIState { Charge, Flank, Retreat, Orbit, Ambush }
        private AIState _aiState = AIState.Charge;
        private float _aiStateTimer = 0f;         // 현재 상태 지속 시간
        private float _aiStateDuration = 2f;      // 상태 전환까지 시간
        private float _aiFlankDir = 1f;            // 측면 이동 방향 (+1/-1)
        private float _aiAmbushAngle = 0f;         // 매복 목표 각도
        private float _aiPartActionTimer = 0f;     // 부품 사용 쿨다운
        private const float AI_PART_COOLDOWN = 2.5f;

        private void UpdateAI()
        {
            if (_player2 == null || _player1 == null || !_roundActive)
                return;

            if (Time.time < _aiNextActionTime)
                return;

            _aiNextActionTime = Time.time + _aiReactionDelay;

            Vector3 p2Pos = _player2.transform.position;
            Vector3 p1Pos = _player1.transform.position;
            Vector3 toPlayer = p1Pos - p2Pos;
            toPlayer.y = 0f;
            float distToPlayer = toPlayer.magnitude;

            Vector3 forward = _player2.transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 arenaCenter = _arenaCenter != null ? _arenaCenter.position : Vector3.zero;
            Vector3 toCenter = arenaCenter - p2Pos;
            toCenter.y = 0f;
            float distToCenter = toCenter.magnitude;

            float arenaRadius = GetCurrentArenaRadius();
            float edgeDanger = distToCenter / arenaRadius; // 0=중앙, 1=가장자리

            // --- 상태 전환 로직 ---
            _aiStateTimer -= _aiReactionDelay;
            if (_aiStateTimer <= 0f)
            {
                ChooseNextAIState(distToPlayer, edgeDanger, arenaRadius);
            }

            // --- 가장자리 위험 시 강제 복귀 ---
            if (edgeDanger > 0.8f)
            {
                float angleToCenter = Vector3.SignedAngle(forward, toCenter.normalized, Vector3.up);
                _aiThrottle = 1f;
                _aiSteering = Mathf.Clamp(angleToCenter / 40f, -1f, 1f);
                return;
            }

            // --- 상태별 행동 ---
            switch (_aiState)
            {
                case AIState.Charge:
                    // 돌진: 상대를 향해 전속력
                    SteerToward(toPlayer.normalized, forward);
                    _aiThrottle = 1f;
                    break;

                case AIState.Flank:
                    // 측면 우회: 상대 옆으로 돌아서 밀기
                    Vector3 flankDir = Vector3.Cross(Vector3.up, toPlayer.normalized) * _aiFlankDir;
                    Vector3 flankTarget = (toPlayer.normalized * 0.4f + flankDir * 0.6f).normalized;
                    SteerToward(flankTarget, forward);
                    _aiThrottle = distToPlayer > 5f ? 1f : 0.7f;
                    // 가까워지면 돌진으로 전환
                    if (distToPlayer < 8f && _aiStateTimer < _aiStateDuration * 0.5f)
                    {
                        _aiState = AIState.Charge;
                        _aiStateTimer = Random.Range(1f, 2f);
                    }
                    break;

                case AIState.Retreat:
                    // 후퇴: 상대 반대로 이동 후 반격 준비
                    Vector3 awayDir = -toPlayer.normalized;
                    // 중앙 쪽으로 약간 보정
                    Vector3 retreatTarget = (awayDir + toCenter.normalized * 0.4f).normalized;
                    SteerToward(retreatTarget, forward);
                    _aiThrottle = 0.9f;
                    // 거리 벌어지면 돌진
                    if (distToPlayer > 20f)
                    {
                        _aiState = AIState.Charge;
                        _aiStateTimer = Random.Range(2f, 3f);
                    }
                    break;

                case AIState.Orbit:
                    // 선회: 상대 주위를 원형으로 돌며 기회 엿보기
                    Vector3 orbitDir = Vector3.Cross(Vector3.up, toPlayer.normalized) * _aiFlankDir;
                    // 적정 거리 유지 (10~15m)
                    float distFactor = (distToPlayer - 12f) / 10f; // 가까우면 양수→멀어짐, 멀면 음수→다가감
                    Vector3 orbitTarget = (orbitDir + toPlayer.normalized * (-distFactor)).normalized;
                    SteerToward(orbitTarget, forward);
                    _aiThrottle = 0.8f;
                    break;

                case AIState.Ambush:
                    // 매복: 아레나 특정 지점에서 대기 후 급습
                    Vector3 ambushPos = arenaCenter + new Vector3(
                        Mathf.Cos(_aiAmbushAngle) * arenaRadius * 0.5f,
                        0f,
                        Mathf.Sin(_aiAmbushAngle) * arenaRadius * 0.5f);
                    Vector3 toAmbush = ambushPos - p2Pos;
                    toAmbush.y = 0f;

                    if (toAmbush.magnitude < 5f)
                    {
                        // 매복 지점 도착 → 플레이어 방향 바라보며 대기
                        SteerToward(toPlayer.normalized, forward);
                        _aiThrottle = 0.2f;
                        // 가까이 오면 급습
                        if (distToPlayer < 18f)
                        {
                            _aiState = AIState.Charge;
                            _aiStateTimer = Random.Range(2f, 3.5f);
                        }
                    }
                    else
                    {
                        SteerToward(toAmbush.normalized, forward);
                        _aiThrottle = 0.9f;
                    }
                    break;
            }

            // --- AI 부품 사용 ---
            _aiPartActionTimer -= _aiReactionDelay;
            if (_aiPartActionTimer <= 0f && distToPlayer < 8f && _partSystem != null)
            {
                // 가까울 때 랜덤 부품 사용
                PartSlot[] slots = { PartSlot.Front, PartSlot.Rooftop, PartSlot.Rear };
                PartSlot pick = slots[Random.Range(0, 3)];
                _partSystem.TryExecuteAction(pick, _player2, _player1, false,
                                              ref _p2Velocity, ref _p1Velocity);
                _aiPartActionTimer = AI_PART_COOLDOWN + Random.Range(0f, 1.5f);
            }
        }

        private void ChooseNextAIState(float distToPlayer, float edgeDanger, float arenaRadius)
        {
            float roll = Random.value;

            if (distToPlayer > 25f)
            {
                // 멀면: 돌진 60%, 매복 30%, 선회 10%
                if (roll < 0.6f) _aiState = AIState.Charge;
                else if (roll < 0.9f)
                {
                    _aiState = AIState.Ambush;
                    _aiAmbushAngle = Random.Range(0f, Mathf.PI * 2f);
                }
                else _aiState = AIState.Orbit;
            }
            else if (distToPlayer > 12f)
            {
                // 중거리: 측면우회 35%, 돌진 30%, 선회 25%, 매복 10%
                if (roll < 0.35f) _aiState = AIState.Flank;
                else if (roll < 0.65f) _aiState = AIState.Charge;
                else if (roll < 0.9f) _aiState = AIState.Orbit;
                else
                {
                    _aiState = AIState.Ambush;
                    _aiAmbushAngle = Random.Range(0f, Mathf.PI * 2f);
                }
            }
            else
            {
                // 근거리: 돌진 40%, 후퇴 25%, 측면우회 25%, 선회 10%
                if (roll < 0.4f) _aiState = AIState.Charge;
                else if (roll < 0.65f) _aiState = AIState.Retreat;
                else if (roll < 0.9f) _aiState = AIState.Flank;
                else _aiState = AIState.Orbit;
            }

            _aiFlankDir = Random.value > 0.5f ? 1f : -1f;
            _aiStateDuration = Random.Range(1.5f, 4f);
            _aiStateTimer = _aiStateDuration;
        }

        private void SteerToward(Vector3 targetDir, Vector3 currentForward)
        {
            float angle = Vector3.SignedAngle(currentForward, targetDir, Vector3.up);
            _aiSteering = Mathf.Clamp(angle / 40f, -1f, 1f);
        }

        /// <summary>
        /// AI 물리를 FixedUpdate에서 P1과 동일한 시스템으로 처리
        /// </summary>
        private void FixedUpdateAI()
        {
            if (_player2 == null || _player2.IsFrozen || !_enableAI) return;
            if (_p2Rb == null) _p2Rb = _player2.GetComponent<Rigidbody>();
            if (_p2Rb == null) return;

            SimulateVehiclePhysics(_player2, _p2Rb, _aiThrottle, _aiSteering,
                                    ref _p2Velocity, ref _p2AngularVel);
        }

        #endregion

        #region Part System & Intermission

        /// <summary>
        /// 부품 선택 UI 입력 처리
        /// </summary>
        private void HandlePartSelectionInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // 슬롯 선택: 1=Front, 2=Rooftop, 3=Rear
            if (kb.digit1Key.wasPressedThisFrame)
                _partUISelectedSlot = PartSlot.Front;
            if (kb.digit2Key.wasPressedThisFrame)
                _partUISelectedSlot = PartSlot.Rooftop;
            if (kb.digit3Key.wasPressedThisFrame)
                _partUISelectedSlot = PartSlot.Rear;

            // 좌우로 부품 변경 (현재 슬롯 내에서)
            if (kb.leftArrowKey.wasPressedThisFrame)
            {
                _partSystem.CyclePart(_partUISelectedSlot, -1);
                _partSystem.EquipSelected(_partUISelectedSlot, _player1);
            }
            if (kb.rightArrowKey.wasPressedThisFrame)
            {
                _partSystem.CyclePart(_partUISelectedSlot, 1);
                _partSystem.EquipSelected(_partUISelectedSlot, _player1);
            }

            // Space로 "완료" - 모든 슬롯 선택 끝난 뒤 직접 눌러야 확정
            if (kb.spaceKey.wasPressedThisFrame)
            {
                ConfirmPartSelection();
            }
        }

        /// <summary>
        /// 부품 선택 완료 처리 (완료 버튼)
        /// </summary>
        private void ConfirmPartSelection()
        {
            _partUIOpen = false;

            // 모든 슬롯 장착 확정
            _partSystem.EquipSelected(PartSlot.Front, _player1);
            _partSystem.EquipSelected(PartSlot.Rooftop, _player1);
            _partSystem.EquipSelected(PartSlot.Rear, _player1);

            if (_currentState == GameState.WaitingForPlayers)
            {
                // 랜딩 페이지로 돌아감 (게임시작은 랜딩페이지에서)
            }
            else if (_currentState == GameState.Intermission)
            {
                StartCountdown();
            }
        }

        /// <summary>
        /// 인터미션 시작 (라운드 사이 부품 선택 시간)
        /// </summary>
        private void StartIntermission()
        {
            _currentState = GameState.Intermission;
            _intermissionTimer = _gameConfig != null ? _gameConfig.intermissionDuration : 30f;
            _partUIOpen = false; // 버튼으로 열기 (자동으로 열지 않음)

            if (_audioManager != null)
                _audioManager.StopBGM();

            Debug.Log($"=== 인터미션 시작 ({_intermissionTimer}초) - P키로 부품을 변경하세요! ===");
        }

        private void UpdateIntermission()
        {
            _intermissionTimer -= Time.deltaTime;
            if (_intermissionTimer <= 0f && !_partUIOpen)
            {
                StartCountdown();
            }
            // UI가 열려있으면 HandlePartSelectionInput에서 처리
        }

        /// <summary>
        /// 경기 중 부품 액션 처리 (Q=Front, E=Rooftop, R=Rear)
        /// </summary>
        private void HandlePartActions()
        {
            if (_partSystem == null || _player1 == null || _player2 == null) return;
            if (!_roundActive) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.qKey.wasPressedThisFrame)
            {
                _partSystem.TryExecuteAction(PartSlot.Front, _player1, _player2, true,
                                              ref _p1Velocity, ref _p2Velocity);
            }
            if (kb.eKey.wasPressedThisFrame)
            {
                _partSystem.TryExecuteAction(PartSlot.Rooftop, _player1, _player2, true,
                                              ref _p1Velocity, ref _p2Velocity);
            }
            if (kb.rKey.wasPressedThisFrame)
            {
                _partSystem.TryExecuteAction(PartSlot.Rear, _player1, _player2, true,
                                              ref _p1Velocity, ref _p2Velocity);
            }
        }

        /// <summary>
        /// 펀칭머신 업데이트 (Punching 아레나 전용)
        /// </summary>
        private void UpdatePunchingMachines()
        {
            if (_selectedArenaType != ArenaType.Punching) return;
            if (_punchingMachines == null || _punchPositions == null) return;
            if (!_roundActive) return;

            float dt = Time.deltaTime;

            for (int i = 0; i < 4; i++)
            {
                // 쿨다운 감소
                if (_punchCooldowns[i] > 0f)
                    _punchCooldowns[i] -= dt;

                // 애니메이션 (피스톤 확장/축소)
                if (_punchAnimTimers[i] > 0f)
                {
                    _punchAnimTimers[i] -= dt;
                    if (_punchPistons != null && _punchPistons[i] != null)
                    {
                        float t = _punchAnimTimers[i] / PUNCH_ANIM_DURATION;
                        float extend = (1f - t) * 2f; // 펀치 시 확장
                        Vector3 baseLocal = _punchPistons[i].transform.localPosition;
                        // 피스톤은 안쪽을 향해 확장됨 (이미 배치 완료)
                    }
                    continue;
                }

                // 쿨다운 중이면 스킵
                if (_punchCooldowns[i] > 0f) continue;

                // 범위 내 차량 감지
                Vector3 machinePos = _punchPositions[i];
                OfflineVehicleController[] vehicles = { _player1, _player2 };

                for (int v = 0; v < vehicles.Length; v++)
                {
                    if (vehicles[v] == null || vehicles[v].IsFrozen) continue;

                    float dist = Vector3.Distance(
                        new Vector3(vehicles[v].transform.position.x, 0, vehicles[v].transform.position.z),
                        new Vector3(machinePos.x, 0, machinePos.z));

                    if (dist < PUNCH_RANGE)
                    {
                        // ★ 펀치! 아레나 중앙 방향 + 위로 발사
                        Vector3 toCenter = (Vector3.zero - machinePos).normalized;
                        Vector3 launchDir = (toCenter + Vector3.up * 0.5f).normalized;
                        Vector3 launchVel = launchDir * PUNCH_FORCE;

                        // rb.linearVelocity 직접 설정
                        Rigidbody vRb = vehicles[v].GetComponent<Rigidbody>();
                        if (vRb != null)
                            vRb.linearVelocity = launchVel;

                        // 자체 물리 속도도 업데이트
                        if (v == 0)
                            _p1Velocity = launchVel;
                        else
                            _p2Velocity = launchVel;

                        // 쿨다운 + 애니메이션 시작
                        _punchCooldowns[i] = PUNCH_COOLDOWN;
                        _punchAnimTimers[i] = PUNCH_ANIM_DURATION;

                        if (_audioManager != null)
                            _audioManager.PlayPunchHit();

                        Debug.Log($"[PUNCH] 머신 {i}이(가) Player {v + 1}을(를) 발사! vel={launchVel}");
                        break; // 한 번에 한 차량만
                    }
                }
            }
        }

        /// <summary>
        /// 랜딩 페이지 입력 처리 (WaitingForPlayers && !_partUIOpen)
        /// 3단계 네비게이션: ←/→, Space, Tab, P, Esc
        /// </summary>
        private void HandleLandingPageInput()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // →/Space = 다음 단계 (마지막 단계에서 Space = 게임시작)
            if (kb.rightArrowKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                if (_landingStep < 2)
                    _landingStep++;
                else if (kb.spaceKey.wasPressedThisFrame)
                    StartCountdown();
            }
            // ← = 이전 단계
            if (kb.leftArrowKey.wasPressedThisFrame)
            {
                if (_landingStep > 0)
                    _landingStep--;
            }
            // Tab = 체급 변경 (체급 단계에서)
            if (kb.tabKey.wasPressedThisFrame && _landingStep == 1)
            {
                CycleWeightClass();
            }
            // P = 부품교체 (부품 단계에서)
            if (kb.pKey.wasPressedThisFrame && _landingStep == 2)
            {
                _partUIOpen = true;
            }
            // F1 = AI 토글
            if (kb.f1Key.wasPressedThisFrame)
            {
                _enableAI = !_enableAI;
                Debug.Log($"AI {(_enableAI ? "활성화" : "비활성화")}");
            }
            // Esc = 종료
            if (kb.escapeKey.wasPressedThisFrame)
            {
                QuitGame();
            }
        }

        #endregion

        #region UI (OnGUI)

        // 플레이어 식별용 색상
        private static readonly Color P1_COLOR = new Color(0.2f, 0.5f, 1f); // 파란색
        private static readonly Color P2_COLOR = new Color(1f, 0.25f, 0.2f); // 빨간색
        private bool _playerColorsApplied = false;

        private void OnGUI()
        {
            // 플레이어 차량 색상 적용 (한번만)
            if (!_playerColorsApplied)
            {
                try
                {
                    ApplyPlayerColors();
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[MODEL] ApplyPlayerColors 실패: {e.Message}\n{e.StackTrace}");
                }
                _playerColorsApplied = true;
            }

            float _s = S;
            float centerX = Screen.width / 2f;
            float sw = Screen.width;
            float sh = Screen.height;

            // --- 스코어 (스케일 적용) ---
            float scoreY = 10 * _s;
            float scoreH = 40 * _s;
            DrawText(new Rect(centerX - 350 * _s, scoreY, 300 * _s, scoreH),
                     $"P1 (YOU)  [{_player1Score}]", 28, P1_COLOR, TextAnchor.MiddleRight);
            DrawText(new Rect(centerX - 40 * _s, scoreY, 80 * _s, scoreH),
                     "VS", 28, new Color(0.8f, 0.8f, 0.85f));
            DrawText(new Rect(centerX + 50 * _s, scoreY, 300 * _s, scoreH),
                     $"[{_player2Score}]  P2 (AI)", 28, P2_COLOR, TextAnchor.MiddleLeft);

            // --- 상태 텍스트 ---
            string stateText = _currentState switch
            {
                GameState.Countdown => $"카운트다운: {Mathf.Ceil(_countdownTimer)}",
                GameState.Playing => $"Round {_currentRound}  -  남은시간: {Mathf.Ceil(_roundTimer)}s",
                GameState.RoundEnd => "라운드 종료!",
                GameState.Intermission => $"인터미션: {Mathf.Ceil(_intermissionTimer)}초",
                GameState.MatchEnd => "매치 종료!",
                _ => _partUIOpen ? "부품을 선택하세요" : ""
            };
            if (stateText.Length > 0)
                DrawText(new Rect(0, scoreY + scoreH + 4 * _s, sw, 30 * _s),
                         stateText, 20, new Color(0.8f, 0.82f, 0.88f), bold: false, shadow: false);

            // --- 카운트다운 큰 숫자 ---
            if (_currentState == GameState.Countdown)
            {
                string num = Mathf.Ceil(_countdownTimer).ToString("0");
                float cy = sh * 0.33f;
                DrawText(new Rect(0, cy, sw, 120 * _s), num, 100, new Color(1f, 0.9f, 0.2f));
                DrawText(new Rect(0, cy + 110 * _s, sw, 35 * _s),
                         "준비 중... 곧 시작합니다!", 22, new Color(0.8f, 0.8f, 0.85f), bold: false);
            }

            // --- GO! ---
            if (_currentState == GameState.Playing && _roundTimer > (_gameConfig != null ? _gameConfig.roundDuration - 1f : 179f))
            {
                DrawText(new Rect(0, sh * 0.33f, sw, 120 * _s), "GO!", 96, new Color(0.2f, 1f, 0.3f));
            }


            // --- 차량 위 플레이어 라벨 (월드→스크린 좌표) ---
            DrawPlayerLabel(_player1, "▼ P1 (YOU)", P1_COLOR);
            DrawPlayerLabel(_player2, "▼ P2 (AI)", P2_COLOR);

            // --- 조작법 ---
            string partNames = "";
            if (_partSystem != null)
            {
                var fe = _partSystem.P1EquippedParts.ContainsKey(PartSlot.Front) ? _partSystem.P1EquippedParts[PartSlot.Front].partName : "없음";
                var re = _partSystem.P1EquippedParts.ContainsKey(PartSlot.Rooftop) ? _partSystem.P1EquippedParts[PartSlot.Rooftop].partName : "없음";
                var be = _partSystem.P1EquippedParts.ContainsKey(PartSlot.Rear) ? _partSystem.P1EquippedParts[PartSlot.Rear].partName : "없음";
                partNames = $"Q=[{fe}] E=[{re}] R=[{be}]";
            }
            DrawText(new Rect(10 * _s, sh - 110 * _s, 500 * _s, 100 * _s),
                $"WASD: 이동  |  {partNames}\n" +
                "F1: AI 토글",
                14, new Color(0.55f, 0.55f, 0.6f), TextAnchor.LowerLeft, bold: false, shadow: false);

            // ★ 부품 쿨다운 표시 (경기 중)
            if (_currentState == GameState.Playing && _partSystem != null)
            {
                DrawPartCooldowns();
            }

            // ★ 랜딩 페이지
            if (_currentState == GameState.WaitingForPlayers && !_partUIOpen)
            {
                DrawLandingPage();
            }

            // ★ 부품 선택 UI
            if (_partUIOpen)
            {
                DrawPartSelectionUI();
            }

            // ★ 인터미션 타이머 + CONTINUE / PARTS 버튼
            if (_currentState == GameState.Intermission && !_partUIOpen)
            {
                DrawText(new Rect(0, 70 * _s, sw, 36 * _s),
                    $"INTERMISSION: {Mathf.Ceil(_intermissionTimer)}s", 28,
                    new Color(1f, 0.85f, 0.2f));

                float imBtnW = 240 * _s;
                float imBtnH = 55 * _s;
                float imBtnGap = 20 * _s;
                float imBtnY = 70 * _s + 44 * _s;
                float totalImW = imBtnW * 2 + imBtnGap;
                float imBtnX = centerX - totalImW / 2f;

                // CONTINUE 버튼 (바로 다음 라운드)
                Rect contBtn = new Rect(imBtnX, imBtnY, imBtnW, imBtnH);
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 2.5f);
                if (DrawButton(contBtn, "CONTINUE [Space]", 24,
                              new Color(0.05f * pulse, 0.30f * pulse, 0.08f * pulse, 0.95f),
                              new Color(0.3f * pulse, 0.9f * pulse, 0.4f * pulse, 0.9f), Color.white))
                {
                    StartCountdown();
                }

                // PARTS 버튼
                Rect imBtn = new Rect(imBtnX + imBtnW + imBtnGap, imBtnY, imBtnW, imBtnH);
                if (DrawButton(imBtn, "PARTS [P]", 24,
                              new Color(0.10f, 0.18f, 0.35f, 0.9f),
                              new Color(0.3f, 0.5f, 1f, 0.8f), Color.white))
                {
                    _partUIOpen = true;
                }

                // 키보드 단축키
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.spaceKey.wasPressedThisFrame)
                        StartCountdown();
                    if (Keyboard.current.pKey.wasPressedThisFrame)
                        _partUIOpen = true;
                }
            }

            // --- 체급 정보 ---
            if (_player1 != null)
                DrawText(new Rect(10 * _s, 10 * _s, 250 * _s, 25 * _s),
                         $"P1: {_player1.CurrentWeightClass}", 16, P1_COLOR,
                         TextAnchor.MiddleLeft, shadow: false);
            if (_player2 != null)
                DrawText(new Rect(sw - 260 * _s, 10 * _s, 250 * _s, 25 * _s),
                         $"P2: {_player2.CurrentWeightClass}", 16, P2_COLOR,
                         TextAnchor.MiddleRight, shadow: false);

            // --- AI 상태 ---
            DrawText(new Rect(sw - 260 * _s, 32 * _s, 250 * _s, 25 * _s),
                     $"AI: {(_enableAI ? "ON" : "OFF")}", 16, P2_COLOR,
                     TextAnchor.MiddleRight, bold: false, shadow: false);

            // --- 매치 종료 ---
            if (_currentState == GameState.MatchEnd)
            {
                // 승자 표시
                string winner = _player1Score > _player2Score ? "Player 1 승리!" :
                                _player2Score > _player1Score ? "Player 2 승리!" : "무승부!";
                Color winColor = _player1Score > _player2Score ? P1_COLOR :
                                 _player2Score > _player1Score ? P2_COLOR : new Color(1f, 0.85f, 0.2f);
                DrawText(new Rect(0, sh * 0.3f, sw, 60 * _s), winner, 56, winColor);

                // 매치 종료 버튼
                float mBtnW = 260 * _s;
                float mBtnH = 60 * _s;
                float mBtnGap = 30 * _s;
                float mBtnY = sh * 0.3f + 80 * _s;

                Rect restartBtn = new Rect(centerX - mBtnW - mBtnGap / 2f, mBtnY, mBtnW, mBtnH);
                if (DrawButton(restartBtn, "RESTART [Space]", 26,
                              new Color(0.08f, 0.25f, 0.08f, 0.9f),
                              new Color(0.3f, 0.9f, 0.4f, 0.8f), Color.white))
                {
                    RestartMatch();
                }

                Rect matchQuitBtn = new Rect(centerX + mBtnGap / 2f, mBtnY, mBtnW, mBtnH);
                if (DrawButton(matchQuitBtn, "QUIT [Esc]", 26,
                              new Color(0.25f, 0.08f, 0.08f, 0.9f),
                              new Color(0.8f, 0.25f, 0.2f, 0.7f), new Color(0.9f, 0.7f, 0.7f)))
                {
                    QuitGame();
                }
            }

            // --- 디버그 정보 (우측 하단) ---
            DrawDebugInfo();
        }

        private void DrawDebugInfo()
        {
            // ========== 디버그 패널 ==========
            GUIStyle bgStyle = new GUIStyle(GUI.skin.box);
            GUI.Box(new Rect(5, 80, 380, 160), "", bgStyle);

            GUIStyle dbgStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            dbgStyle.normal.textColor = Color.yellow;

            GUIStyle dbgSmall = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            dbgSmall.normal.textColor = Color.white;

            float y = 85;
            float x = 10;
            float h = 20;

            // 상태 + 속도
            GUI.Label(new Rect(x, y, 370, h), $"{_currentState}  Speed: {_p1Speed:F1} m/s  {_fixedUpdateDebug}", dbgStyle);
            y += h;

            // P1 입력
            string p1State = _player1 != null ? (_player1.IsFrozen ? "FROZEN" : "ACTIVE") : "NULL!";
            Color p1Col = (_player1 != null && !_player1.IsFrozen) ? Color.green : Color.red;
            GUIStyle p1Style = new GUIStyle(dbgSmall);
            p1Style.normal.textColor = p1Col;
            GUI.Label(new Rect(x, y, 370, h), $"P1: {p1State}  T={_p1Throttle:F0} S={_p1Steering:F0}  vel=({_p1Velocity.x:F1},{_p1Velocity.z:F1})", p1Style);
            y += h;

            // P1 위치
            if (_player1 != null)
            {
                var p = _player1.transform.position;
                GUI.Label(new Rect(x, y, 370, h), $"   pos=({p.x:F1}, {p.y:F1}, {p.z:F1})", dbgSmall);
            }
            y += h;

            // P2 상태
            string p2State = _player2 != null ? (_player2.IsFrozen ? "FROZEN" : "ACTIVE") : "NULL!";
            GUI.Label(new Rect(x, y, 370, h), $"P2: {p2State}  AI: {(_enableAI ? "ON" : "OFF")}", dbgSmall);
            y += h;

            // 라운드 정보
            GUI.Label(new Rect(x, y, 370, h), $"Round {_currentRound}  Grace: {(_roundStartGraceTimer > 0 ? $"{_roundStartGraceTimer:F1}s" : "OFF")}", dbgSmall);
            y += h;

            // 큰 속도 표시 (이동 중일 때)
            if (_p1Speed > 0.5f)
            {
                GUIStyle spdStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                spdStyle.normal.textColor = Color.cyan;
                GUI.Label(new Rect(x, y, 370, 26), $">> {_p1Speed:F1} m/s <<", spdStyle);
            }
        }

        /// <summary>
        /// 부품 선택 UI (전체 화면 오버레이) - 세련된 스케일링 UI
        /// </summary>
        private void DrawPartSelectionUI()
        {
            if (_partSystem == null) return;

            float sw = Screen.width;
            float sh = Screen.height;
            float cx = sw / 2f;
            float s = S;

            // ===== 풀스크린 배경 =====
            DrawRect(new Rect(0, 0, sw, sh), new Color(0.02f, 0.02f, 0.08f, 0.92f));

            // ===== 제목 =====
            float titleY = sh * 0.03f;
            DrawText(new Rect(0, titleY, sw, 60 * s), "PARTS SELECT", 52, new Color(0.9f, 0.92f, 1f));

            // ===== 인터미션 타이머 =====
            if (_currentState == GameState.Intermission)
            {
                Color timerCol = _intermissionTimer < 10f
                    ? new Color(1f, 0.3f, 0.2f) : new Color(1f, 0.85f, 0.2f);
                DrawText(new Rect(0, titleY + 58 * s, sw, 35 * s),
                    $"남은 시간: {Mathf.Ceil(_intermissionTimer)}초", 26, timerCol);
            }

            // ===== 슬롯 탭 =====
            PartSlot[] slots = { PartSlot.Front, PartSlot.Rooftop, PartSlot.Rear };
            string[] slotNames = { "앞범퍼  FRONT", "루프탑  ROOF", "뒷범퍼  REAR" };
            string[] slotKeys = { "Q", "E", "R" };
            float tabW = Mathf.Min(280 * s, (sw - 80 * s) / 3f);
            float tabH = 60 * s;
            float tabGap = 12 * s;
            float tabStartX = cx - (tabW * 3f + tabGap * 2f) / 2f;
            float tabY = sh * 0.12f;

            for (int i = 0; i < slots.Length; i++)
            {
                bool isActive = (_partUISelectedSlot == slots[i]);
                float tx = tabStartX + i * (tabW + tabGap);
                Rect tabRect = new Rect(tx, tabY, tabW, tabH);

                Color tabBg = isActive
                    ? new Color(0.15f, 0.25f, 0.55f, 0.95f)
                    : new Color(0.08f, 0.08f, 0.12f, 0.7f);
                Color tabBorder = isActive
                    ? new Color(0.4f, 0.65f, 1f, 0.9f)
                    : new Color(0.2f, 0.2f, 0.25f, 0.5f);
                DrawRect(tabRect, tabBg, tabBorder, isActive ? 2 * s : 1 * s);

                if (isActive)
                    DrawRect(new Rect(tx, tabY + tabH - 4 * s, tabW, 4 * s), new Color(0.4f, 0.7f, 1f));

                DrawText(new Rect(tx, tabY, tabW, tabH),
                         $"[{i + 1}] {slotNames[i]}", 22,
                         isActive ? Color.white : new Color(0.5f, 0.5f, 0.55f));

                // 탭 클릭
                if (Event.current.type == EventType.MouseDown && tabRect.Contains(Event.current.mousePosition))
                {
                    _partUISelectedSlot = slots[i];
                    Event.current.Use();
                }
            }

            // ===== 부품 데이터 =====
            PartSlot currentSlot = _partUISelectedSlot;
            if (!_partSystem.AvailableParts.ContainsKey(currentSlot)) return;
            var partsList = _partSystem.AvailableParts[currentSlot];
            int currentIdx = _partSystem.P1SelectedIndex.ContainsKey(currentSlot) ? _partSystem.P1SelectedIndex[currentSlot] : 0;
            if (partsList.Count == 0) return;
            OfflinePartData currentPart = partsList[currentIdx];

            // ===== 메인 컨텐츠 =====
            float contentY = tabY + tabH + 20 * s;
            float contentH = sh * 0.52f;
            float contentW = sw * 0.82f;
            float contentX = (sw - contentW) / 2f;

            // 컨텐츠 배경
            Color partAccent = currentPart.partColor;
            DrawRect(new Rect(contentX, contentY, contentW, contentH),
                     new Color(0.05f, 0.05f, 0.09f, 0.9f),
                     new Color(partAccent.r * 0.5f, partAccent.g * 0.5f, partAccent.b * 0.5f, 0.6f), 2 * s);

            // 상단 액센트 라인 (부품 색상)
            DrawRect(new Rect(contentX, contentY, contentW, 4 * s), partAccent);

            // === 왼쪽: 프리뷰 ===
            float previewSize = Mathf.Min(contentH * 0.7f, contentW * 0.32f);
            float previewX = contentX + contentW * 0.08f;
            float previewY = contentY + (contentH - previewSize) / 2f;

            // 프리뷰 배경
            DrawRect(new Rect(previewX - 15 * s, previewY - 15 * s, previewSize + 30 * s, previewSize + 30 * s),
                     new Color(0.08f, 0.08f, 0.12f, 0.9f),
                     new Color(partAccent.r * 0.3f, partAccent.g * 0.3f, partAccent.b * 0.3f, 0.5f), 1 * s);

            // ★ 부품 프리뷰 도형 (액션 타입별 다른 아이콘)
            DrawPartPreviewIcon(previewX, previewY, previewSize, currentSlot, currentPart.actionType, partAccent);

            // 슬롯 키
            int slotIdx = currentSlot == PartSlot.Front ? 0 : (currentSlot == PartSlot.Rooftop ? 1 : 2);
            DrawText(new Rect(previewX, previewY + previewSize + 15 * s, previewSize, 30 * s),
                     $"전투 키: [{slotKeys[slotIdx]}]", 20, new Color(0.6f, 0.6f, 0.65f), bold: false, shadow: false);

            // === 오른쪽: 부품 정보 ===
            float infoX = contentX + contentW * 0.46f;
            float infoW = contentW * 0.48f;
            float infoY = contentY + 25 * s;

            // 부품 이름
            DrawText(new Rect(infoX, infoY, infoW, 55 * s),
                     currentPart.partName, 44, partAccent, TextAnchor.MiddleLeft);
            infoY += 60 * s;

            // 페이지
            DrawText(new Rect(infoX, infoY, infoW, 30 * s),
                     $"{currentIdx + 1} / {partsList.Count}", 22,
                     new Color(0.5f, 0.52f, 0.58f), TextAnchor.MiddleLeft, bold: false, shadow: false);
            infoY += 38 * s;

            // 구분선
            DrawRect(new Rect(infoX, infoY, infoW * 0.6f, 2 * s), new Color(0.3f, 0.3f, 0.38f, 0.6f));
            infoY += 16 * s;

            // 설명
            DrawText(new Rect(infoX, infoY, infoW, 70 * s),
                     currentPart.description, 24, new Color(0.85f, 0.85f, 0.9f),
                     TextAnchor.UpperLeft, bold: false, shadow: false);
            infoY += 80 * s;

            // 스탯 바
            float barW = Mathf.Min(280 * s, infoW * 0.7f);
            float barH = 12 * s;
            float statH = 42 * s;
            float labelW = 80 * s;
            float valW = 90 * s;

            // 공격력
            DrawText(new Rect(infoX, infoY, labelW, statH), "공격력", 22,
                     new Color(0.6f, 0.6f, 0.65f), TextAnchor.MiddleLeft, bold: false, shadow: false);
            DrawText(new Rect(infoX + labelW, infoY, valW, statH), $"{currentPart.actionForce:F0}", 24,
                     new Color(1f, 0.4f, 0.3f), TextAnchor.MiddleLeft);
            float bx = infoX + labelW + valW;
            DrawRect(new Rect(bx, infoY + statH / 2f - barH / 2f, barW, barH), new Color(0.15f, 0.15f, 0.2f, 0.8f));
            DrawRect(new Rect(bx, infoY + statH / 2f - barH / 2f, barW * Mathf.Clamp01(currentPart.actionForce / 35f), barH), new Color(1f, 0.4f, 0.3f));
            infoY += statH;

            // 쿨다운
            DrawText(new Rect(infoX, infoY, labelW, statH), "쿨다운", 22,
                     new Color(0.6f, 0.6f, 0.65f), TextAnchor.MiddleLeft, bold: false, shadow: false);
            DrawText(new Rect(infoX + labelW, infoY, valW, statH), $"{currentPart.cooldown:F1}s", 24,
                     new Color(0.35f, 0.7f, 1f), TextAnchor.MiddleLeft);
            DrawRect(new Rect(bx, infoY + statH / 2f - barH / 2f, barW, barH), new Color(0.15f, 0.15f, 0.2f, 0.8f));
            DrawRect(new Rect(bx, infoY + statH / 2f - barH / 2f, barW * Mathf.Clamp01(currentPart.cooldown / 7f), barH), new Color(0.35f, 0.7f, 1f));
            infoY += statH;

            // 무게
            DrawText(new Rect(infoX, infoY, labelW, statH), "무게", 22,
                     new Color(0.6f, 0.6f, 0.65f), TextAnchor.MiddleLeft, bold: false, shadow: false);
            DrawText(new Rect(infoX + labelW, infoY, valW, statH), $"{currentPart.weight:F0}kg", 24,
                     new Color(0.95f, 0.8f, 0.25f), TextAnchor.MiddleLeft);
            DrawRect(new Rect(bx, infoY + statH / 2f - barH / 2f, barW, barH), new Color(0.15f, 0.15f, 0.2f, 0.8f));
            DrawRect(new Rect(bx, infoY + statH / 2f - barH / 2f, barW * Mathf.Clamp01(currentPart.weight / 150f), barH), new Color(0.95f, 0.8f, 0.25f));

            // ===== 좌우 화살표 =====
            float arrowW = 70 * s;
            float arrowH = 90 * s;
            float arrowY = contentY + contentH / 2f - arrowH / 2f;

            Rect leftArrow = new Rect(contentX - arrowW - 10 * s, arrowY, arrowW, arrowH);
            Rect rightArrow = new Rect(contentX + contentW + 10 * s, arrowY, arrowW, arrowH);

            // 왼쪽
            bool hoverL = leftArrow.Contains(Event.current.mousePosition);
            DrawText(leftArrow, "◀", 52,
                     hoverL ? Color.white : new Color(0.7f, 0.7f, 0.75f, 0.8f));
            if (Event.current.type == EventType.MouseDown && leftArrow.Contains(Event.current.mousePosition))
            {
                _partSystem.CyclePart(_partUISelectedSlot, -1);
                _partSystem.EquipSelected(_partUISelectedSlot, _player1);
                Event.current.Use();
            }

            // 오른쪽
            bool hoverR = rightArrow.Contains(Event.current.mousePosition);
            DrawText(rightArrow, "▶", 52,
                     hoverR ? Color.white : new Color(0.7f, 0.7f, 0.75f, 0.8f));
            if (Event.current.type == EventType.MouseDown && rightArrow.Contains(Event.current.mousePosition))
            {
                _partSystem.CyclePart(_partUISelectedSlot, 1);
                _partSystem.EquipSelected(_partUISelectedSlot, _player1);
                Event.current.Use();
            }

            // ===== 완료 버튼 =====
            float btnW = 320 * s;
            float btnH = 65 * s;
            float btnY = contentY + contentH + 20 * s;
            Rect confirmBtn = new Rect(cx - btnW / 2f, btnY, btnW, btnH);

            float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 2.5f);
            Color cfBg = new Color(0.05f * pulse, 0.38f * pulse, 0.08f * pulse, 0.95f);
            Color cfBorder = new Color(0.3f * pulse, 1f * pulse, 0.4f * pulse, 0.9f);

            // 글로우
            DrawRect(new Rect(confirmBtn.x - 4 * s, confirmBtn.y - 4 * s,
                             confirmBtn.width + 8 * s, confirmBtn.height + 8 * s),
                     new Color(0.1f, 0.8f, 0.2f, 0.06f * pulse));

            if (DrawButton(confirmBtn, "완료   [ Space ]", 30, cfBg, cfBorder, Color.white))
            {
                ConfirmPartSelection();
            }

            // ===== 조작 안내 =====
            float hintY = btnY + btnH + 18 * s;
            DrawText(new Rect(0, hintY, sw, 28 * s),
                     "[1/2/3] 슬롯 선택      [← →] 부품 변경      [Space] 완료", 19,
                     new Color(0.45f, 0.47f, 0.52f), bold: false, shadow: false);

            // 장착 현황
            float sumY = hintY + 32 * s;
            string fn = _partSystem.P1EquippedParts.ContainsKey(PartSlot.Front) ? _partSystem.P1EquippedParts[PartSlot.Front].partName : "없음";
            string rn = _partSystem.P1EquippedParts.ContainsKey(PartSlot.Rooftop) ? _partSystem.P1EquippedParts[PartSlot.Rooftop].partName : "없음";
            string bn = _partSystem.P1EquippedParts.ContainsKey(PartSlot.Rear) ? _partSystem.P1EquippedParts[PartSlot.Rear].partName : "없음";
            DrawText(new Rect(0, sumY, sw, 26 * s),
                     $"장착 현황:   앞범퍼 [{fn}]   |   루프탑 [{rn}]   |   뒷범퍼 [{bn}]", 18,
                     new Color(0.45f, 0.47f, 0.52f), bold: false, shadow: false);
        }

        /// <summary>
        /// 스탯 바 그리기 (UI 헬퍼 - 레거시 호환)
        /// </summary>
        private void DrawStatBar(float x, float y, float w, float h, float ratio, Color color)
        {
            ratio = Mathf.Clamp01(ratio);
            DrawRect(new Rect(x, y, w, h), new Color(0.15f, 0.15f, 0.2f, 0.8f));
            DrawRect(new Rect(x, y, w * ratio, h), color);
        }

        /// <summary>
        /// 부품 쿨다운 표시 (경기 중, 화면 하단 중앙)
        /// </summary>
        private void DrawPartCooldowns()
        {
            if (_partSystem == null) return;

            float s = S;
            float cx = Screen.width / 2f;
            float boxW = 170 * s;
            float boxH = 60 * s;
            float gap = 12 * s;
            float y = Screen.height - boxH - 15 * s;
            float startX = cx - (boxW * 3f + gap * 2f) / 2f;

            PartSlot[] slots = { PartSlot.Front, PartSlot.Rooftop, PartSlot.Rear };
            string[] keys = { "Q", "E", "R" };

            for (int i = 0; i < slots.Length; i++)
            {
                PartSlot slot = slots[i];
                float sx = startX + i * (boxW + gap);

                if (!_partSystem.P1EquippedParts.ContainsKey(slot)) continue;
                var part = _partSystem.P1EquippedParts[slot];
                float cd = _partSystem.GetCooldownRemaining(slot, true);
                bool ready = cd <= 0f;

                // 배경
                Color bgCol = ready ? new Color(0.08f, 0.35f, 0.1f, 0.8f) : new Color(0.35f, 0.08f, 0.08f, 0.8f);
                Color borderCol = ready ? new Color(0.2f, 0.8f, 0.3f, 0.6f) : new Color(0.6f, 0.15f, 0.1f, 0.5f);
                DrawRect(new Rect(sx, y, boxW, boxH), bgCol, borderCol, 1 * s);

                // 쿨다운 바
                if (!ready)
                {
                    float ratio = 1f - (cd / part.cooldown);
                    DrawRect(new Rect(sx + 2 * s, y + boxH - 8 * s, (boxW - 4 * s) * ratio, 5 * s),
                             new Color(0.2f, 0.7f, 0.3f, 0.6f));
                }

                DrawText(new Rect(sx, y + 4 * s, boxW, 28 * s),
                         $"[{keys[i]}] {part.partName}", 16,
                         ready ? new Color(0.3f, 1f, 0.4f) : new Color(0.5f, 0.5f, 0.55f),
                         shadow: false);

                string cdText = ready ? "READY" : $"{cd:F1}s";
                DrawText(new Rect(sx, y + 28 * s, boxW, 26 * s),
                         cdText, 20,
                         ready ? Color.white : new Color(1f, 0.3f, 0.2f));
            }
        }

        // ===== UI 헬퍼 메서드들 =====

        /// <summary>UI 스케일 팩터 (1080p 기준)</summary>
        private float S => Mathf.Max(Screen.height / 1080f, 0.5f);

        /// <summary>스케일된 폰트 크기</summary>
        private int FS(int baseSize) => Mathf.Max((int)(baseSize * S), 8);

        /// <summary>커스텀 폰트가 있으면 로드</summary>
        private void EnsureUIFont()
        {
            if (_uiFontLoaded) return;
            _uiFontLoaded = true;
            // Resources에서 커스텀 폰트 시도
            _uiFont = Resources.Load<Font>("UIFont");
            if (_uiFont == null) _uiFont = Resources.Load<Font>("Fonts/UIFont");
            // 없으면 시스템 폰트에서 게임스러운 볼드 폰트 로드
            if (_uiFont == null)
            {
                string[] gameFonts = { "Impact", "Arial Black", "Futura-CondensedExtraBold",
                                        "Helvetica Neue", "AppleSDGothicNeo-Bold", "Arial Bold" };
                foreach (var fontName in gameFonts)
                {
                    _uiFont = Font.CreateDynamicFontFromOSFont(fontName, 32);
                    if (_uiFont != null) break;
                }
            }
        }

        /// <summary>색상→텍스처 캐시</summary>
        private Texture2D GetTex(Color c)
        {
            // Color32로 변환 → uint 키
            Color32 c32 = c;
            uint key = ((uint)c32.r << 24) | ((uint)c32.g << 16) | ((uint)c32.b << 8) | c32.a;
            if (_uiTexCache.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            _uiTexCache[key] = tex;
            return tex;
        }

        /// <summary>사각형 그리기 (배경 + 선택적 테두리)</summary>
        private void DrawRect(Rect r, Color bgColor, Color borderColor = default, float borderW = 0f)
        {
            GUI.DrawTexture(r, GetTex(bgColor));
            if (borderW > 0f && borderColor.a > 0f)
            {
                var bt = GetTex(borderColor);
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, borderW), bt);
                GUI.DrawTexture(new Rect(r.x, r.yMax - borderW, r.width, borderW), bt);
                GUI.DrawTexture(new Rect(r.x, r.y, borderW, r.height), bt);
                GUI.DrawTexture(new Rect(r.xMax - borderW, r.y, borderW, r.height), bt);
            }
        }

        /// <summary>텍스트 + 그림자</summary>
        private void DrawText(Rect r, string text, int baseFontSize, Color color, TextAnchor align = TextAnchor.MiddleCenter, bool bold = true, bool shadow = true, bool wrap = false)
        {
            EnsureUIFont();
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = FS(baseFontSize),
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = align,
                wordWrap = wrap,
                clipping = TextClipping.Overflow
            };
            if (_uiFont != null) style.font = _uiFont;
            style.normal.textColor = color;

            if (shadow)
            {
                float off = Mathf.Max(2f * S, 1f);
                GUIStyle shadowStyle = new GUIStyle(style);
                shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
                GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), text, shadowStyle);
            }
            GUI.Label(r, text, style);
        }

        /// <summary>클릭 가능 버튼 (호버 효과 포함)</summary>
        private bool DrawButton(Rect r, string text, int baseFontSize, Color bgColor, Color borderColor, Color textColor)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color bg = hover ? Color.Lerp(bgColor, Color.white, 0.15f) : bgColor;
            Color border = hover ? Color.Lerp(borderColor, Color.white, 0.3f) : borderColor;
            float bw = Mathf.Max(2f * S, 1f);
            DrawRect(r, bg, border, bw);
            DrawText(r, text, baseFontSize, textColor);

            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 부품 프리뷰 아이콘 (2D로 부품 형태 표현)
        /// </summary>
        private void DrawPartPreviewIcon(float px, float py, float size, PartSlot slot, PartActionType actionType, Color accent)
        {
            float cx = px + size / 2f;
            float cy = py + size / 2f;
            Color dark = new Color(accent.r * 0.4f, accent.g * 0.4f, accent.b * 0.4f);
            Color light = Color.Lerp(accent, Color.white, 0.3f);

            if (slot == PartSlot.Front)
            {
                if (actionType == PartActionType.Shield)
                {
                    // 방패 모양
                    DrawRect(new Rect(cx - size * 0.35f, cy - size * 0.25f, size * 0.7f, size * 0.5f), accent);
                    DrawRect(new Rect(cx - size * 0.25f, cy - size * 0.15f, size * 0.5f, size * 0.3f), dark);
                    DrawText(new Rect(px, cy - size * 0.1f, size, size * 0.2f), "SHIELD", 18, light);
                }
                else
                {
                    // 펀치/돌진 범퍼
                    DrawRect(new Rect(cx - size * 0.3f, cy - size * 0.1f, size * 0.6f, size * 0.2f), accent);
                    DrawRect(new Rect(cx - size * 0.08f, cy - size * 0.25f, size * 0.16f, size * 0.15f), light); // 돌출
                    DrawRect(new Rect(cx - size * 0.15f, cy + size * 0.1f, size * 0.3f, size * 0.12f), dark); // 하부
                    DrawText(new Rect(px, cy + size * 0.25f, size, size * 0.15f), "BUMPER", 16, light);
                }
            }
            else if (slot == PartSlot.Rooftop)
            {
                if (actionType == PartActionType.Lift)
                {
                    // 리프트 장치
                    DrawRect(new Rect(cx - size * 0.2f, cy + size * 0.05f, size * 0.4f, size * 0.08f), dark); // 베이스
                    DrawRect(new Rect(cx - size * 0.03f, cy - size * 0.15f, size * 0.06f, size * 0.2f), accent); // 기둥
                    DrawRect(new Rect(cx - size * 0.15f, cy - size * 0.2f, size * 0.3f, size * 0.06f), light); // 포크
                    DrawText(new Rect(px, cy + size * 0.2f, size, size * 0.15f), "LIFT", 16, light);
                }
                else if (actionType == PartActionType.Slam)
                {
                    // 해머
                    DrawRect(new Rect(cx - size * 0.03f, cy - size * 0.05f, size * 0.06f, size * 0.2f), dark); // 자루
                    DrawRect(new Rect(cx - size * 0.15f, cy - size * 0.2f, size * 0.3f, size * 0.15f), accent); // 헤드
                    DrawText(new Rect(px, cy + size * 0.2f, size, size * 0.15f), "HAMMER", 16, light);
                }
                else
                {
                    // 디스크 (자기장/EMP)
                    float diskSize = size * 0.4f;
                    DrawRect(new Rect(cx - diskSize / 2f, cy - diskSize / 2f, diskSize, diskSize), accent);
                    float coreSize = size * 0.15f;
                    DrawRect(new Rect(cx - coreSize / 2f, cy - coreSize / 2f, coreSize, coreSize), light);
                    DrawText(new Rect(px, cy + size * 0.25f, size, size * 0.15f), "DEVICE", 16, light);
                }
            }
            else // Rear
            {
                if (actionType == PartActionType.Boost)
                {
                    // 부스터 - 배기관 2개
                    float pipeW = size * 0.12f;
                    float pipeH = size * 0.3f;
                    DrawRect(new Rect(cx - size * 0.15f - pipeW / 2f, cy - pipeH / 2f, pipeW, pipeH), accent);
                    DrawRect(new Rect(cx + size * 0.15f - pipeW / 2f, cy - pipeH / 2f, pipeW, pipeH), accent);
                    // 불꽃
                    DrawRect(new Rect(cx - size * 0.15f - pipeW * 0.3f, cy + pipeH / 2f, pipeW * 0.6f, size * 0.1f), light);
                    DrawRect(new Rect(cx + size * 0.15f - pipeW * 0.3f, cy + pipeH / 2f, pipeW * 0.6f, size * 0.1f), light);
                    DrawRect(new Rect(cx - size * 0.25f, cy - size * 0.05f, size * 0.5f, size * 0.1f), dark); // 탱크
                    DrawText(new Rect(px, cy + size * 0.3f, size, size * 0.12f), "BOOST", 16, light);
                }
                else if (actionType == PartActionType.SpikeDrop)
                {
                    // 스파이크 투하기
                    DrawRect(new Rect(cx - size * 0.2f, cy - size * 0.08f, size * 0.4f, size * 0.16f), accent);
                    float spikeS = size * 0.06f;
                    for (int i = 0; i < 4; i++)
                    {
                        float sx = cx - size * 0.15f + i * size * 0.1f;
                        DrawRect(new Rect(sx, cy + size * 0.1f, spikeS, spikeS), light);
                    }
                    DrawText(new Rect(px, cy + size * 0.25f, size, size * 0.12f), "SPIKE", 16, light);
                }
                else
                {
                    // 기타
                    DrawRect(new Rect(cx - size * 0.2f, cy - size * 0.1f, size * 0.4f, size * 0.2f), accent);
                    DrawRect(new Rect(cx - size * 0.06f, cy + size * 0.1f, size * 0.12f, size * 0.15f), dark);
                    DrawText(new Rect(px, cy + size * 0.28f, size, size * 0.12f), "REAR", 16, light);
                }
            }
        }

        // ===== 랜딩 페이지 =====

        /// <summary>
        /// 랜딩 페이지 UI (WaitingForPlayers && !_partUIOpen)
        /// 3단계: 0=경기장 선택, 1=체급 선택, 2=부품/준비
        /// </summary>
        private void DrawLandingPage()
        {
            float sw = Screen.width;
            float sh = Screen.height;
            float cx = sw / 2f;
            float s = S;

            // ===== 풀스크린 배경 =====
            DrawRect(new Rect(0, 0, sw, sh), new Color(0.03f, 0.03f, 0.10f, 0.85f));
            DrawRect(new Rect(0, 0, sw, sh * 0.03f), new Color(0f, 0f, 0f, 0.5f));
            DrawRect(new Rect(0, sh - 3 * s, sw, 3 * s), new Color(1f, 0.75f, 0.1f, 0.6f));

            // ===== 타이틀 =====
            float titleH = 80 * s;
            float titleY = sh * 0.03f;
            DrawRect(new Rect(cx - 400 * s, titleY - 5 * s, 800 * s, titleH + 30 * s),
                     new Color(1f, 0.8f, 0.1f, 0.04f));
            DrawText(new Rect(0, titleY, sw, titleH),
                     "BATTLE CAR SUMO", 72, new Color(1f, 0.88f, 0.25f));

            // ===== 단계 인디케이터 =====
            float indY = titleY + titleH + 10 * s;
            string[] stepNames = { "경기장 선택", "체급 선택", "부품 & 출전" };
            float stepW = 160 * s;
            float stepGap = 40 * s;
            float stepTotalW = stepW * 3f + stepGap * 2f;
            float stepStartX = cx - stepTotalW / 2f;

            for (int i = 0; i < 3; i++)
            {
                float sx = stepStartX + i * (stepW + stepGap);
                bool isCurrent = (i == _landingStep);
                bool isPast = (i < _landingStep);

                // 번호 원
                float circleSize = 28 * s;
                Rect circleRect = new Rect(sx + stepW / 2f - circleSize / 2f, indY, circleSize, circleSize);
                Color circBg = isCurrent ? new Color(1f, 0.85f, 0.2f, 0.9f)
                    : isPast ? new Color(0.3f, 0.8f, 0.4f, 0.7f)
                    : new Color(0.2f, 0.2f, 0.25f, 0.6f);
                DrawRect(circleRect, circBg);
                DrawText(circleRect, $"{i + 1}", 16,
                    isCurrent ? Color.black : isPast ? Color.white : new Color(0.5f, 0.5f, 0.55f));

                // 단계명
                Color nameCol = isCurrent ? new Color(1f, 0.92f, 0.5f)
                    : isPast ? new Color(0.5f, 0.8f, 0.55f)
                    : new Color(0.4f, 0.4f, 0.45f);
                DrawText(new Rect(sx, indY + circleSize + 4 * s, stepW, 24 * s),
                    stepNames[i], 16, nameCol, bold: isCurrent, shadow: false);

                // 연결선
                if (i < 2)
                {
                    float lineX = sx + stepW / 2f + circleSize / 2f + 4 * s;
                    float lineEndX = stepStartX + (i + 1) * (stepW + stepGap) + stepW / 2f - circleSize / 2f - 4 * s;
                    DrawRect(new Rect(lineX, indY + circleSize / 2f - 1 * s, lineEndX - lineX, 2 * s),
                        isPast ? new Color(0.3f, 0.8f, 0.4f, 0.5f) : new Color(0.25f, 0.25f, 0.3f, 0.4f));
                }
            }

            // 구분선
            float divY = indY + 55 * s;
            DrawRect(new Rect(cx - 350 * s, divY, 700 * s, 2 * s),
                new Color(1f, 0.85f, 0.3f, 0.2f));

            // ===== 단계별 콘텐츠 =====
            float contentY = divY + 20 * s;
            switch (_landingStep)
            {
                case 0: DrawLandingStep_Arena(sw, sh, cx, s, contentY); break;
                case 1: DrawLandingStep_Weight(sw, sh, cx, s, contentY); break;
                case 2: DrawLandingStep_Parts(sw, sh, cx, s, contentY); break;
            }
        }

        // ===== Step 0: 경기장 선택 =====
        private void DrawLandingStep_Arena(float sw, float sh, float cx, float s, float startY)
        {
            DrawText(new Rect(0, startY, sw, 50 * s),
                "ARENA SELECT", 44, new Color(0.9f, 0.92f, 1f));
            DrawText(new Rect(0, startY + 50 * s, sw, 30 * s),
                "경기장을 선택하세요", 22, new Color(0.6f, 0.62f, 0.7f), bold: false, shadow: false);

            float cardY = startY + 100 * s;
            ArenaType[] arenaTypes = { ArenaType.Classic, ArenaType.Square, ArenaType.Punching };
            string[] arenaNames = { "클래식", "스퀘어", "펀칭" };
            string[] arenaEng = { "CLASSIC", "SQUARE", "PUNCHING" };
            string[] arenaDesc = {
                "원형 아레나\n기본적인 스모 경기장",
                "사각형 + 굴곡 바닥\n울퉁불퉁한 지형이 변수!",
                "대형 + 펀칭머신 4개\n경계에 가면 튕겨나간다!"
            };
            Color[] arenaColors = {
                new Color(0.4f, 0.85f, 1f),
                new Color(1f, 0.7f, 0.2f),
                new Color(1f, 0.3f, 0.4f)
            };

            float cardW = Mathf.Min(280 * s, (sw - 80 * s) / 3f);
            float cardH = 180 * s;
            float cardGap = 20 * s;
            float cardStartX = cx - (cardW * 3f + cardGap * 2f) / 2f;

            for (int i = 0; i < 3; i++)
            {
                bool isActive = (_selectedArenaType == arenaTypes[i]);
                float cxPos = cardStartX + i * (cardW + cardGap);
                Rect cardRect = new Rect(cxPos, cardY, cardW, cardH);

                Color bg = isActive
                    ? new Color(arenaColors[i].r * 0.15f, arenaColors[i].g * 0.15f, arenaColors[i].b * 0.15f, 0.95f)
                    : new Color(0.06f, 0.06f, 0.10f, 0.8f);
                Color border = isActive ? arenaColors[i] : new Color(0.2f, 0.2f, 0.25f, 0.5f);
                DrawRect(cardRect, bg, border, isActive ? 3 * s : 1 * s);

                if (isActive)
                    DrawRect(new Rect(cxPos, cardY, cardW, 5 * s), arenaColors[i]);

                // 영문명
                DrawText(new Rect(cxPos, cardY + 15 * s, cardW, 35 * s),
                    arenaEng[i], 30, isActive ? arenaColors[i] : new Color(0.4f, 0.4f, 0.45f));
                // 한글명
                DrawText(new Rect(cxPos, cardY + 52 * s, cardW, 30 * s),
                    arenaNames[i], 22, isActive ? new Color(0.85f, 0.87f, 0.92f) : new Color(0.35f, 0.35f, 0.4f),
                    bold: false, shadow: false);
                // 설명
                DrawText(new Rect(cxPos + 15 * s, cardY + 90 * s, cardW - 30 * s, 80 * s),
                    arenaDesc[i], 16, isActive ? new Color(0.7f, 0.72f, 0.78f) : new Color(0.3f, 0.3f, 0.35f),
                    TextAnchor.UpperCenter, bold: false, shadow: false);

                if (Event.current.type == EventType.MouseDown && cardRect.Contains(Event.current.mousePosition))
                {
                    if (_selectedArenaType != arenaTypes[i])
                    {
                        _selectedArenaType = arenaTypes[i];
                        RebuildArena();
                    }
                    Event.current.Use();
                }
            }

            // 하단 버튼
            float btnY = cardY + cardH + 40 * s;
            DrawLandingNavButtons(sw, sh, cx, s, btnY, showBack: false, showStart: false);
        }

        // ===== Step 1: 체급 선택 =====
        private void DrawLandingStep_Weight(float sw, float sh, float cx, float s, float startY)
        {
            DrawText(new Rect(0, startY, sw, 50 * s),
                "WEIGHT CLASS", 44, new Color(0.9f, 0.92f, 1f));
            DrawText(new Rect(0, startY + 50 * s, sw, 30 * s),
                "차량 체급을 선택하세요", 22, new Color(0.6f, 0.62f, 0.7f), bold: false, shadow: false);

            float cardY = startY + 100 * s;
            WeightClass[] classes = { WeightClass.Light, WeightClass.Middle, WeightClass.Heavy };
            string[] classNames = { "경량급", "중량급", "헤비급" };
            string[] classEng = { "LIGHT", "MIDDLE", "HEAVY" };
            string[] classDesc = { "빠른 속도\n높은 기동성", "균형 잡힌\n올라운드 성능", "강력한 밀기\n높은 내구성" };
            Color[] classColors = {
                new Color(0.2f, 1f, 0.45f),
                new Color(0.35f, 0.65f, 1f),
                new Color(1f, 0.35f, 0.25f)
            };

            WeightClass currentClass = _player1 != null ? _player1.CurrentWeightClass : WeightClass.Middle;
            float cardW = Mathf.Min(280 * s, (sw - 80 * s) / 3f);
            float cardH = 220 * s;
            float cardGap = 20 * s;
            float cardStartX = cx - (cardW * 3f + cardGap * 2f) / 2f;

            for (int i = 0; i < 3; i++)
            {
                bool isActive = (currentClass == classes[i]);
                float cxPos = cardStartX + i * (cardW + cardGap);
                Rect cardRect = new Rect(cxPos, cardY, cardW, cardH);

                Color bg = isActive
                    ? new Color(classColors[i].r * 0.15f, classColors[i].g * 0.15f, classColors[i].b * 0.15f, 0.95f)
                    : new Color(0.06f, 0.06f, 0.10f, 0.8f);
                Color border = isActive ? classColors[i] : new Color(0.2f, 0.2f, 0.25f, 0.5f);
                DrawRect(cardRect, bg, border, isActive ? 3 * s : 1 * s);

                if (isActive)
                    DrawRect(new Rect(cxPos, cardY, cardW, 5 * s), classColors[i]);

                // 영문
                DrawText(new Rect(cxPos, cardY + 15 * s, cardW, 35 * s),
                    classEng[i], 30, isActive ? classColors[i] : new Color(0.4f, 0.4f, 0.45f));
                // 한글
                DrawText(new Rect(cxPos, cardY + 52 * s, cardW, 30 * s),
                    classNames[i], 24, isActive ? new Color(0.85f, 0.87f, 0.92f) : new Color(0.35f, 0.35f, 0.4f),
                    bold: false, shadow: false);
                // 설명
                DrawText(new Rect(cxPos + 10 * s, cardY + 90 * s, cardW - 20 * s, 50 * s),
                    classDesc[i], 17, isActive ? new Color(0.7f, 0.72f, 0.78f) : new Color(0.3f, 0.3f, 0.35f),
                    TextAnchor.UpperCenter, bold: false, shadow: false);

                // 스탯 바
                if (_gameConfig != null)
                {
                    WeightClassPhysics phys = _gameConfig.GetPhysicsForClass(classes[i]);
                    float barY2 = cardY + 150 * s;
                    float barW2 = cardW * 0.7f;
                    float barX2 = cxPos + (cardW - barW2) / 2f;
                    float barH2 = 10 * s;
                    Color barCol = isActive ? classColors[i] : new Color(0.3f, 0.3f, 0.35f, 0.5f);

                    DrawText(new Rect(barX2, barY2 - 16 * s, barW2, 16 * s),
                        "SPD", 12, barCol, TextAnchor.MiddleLeft, bold: false, shadow: false);
                    DrawRect(new Rect(barX2, barY2, barW2, barH2), new Color(0.12f, 0.12f, 0.18f, 0.8f));
                    DrawRect(new Rect(barX2, barY2, barW2 * Mathf.Clamp01(phys.maxSpeed / 14f), barH2), barCol);

                    DrawText(new Rect(barX2, barY2 + barH2 + 4 * s, barW2, 16 * s),
                        "PWR", 12, barCol, TextAnchor.MiddleLeft, bold: false, shadow: false);
                    DrawRect(new Rect(barX2, barY2 + barH2 + 20 * s, barW2, barH2), new Color(0.12f, 0.12f, 0.18f, 0.8f));
                    DrawRect(new Rect(barX2, barY2 + barH2 + 20 * s, barW2 * Mathf.Clamp01(phys.baseMass / 2000f), barH2), barCol);
                }

                if (Event.current.type == EventType.MouseDown && cardRect.Contains(Event.current.mousePosition))
                {
                    if (_player1 != null)
                    {
                        _player1.SetWeightClass(classes[i]);
                        SwapVehicleModel(_player1, P1_COLOR);
                        ApplyVehicleColor(_player1.gameObject, P1_COLOR);
                        // ★ 체급 변경 → 부품 AP 위치 업데이트
                        if (_partSystem != null)
                        {
                            foreach (var kvp in _partSystem.P1EquippedParts)
                                _partSystem.UpdatePartVisual(kvp.Key, _player1, kvp.Value);
                        }
                    }
                    Event.current.Use();
                }
            }

            // Tab 힌트
            DrawText(new Rect(0, cardY + cardH + 8 * s, sw, 22 * s),
                "[Tab] 으로 변경", 16, new Color(0.45f, 0.45f, 0.5f), bold: false, shadow: false);

            float btnY = cardY + cardH + 40 * s;
            DrawLandingNavButtons(sw, sh, cx, s, btnY, showBack: true, showStart: false);
        }

        // ===== Step 2: 부품 & 출전 =====
        private void DrawLandingStep_Parts(float sw, float sh, float cx, float s, float startY)
        {
            DrawText(new Rect(0, startY, sw, 50 * s),
                "PARTS & READY", 44, new Color(0.9f, 0.92f, 1f));
            DrawText(new Rect(0, startY + 50 * s, sw, 30 * s),
                "부품을 확인하고 출전하세요!", 22, new Color(0.6f, 0.62f, 0.7f), bold: false, shadow: false);

            // 선택 요약
            float sumY = startY + 95 * s;
            string arenaName = _selectedArenaType switch {
                ArenaType.Classic => "클래식", ArenaType.Square => "스퀘어", _ => "펀칭"
            };
            string weightName = (_player1 != null ? _player1.CurrentWeightClass : WeightClass.Middle) switch {
                WeightClass.Light => "경량급", WeightClass.Heavy => "헤비급", _ => "중량급"
            };
            DrawText(new Rect(0, sumY, sw, 28 * s),
                $"경기장: {arenaName}    |    체급: {weightName}", 20,
                new Color(0.7f, 0.75f, 0.85f), bold: false, shadow: false);

            // 부품 카드
            float cardY = sumY + 45 * s;
            PartSlot[] slots = { PartSlot.Front, PartSlot.Rooftop, PartSlot.Rear };
            string[] slotLabels = { "FRONT", "ROOFTOP", "REAR" };
            string[] slotDesc = { "앞 범퍼  [Q]", "루프탑  [E]", "뒷 범퍼  [R]" };

            float cardW = Mathf.Min(280 * s, (sw - 80 * s) / 3f);
            float cardH = 120 * s;
            float cardGap = 20 * s;
            float cardStartX = cx - (cardW * 3f + cardGap * 2f) / 2f;

            for (int i = 0; i < slots.Length; i++)
            {
                float px = cardStartX + i * (cardW + cardGap);
                Rect cardRect = new Rect(px, cardY, cardW, cardH);

                DrawRect(cardRect, new Color(0.06f, 0.06f, 0.10f, 0.85f),
                    new Color(0.3f, 0.3f, 0.38f, 0.5f), 1 * s);

                // 슬롯 라벨
                DrawText(new Rect(px, cardY + 8 * s, cardW, 22 * s),
                    slotLabels[i], 18, new Color(0.5f, 0.52f, 0.58f), bold: false, shadow: false);
                DrawText(new Rect(px, cardY + 28 * s, cardW, 20 * s),
                    slotDesc[i], 14, new Color(0.4f, 0.42f, 0.48f), bold: false, shadow: false);

                // 장착 부품명
                string partName = "없음";
                Color partColor = new Color(0.4f, 0.4f, 0.4f);
                if (_partSystem != null && _partSystem.P1EquippedParts.ContainsKey(slots[i]))
                {
                    partName = _partSystem.P1EquippedParts[slots[i]].partName;
                    partColor = _partSystem.P1EquippedParts[slots[i]].partColor;
                }

                float indW = 6 * s;
                DrawRect(new Rect(px, cardY, indW, cardH), partColor);

                DrawText(new Rect(px, cardY + 60 * s, cardW, 40 * s),
                    partName, 28, partColor);
            }

            // 부품교체 버튼
            float pBtnY = cardY + cardH + 20 * s;
            float pBtnW = 260 * s;
            float pBtnH = 55 * s;
            Rect partsBtn = new Rect(cx - pBtnW / 2f, pBtnY, pBtnW, pBtnH);
            if (DrawButton(partsBtn, "PARTS [P]", 24,
                new Color(0.10f, 0.18f, 0.35f, 0.9f),
                new Color(0.3f, 0.5f, 1f, 0.8f), Color.white))
            {
                _partUIOpen = true;
            }

            float btnY = pBtnY + pBtnH + 25 * s;
            DrawLandingNavButtons(sw, sh, cx, s, btnY, showBack: true, showStart: true);
        }

        // ===== 하단 네비게이션 버튼 (이전/다음/게임시작/종료) =====
        private void DrawLandingNavButtons(float sw, float sh, float cx, float s, float btnY,
            bool showBack, bool showStart)
        {
            float btnW = 260 * s;
            float btnH = 60 * s;
            float btnGap = 20 * s;

            // 버튼 개수 계산
            int btnCount = 1; // 항상 종료 or 다음
            if (showBack) btnCount++;
            if (showStart) btnCount++; // 게임시작
            else btnCount++; // 다음 단계

            float totalW = btnW * btnCount + btnGap * (btnCount - 1);
            float bx = cx - totalW / 2f;
            int idx = 0;

            // --- 이전 단계 ---
            if (showBack)
            {
                Rect backBtn = new Rect(bx + idx * (btnW + btnGap), btnY, btnW, btnH);
                if (DrawButton(backBtn, "◀  BACK", 24,
                    new Color(0.12f, 0.12f, 0.18f, 0.9f),
                    new Color(0.4f, 0.4f, 0.5f, 0.7f), new Color(0.8f, 0.8f, 0.85f)))
                {
                    _landingStep--;
                }
                idx++;
            }

            // --- 다음 단계 / 게임시작 ---
            if (showStart)
            {
                // 게임시작 버튼 (펄스 글로우)
                Rect startBtn = new Rect(bx + idx * (btnW + btnGap), btnY, btnW, btnH);
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 2.5f);
                Color startBg = new Color(0.05f * pulse, 0.35f * pulse, 0.08f * pulse, 0.95f);
                Color startBorder = new Color(0.3f * pulse, 1f * pulse, 0.4f * pulse, 0.9f);

                DrawRect(new Rect(startBtn.x - 4 * s, startBtn.y - 4 * s,
                    startBtn.width + 8 * s, startBtn.height + 8 * s),
                    new Color(0.1f, 0.8f, 0.2f, 0.08f * pulse));

                if (DrawButton(startBtn, "START [Space]", 26, startBg, startBorder, Color.white))
                {
                    StartCountdown();
                }
            }
            else
            {
                Rect nextBtn = new Rect(bx + idx * (btnW + btnGap), btnY, btnW, btnH);
                if (DrawButton(nextBtn, "NEXT  ▶", 24,
                    new Color(0.08f, 0.20f, 0.40f, 0.9f),
                    new Color(0.3f, 0.6f, 1f, 0.8f), Color.white))
                {
                    _landingStep++;
                }
            }
            idx++;

            // --- 종료 ---
            Rect quitBtn = new Rect(bx + idx * (btnW + btnGap), btnY, btnW, btnH);
            if (DrawButton(quitBtn, "QUIT [Esc]", 24,
                new Color(0.25f, 0.08f, 0.08f, 0.9f),
                new Color(0.8f, 0.25f, 0.2f, 0.7f), new Color(0.9f, 0.7f, 0.7f)))
            {
                QuitGame();
            }

            // 키 안내
            float hintY = btnY + btnH + 15 * s;
            string hints = showStart
                ? "[Space] START   [←] BACK   [Esc] QUIT   [F1] AI Toggle"
                : showBack
                    ? "[→] NEXT   [←] BACK   [Esc] QUIT"
                    : "[→/Space] NEXT   [Esc] QUIT";
            DrawText(new Rect(0, hintY, sw, 24 * s), hints, 15,
                new Color(0.4f, 0.42f, 0.48f), bold: false, shadow: false);
        }

        /// <summary>
        /// 차량 위에 떠다니는 라벨 표시
        /// </summary>
        private void DrawPlayerLabel(OfflineVehicleController player, string label, Color color)
        {
            if (player == null) return;

            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 worldPos = player.transform.position + Vector3.up * 3f;
            Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
            if (screenPos.z < 0) return;

            float guiY = Screen.height - screenPos.y;
            float s = S;
            float lw = 140 * s;
            float lh = 35 * s;

            DrawText(new Rect(screenPos.x - lw / 2f, guiY - lh, lw, lh),
                     label, 20, color);
        }

        /// <summary>
        /// 플레이어 차량 본체에 색상 적용 (파란/빨간)
        /// </summary>
        private void ApplyPlayerColors()
        {
            if (_player1 != null)
            {
                SwapVehicleModel(_player1, P1_COLOR);
                ApplyVehicleColor(_player1.gameObject, P1_COLOR);
            }
            if (_player2 != null)
            {
                SwapVehicleModel(_player2, P2_COLOR);
                ApplyVehicleColor(_player2.gameObject, P2_COLOR);
            }

            // ★ 모델 교체 후 부품 비주얼 갱신 (AP 위치 업데이트)
            if (_partSystem != null)
                _partSystem.RefreshAllVisuals(_player1, _player2);
        }

        /// <summary>
        /// 체급에 맞는 차량 비주얼 적용 (FBX 시도 → 실패 시 프로시저럴 빌드)
        /// </summary>
        private void SwapVehicleModel(OfflineVehicleController vehicle, Color teamColor)
        {
            if (vehicle == null) return;

            try
            {
                // ★ 원본 프리미티브 메시 숨김 (항상 - 물리 Collider/Rigidbody는 유지)
                MeshRenderer origRenderer = vehicle.GetComponent<MeshRenderer>();
                if (origRenderer != null)
                    origRenderer.enabled = false;

                // 기존 모델 자식 제거
                Transform oldModel = vehicle.transform.Find("_VehicleModel");
                if (oldModel != null)
                    DestroyImmediate(oldModel.gameObject);

                // ★ FBX 로드 시도
                string modelPath = vehicle.CurrentWeightClass switch
                {
                    WeightClass.Light => "Models/Vehicles/vehicle_light",
                    WeightClass.Heavy => "Models/Vehicles/vehicle_heavy",
                    _ => "Models/Vehicles/vehicle_middle"
                };

                GameObject prefab = Resources.Load<GameObject>(modelPath);
                GameObject model;

                if (prefab != null)
                {
                    model = Instantiate(prefab, vehicle.transform);
                    Renderer[] rs = model.GetComponentsInChildren<Renderer>(true);
                    Debug.Log($"[MODEL] ★ {vehicle.name} → FBX 로드 성공! 렌더러={rs.Length}, 자식={model.transform.childCount}");
                }
                else
                {
                    Debug.Log($"[MODEL] FBX '{modelPath}' 없음 → 프로시저럴 차량 빌드");
                    model = BuildProceduralVehicle(vehicle.CurrentWeightClass);
                    model.transform.SetParent(vehicle.transform);
                }

                model.name = "_VehicleModel";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                // 모델 내 모든 콜라이더 제거 (물리 간섭 방지)
                foreach (Collider col in model.GetComponentsInChildren<Collider>())
                    DestroyImmediate(col);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MODEL] SwapVehicleModel 실패 ({vehicle.name}): {e.Message}");
                // 실패 시 원본 프리미티브 다시 보이기
                MeshRenderer mr = vehicle.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = true;
            }
        }

        /// <summary>
        /// 프리미티브 조합으로 자동차 모양 빌드 (FBX 폴백)
        /// 체급별 크기: Light=작고 날렵, Middle=중간, Heavy=크고 각짐
        /// </summary>
        private GameObject BuildProceduralVehicle(WeightClass weightClass)
        {
            GameObject root = new GameObject("ProceduralVehicle");

            // 체급별 치수
            float bodyL, bodyW, bodyH, cabinL, cabinW, cabinH, cabinZ, wheelR, wheelW;
            switch (weightClass)
            {
                case WeightClass.Light:
                    bodyL = 2.0f; bodyW = 1.2f; bodyH = 0.4f;
                    cabinL = 0.9f; cabinW = 1.0f; cabinH = 0.35f; cabinZ = -0.1f;
                    wheelR = 0.18f; wheelW = 0.12f;
                    break;
                case WeightClass.Heavy:
                    bodyL = 2.8f; bodyW = 1.8f; bodyH = 0.6f;
                    cabinL = 1.2f; cabinW = 1.5f; cabinH = 0.5f; cabinZ = -0.2f;
                    wheelR = 0.25f; wheelW = 0.18f;
                    break;
                default: // Middle
                    bodyL = 2.4f; bodyW = 1.5f; bodyH = 0.5f;
                    cabinL = 1.0f; cabinW = 1.2f; cabinH = 0.4f; cabinZ = -0.15f;
                    wheelR = 0.22f; wheelW = 0.15f;
                    break;
            }

            float bodyY = wheelR + bodyH / 2f; // 바퀴 위에 차체

            // --- 차체 (Body) ---
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = new Vector3(0f, bodyY, 0f);
            body.transform.localScale = new Vector3(bodyW, bodyH, bodyL);

            // --- 앞쪽 경사면 (Hood / Nose) ---
            GameObject hood = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hood.name = "Hood";
            hood.transform.SetParent(root.transform);
            float hoodZ = bodyL * 0.32f;
            hood.transform.localPosition = new Vector3(0f, bodyY + bodyH * 0.15f, hoodZ);
            hood.transform.localScale = new Vector3(bodyW * 0.9f, bodyH * 0.4f, bodyL * 0.25f);
            hood.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);

            // --- 캐빈 (Cabin) ---
            GameObject cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = "Cabin";
            cabin.transform.SetParent(root.transform);
            cabin.transform.localPosition = new Vector3(0f, bodyY + bodyH / 2f + cabinH / 2f, cabinZ);
            cabin.transform.localScale = new Vector3(cabinW, cabinH, cabinL);

            // --- 범퍼 (Front Bumper) ---
            GameObject bumperF = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bumperF.name = "BumperFront";
            bumperF.transform.SetParent(root.transform);
            bumperF.transform.localPosition = new Vector3(0f, bodyY - bodyH * 0.1f, bodyL / 2f + 0.08f);
            bumperF.transform.localScale = new Vector3(bodyW * 1.05f, bodyH * 0.5f, 0.15f);

            // --- 범퍼 (Rear Bumper) ---
            GameObject bumperR = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bumperR.name = "BumperRear";
            bumperR.transform.SetParent(root.transform);
            bumperR.transform.localPosition = new Vector3(0f, bodyY - bodyH * 0.1f, -bodyL / 2f - 0.08f);
            bumperR.transform.localScale = new Vector3(bodyW * 1.05f, bodyH * 0.5f, 0.15f);

            // --- 바퀴 4개 (Wheel) ---
            float wx = bodyW / 2f + wheelW / 2f;
            float wz = bodyL * 0.32f;
            float wy = wheelR;
            string[] wheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            Vector3[] wheelPos = {
                new Vector3(-wx, wy, wz),
                new Vector3(wx, wy, wz),
                new Vector3(-wx, wy, -wz),
                new Vector3(wx, wy, -wz),
            };
            for (int i = 0; i < 4; i++)
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = wheelNames[i];
                wheel.transform.SetParent(root.transform);
                wheel.transform.localPosition = wheelPos[i];
                wheel.transform.localScale = new Vector3(wheelR * 2f, wheelW / 2f, wheelR * 2f);
                wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            // --- Attachment Points (빈 오브젝트) ---
            GameObject apFront = new GameObject("AP_Front");
            apFront.transform.SetParent(root.transform);
            apFront.transform.localPosition = new Vector3(0f, bodyY, bodyL / 2f + 0.15f);

            GameObject apRoof = new GameObject("AP_Rooftop");
            apRoof.transform.SetParent(root.transform);
            apRoof.transform.localPosition = new Vector3(0f, bodyY + bodyH / 2f + cabinH + 0.05f, cabinZ);

            GameObject apRear = new GameObject("AP_Rear");
            apRear.transform.SetParent(root.transform);
            apRear.transform.localPosition = new Vector3(0f, bodyY, -bodyL / 2f - 0.15f);

            return root;
        }

        /// <summary>
        /// 씬에서 URP Lit 머티리얼을 찾아 복사하는 헬퍼 (Shader.Find 실패 대비)
        /// </summary>
        private Material _cachedBaseMaterial;
        private Material GetSafeBaseMaterial(Material source)
        {
            if (source != null) return new Material(source);

            // source가 null → 캐시된 머티리얼 사용
            if (_cachedBaseMaterial != null) return new Material(_cachedBaseMaterial);

            // 씬에서 아무 렌더러의 머티리얼을 가져와서 캐시
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.sharedMaterial != null)
                {
                    _cachedBaseMaterial = r.sharedMaterial;
                    return new Material(_cachedBaseMaterial);
                }
            }

            // 최종 폴백: URP에서 Standard 셰이더 없음 → URP Lit 사용
            string[] shaderNames = {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "Standard",
                "Sprites/Default"
            };
            foreach (string sn in shaderNames)
            {
                Shader s = Shader.Find(sn);
                if (s != null) return new Material(s);
            }

            Debug.LogWarning("[MODEL] 사용 가능한 셰이더 없음 - null 반환");
            return null;
        }

        private void ApplyVehicleColor(GameObject vehicle, Color baseColor)
        {
            if (vehicle == null) return;
            foreach (Renderer r in vehicle.GetComponentsInChildren<Renderer>())
            {
                if (r == null) continue;
                string n = r.gameObject.name.ToLower();

                // 부품 오브젝트 스킵 (별도 색상 관리)
                if (n.Contains("part_")) continue;

                // ★ null-safe 머티리얼 생성
                Material m = GetSafeBaseMaterial(r.sharedMaterial);
                if (m == null) continue; // 셰이더 없으면 스킵

                if (n.Contains("wheel"))
                {
                    // 바퀴: 짙은 회색
                    Color wheelColor = new Color(0.15f, 0.15f, 0.18f);
                    m.color = wheelColor;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", wheelColor);
                }
                else if (n.Contains("body") || n.Contains("hood") || n.Contains("armor") || n.Contains("bull"))
                {
                    // 차체/후드/장갑: 팀 컬러
                    m.color = baseColor;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
                }
                else if (n.Contains("cabin"))
                {
                    // 캐빈: 팀 컬러 + 밝게 (유리 느낌)
                    Color cabinColor = Color.Lerp(baseColor, Color.white, 0.5f);
                    m.color = cabinColor;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cabinColor);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.9f);
                }
                else if (n.Contains("bumper"))
                {
                    // 범퍼: 팀 컬러 약간 어둡게
                    Color bumperColor = Color.Lerp(baseColor, Color.black, 0.3f);
                    m.color = bumperColor;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", bumperColor);
                }
                else
                {
                    // 기타: 팀 컬러
                    m.color = baseColor;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
                }

                r.material = m;
            }
        }

        private void LateUpdate()
        {
            // ★ 동적 카메라: 두 플레이어 추적 + 부드러운 줌인/아웃
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 arenaCenter = _arenaCenter != null ? _arenaCenter.position : new Vector3(0f, 1f, 0f);

                // --- 목표 시점(lookAt)과 줌 계산 ---
                float targetZoom;
                Vector3 targetLookAt;

                bool hasP1 = _player1 != null && _player1.gameObject.activeInHierarchy;
                bool hasP2 = _player2 != null && _player2.gameObject.activeInHierarchy;

                if (_currentState == GameState.RoundEnd || _currentState == GameState.MatchEnd)
                {
                    // 라운드/매치 종료: 전체 아레나가 보이도록 크게 줌아웃
                    targetZoom = CAM_MAX_ZOOM;
                    targetLookAt = arenaCenter;
                }
                else if (_currentState == GameState.WaitingForPlayers || _currentState == GameState.Countdown)
                {
                    // 대기/카운트다운: 아레나 중심, 적당한 줌
                    targetZoom = 1.2f;
                    targetLookAt = arenaCenter;
                }
                else if (hasP1 && hasP2)
                {
                    // ★ Playing: 두 플레이어 사이 중점 추적 + 거리 기반 줌
                    Vector3 p1Pos = _player1.transform.position;
                    Vector3 p2Pos = _player2.transform.position;
                    Vector3 midpoint = (p1Pos + p2Pos) * 0.5f;

                    // 시점: 중점과 아레나 중심 사이 (중점에 70% 가중치)
                    targetLookAt = Vector3.Lerp(arenaCenter, midpoint, 0.7f);

                    // 두 플레이어 간 수평 거리
                    float playerDist = Vector3.Distance(
                        new Vector3(p1Pos.x, 0f, p1Pos.z),
                        new Vector3(p2Pos.x, 0f, p2Pos.z));

                    // 아레나 중심에서 가장 먼 플레이어 거리 (화면 밖 방지)
                    float maxDistFromCenter = Mathf.Max(
                        Vector3.Distance(new Vector3(p1Pos.x, 0f, p1Pos.z), new Vector3(arenaCenter.x, 0f, arenaCenter.z)),
                        Vector3.Distance(new Vector3(p2Pos.x, 0f, p2Pos.z), new Vector3(arenaCenter.x, 0f, arenaCenter.z)));

                    // 두 가지 기준 중 더 큰 줌 채택
                    float zoomByPlayerDist = Mathf.Lerp(CAM_MIN_ZOOM, CAM_MAX_ZOOM,
                        Mathf.InverseLerp(CAM_PLAYER_DIST_MIN, CAM_PLAYER_DIST_MAX, playerDist));
                    float zoomByArenaEdge = Mathf.Lerp(CAM_MIN_ZOOM, CAM_MAX_ZOOM,
                        Mathf.InverseLerp(CAM_PLAYER_DIST_MIN, CAM_PLAYER_DIST_MAX, maxDistFromCenter * 1.5f));
                    targetZoom = Mathf.Max(zoomByPlayerDist, zoomByArenaEdge);
                }
                else
                {
                    targetZoom = 1f;
                    targetLookAt = arenaCenter;
                }

                // --- 부드러운 보간 적용 ---
                _cameraCurrentZoom = Mathf.Lerp(_cameraCurrentZoom, targetZoom, Time.deltaTime * CAM_ZOOM_SPEED);
                _cameraCurrentLookAt = Vector3.Lerp(_cameraCurrentLookAt, targetLookAt, Time.deltaTime * CAM_LOOK_SPEED);

                // --- 카메라 위치 및 방향 적용 ---
                Vector3 zoomedOffset = _cameraOffset * _cameraCurrentZoom;
                cam.transform.position = _cameraCurrentLookAt + zoomedOffset;
                cam.transform.LookAt(_cameraCurrentLookAt);
            }

            var kb = Keyboard.current;
            if (kb == null) return;

            // 부품 UI 열려있거나 랜딩 페이지일 때는 글로벌 단축키 무시
            // (각 상태에서 자체 입력 처리)
            if (_partUIOpen) return;
            if (_currentState == GameState.WaitingForPlayers) return;

            // 단축키 처리 (Playing, RoundEnd, MatchEnd 등에서만)
            if (kb.spaceKey.wasPressedThisFrame && _currentState == GameState.MatchEnd)
            {
                RestartMatch();
            }
            if (kb.escapeKey.wasPressedThisFrame && _currentState == GameState.MatchEnd)
            {
                QuitGame();
            }
            if (kb.tabKey.wasPressedThisFrame)
            {
                CycleWeightClass();
            }
            if (kb.f1Key.wasPressedThisFrame)
            {
                _enableAI = !_enableAI;
                Debug.Log($"AI {(_enableAI ? "활성화" : "비활성화")}");
            }
        }

        private void RestartMatch()
        {
            _player1Score = 0;
            _player2Score = 0;
            _currentRound = 1;
            StartCountdown();
            Debug.Log("=== 매치 재시작! ===");
        }

        private void QuitGame()
        {
            Debug.Log("=== 게임 종료 ===");
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }

        private void CycleWeightClass()
        {
            if (_player1 == null) return;

            WeightClass current = _player1.CurrentWeightClass;
            WeightClass next = current switch
            {
                WeightClass.Light => WeightClass.Middle,
                WeightClass.Middle => WeightClass.Heavy,
                WeightClass.Heavy => WeightClass.Light,
                _ => WeightClass.Middle
            };

            _player1.SetWeightClass(next);
            SwapVehicleModel(_player1, P1_COLOR);
            ApplyVehicleColor(_player1.gameObject, P1_COLOR);
            // ★ 체급 변경 → 부품 AP 위치 업데이트
            if (_partSystem != null)
            {
                foreach (var kvp in _partSystem.P1EquippedParts)
                    _partSystem.UpdatePartVisual(kvp.Key, _player1, kvp.Value);
            }
            Debug.Log($"Player 1 체급 변경: {current} → {next}");
        }

        #endregion

        #region Environment Builder (런타임 자동 생성)

        private Shader _cachedLitShader;

        /// <summary>
        /// 하늘 + 초원 + 경기장 환경을 런타임에 자동 생성합니다.
        /// </summary>
        private void BuildEnvironment()
        {
            float arenaRadius = GetCurrentArenaRadius();
            float arenaY = 1f;

            // --- 셰이더를 씬의 기존 오브젝트에서 캐시 (Shader.Find는 런타임에 실패함) ---
            CacheShaderFromScene();

            // --- 씬의 기존 아레나 오브젝트 처리 ---
            if (_arenaCenter != null)
            {
                if (_selectedArenaType == ArenaType.Classic)
                {
                    // Classic: 기존 원형 아레나 사용
                    _arenaCenter.gameObject.SetActive(true);
                    _arenaCenter.position = new Vector3(
                        _arenaCenter.position.x, arenaY, _arenaCenter.position.z);
                    FixArenaCollider(_arenaCenter.gameObject);
                    Debug.Log($"[ENV] 기존 아레나 활성화 → Y={arenaY}");
                }
                else
                {
                    // Square/Punching: 기존 원형 아레나 숨김 (자체 플랫폼 생성)
                    _arenaCenter.gameObject.SetActive(false);
                    Debug.Log($"[ENV] 기존 아레나 비활성화 (타입={_selectedArenaType})");
                }
            }

            // ★ CapsuleCollider → MeshCollider 교체 (Classic일 때만 필요)
            if (_selectedArenaType == ArenaType.Classic)
                FixAllArenaColliders();

            // --- 카메라: 스카이박스 렌더링 활성화 ---
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                Debug.Log("[ENV] 카메라 clearFlags → Skybox");
            }

            if (GameObject.Find("ENV_Root") != null)
            {
                Debug.Log("[ENV] ENV_Root 이미 존재 → 스킵");
                return;
            }

            _envRoot = new GameObject("ENV_Root");

            // ========== 1. 하늘 ==========
            BuildSkybox();

            // ========== 2. 초원 바닥 ==========
            BuildGround(_envRoot.transform, arenaRadius);

            // ========== 3. 아레나 플랫폼 ==========
            switch (_selectedArenaType)
            {
                case ArenaType.Square:
                    BuildSquareArena(_envRoot.transform, arenaY);
                    break;
                case ArenaType.Punching:
                    BuildPunchingArena(_envRoot.transform, arenaY);
                    break;
                default:
                    BuildArenaPlatform(_envRoot.transform, arenaRadius, arenaY);
                    BuildBoundary(_envRoot.transform, arenaRadius, arenaY);
                    break;
            }

            // ========== 4. 조명 보정 ==========
            BuildLighting();

            // ★ 카메라 오프셋 조정 (아레나 크기에 맞춤)
            _cameraOffset = _selectedArenaType switch
            {
                ArenaType.Punching => new Vector3(0f, 40f, -32f),  // 대형 아레나
                ArenaType.Square => new Vector3(0f, 30f, -24f),    // 사각형 아레나
                _ => new Vector3(0f, 28f, -22f)                     // 클래식
            };

            Debug.Log($"[ENV] 환경 생성 완료! 아레나: {_selectedArenaType}, 셰이더: {(_cachedLitShader != null ? _cachedLitShader.name : "NONE")}");
        }

        private float GetCurrentArenaRadius()
        {
            return _selectedArenaType switch
            {
                ArenaType.Punching => PUNCHING_ARENA_RADIUS,
                _ => _gameConfig != null ? _gameConfig.arenaRadius : 15f
            };
        }

        private void RebuildArena()
        {
            // 기존 환경 즉시 제거 (DestroyImmediate로 같은 프레임 내 완전 삭제)
            if (_envRoot != null)
                DestroyImmediate(_envRoot);
            GameObject oldEnv = GameObject.Find("ENV_Root");
            if (oldEnv != null)
                DestroyImmediate(oldEnv);

            // 펀칭머신 참조 초기화
            _punchingMachines = null;
            _punchPistons = null;
            _punchPositions = null;

            // 재생성
            BuildEnvironment();

            Debug.Log($"[ENV] 아레나 재생성: {_selectedArenaType}");
        }

        /// <summary>
        /// 씬 전체에서 아레나 관련 CapsuleCollider를 모두 찾아 MeshCollider로 교체.
        /// Unity Cylinder의 기본 CapsuleCollider는 돔 모양이라 차량이 미끄러짐!
        /// </summary>
        private void FixAllArenaColliders()
        {
            int fixedCount = 0;

            // 방법 1: 이름으로 찾기
            string[] arenaNames = { "Arena", "arena" };
            foreach (string aName in arenaNames)
            {
                GameObject found = GameObject.Find(aName);
                if (found != null && FixColliderOnObject(found))
                    fixedCount++;
            }

            // 방법 2: 씬의 모든 CapsuleCollider 검사 (큰 Cylinder = 아레나일 가능성)
            CapsuleCollider[] allCapsules = FindObjectsByType<CapsuleCollider>(FindObjectsSortMode.None);
            foreach (CapsuleCollider cap in allCapsules)
            {
                // 아레나 크기 (반지름 5 이상인 큰 Cylinder)
                float worldRadius = cap.radius * Mathf.Max(cap.transform.lossyScale.x, cap.transform.lossyScale.z);
                if (worldRadius >= 5f)
                {
                    if (FixColliderOnObject(cap.gameObject))
                        fixedCount++;
                }
            }

            Debug.Log($"[ENV] 아레나 콜라이더 수정 완료: {fixedCount}개 교체됨");
        }

        private void FixArenaCollider(GameObject arenaObj)
        {
            FixColliderOnObject(arenaObj);
            foreach (Transform child in arenaObj.transform)
            {
                if (child.GetComponent<CapsuleCollider>() != null)
                    FixColliderOnObject(child.gameObject);
            }
        }

        /// <summary>
        /// CapsuleCollider → MeshCollider로 교체. 성공 시 true 리턴.
        /// </summary>
        private bool FixColliderOnObject(GameObject obj)
        {
            CapsuleCollider capsule = obj.GetComponent<CapsuleCollider>();
            if (capsule == null) return false;

            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                // CapsuleCollider 즉시 비활성화 후 제거
                capsule.enabled = false;
                Destroy(capsule);

                // 이미 MeshCollider가 있으면 스킵
                if (obj.GetComponent<MeshCollider>() == null)
                {
                    MeshCollider meshCol = obj.AddComponent<MeshCollider>();
                    meshCol.sharedMesh = meshFilter.sharedMesh;
                    meshCol.convex = false;
                }

                Debug.Log($"[ENV] ★ {obj.name}: CapsuleCollider → MeshCollider 교체! (평평한 표면)");
                return true;
            }
            else
            {
                Debug.LogWarning($"[ENV] {obj.name}: MeshFilter 없음 → BoxCollider 폴백 추가");
                // 폴백: BoxCollider로라도 평평한 표면 만들기
                capsule.enabled = false;
                Destroy(capsule);
                if (obj.GetComponent<BoxCollider>() == null)
                {
                    BoxCollider box = obj.AddComponent<BoxCollider>();
                    box.center = Vector3.zero;
                    box.size = new Vector3(1f, 1f, 1f); // 스케일에 의해 자동 확대됨
                }
                return true;
            }
        }

        // ---- 기존 씬 오브젝트에서 셰이더 캐시 ----
        private void CacheShaderFromScene()
        {
            if (_cachedLitShader != null) return;

            // 씬에 이미 있는 Renderer에서 셰이더를 가져옴 (에디터가 만든 머테리얼은 유효함)
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (Renderer r in renderers)
            {
                if (r.sharedMaterial != null && r.sharedMaterial.shader != null)
                {
                    _cachedLitShader = r.sharedMaterial.shader;
                    Debug.Log($"[ENV] 셰이더 캐시 성공: {_cachedLitShader.name} (from {r.gameObject.name})");
                    return;
                }
            }

            // 폴백: Shader.Find 시도
            _cachedLitShader = Shader.Find("Universal Render Pipeline/Lit");
            if (_cachedLitShader == null) _cachedLitShader = Shader.Find("Standard");
            if (_cachedLitShader == null) _cachedLitShader = Shader.Find("Unlit/Color");

            Debug.LogWarning($"[ENV] 셰이더 폴백 사용: {(_cachedLitShader != null ? _cachedLitShader.name : "NULL - 오브젝트가 안 보일 수 있음!")}");
        }

        // ---- 하늘 (프리미엄) ----
        private void BuildSkybox()
        {
            // Procedural Skybox 시도
            Shader skyShader = Shader.Find("Skybox/Procedural");

            if (skyShader == null)
            {
                Debug.LogWarning("[ENV] Skybox/Procedural 셰이더 없음 → 색상 배경 사용");
                Camera cam = Camera.main;
                if (cam != null && RenderSettings.skybox == null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.35f, 0.55f, 0.85f);
                }
            }
            else
            {
                // ★ 프리미엄 하늘: 선명한 일몰/골든아워 느낌
                Material skyMat = new Material(skyShader);
                skyMat.SetFloat("_SunSize", 0.04f);
                skyMat.SetFloat("_SunSizeConvergence", 8f);
                skyMat.SetFloat("_AtmosphereThickness", 1.4f);
                skyMat.SetColor("_SkyTint", new Color(0.3f, 0.5f, 0.78f)); // 깊은 파랑
                skyMat.SetColor("_GroundColor", new Color(0.45f, 0.4f, 0.35f)); // 따뜻한 대지
                skyMat.SetFloat("_Exposure", 1.5f);
                RenderSettings.skybox = skyMat;
                Debug.Log("[ENV] 프리미엄 Procedural Skybox 생성 완료");
            }

            // ★ 프리미엄 환경광: 따뜻하고 풍부한 조명
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.78f, 0.95f);    // 밝은 하늘색
            RenderSettings.ambientEquatorColor = new Color(0.6f, 0.55f, 0.45f);  // 따뜻한 수평선
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.28f, 0.2f);   // 짙은 초록

            // ★ 프리미엄 안개: 깊이감과 분위기
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.65f, 0.75f, 0.88f);
            RenderSettings.fogDensity = 0.004f;
        }

        // ---- 초원 바닥 (프리미엄) ----
        private void BuildGround(Transform parent, float arenaRadius)
        {
            float groundSize = arenaRadius * 10f;

            // 메인 초원 (더 자연스러운 초록)
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground_Meadow";
            ground.transform.SetParent(parent);
            ground.transform.position = new Vector3(0f, -0.25f, 0f);
            ground.transform.localScale = new Vector3(groundSize, 0.5f, groundSize);
            ApplyColorSmooth(ground, new Color(0.3f, 0.52f, 0.22f), 0.05f);

            // 아레나 아래 진한 영역 (Square=사각형, 그 외=원형)
            bool useSquarePatch = _selectedArenaType == ArenaType.Square;
            GameObject darkPatch = GameObject.CreatePrimitive(
                useSquarePatch ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            darkPatch.name = "Ground_DarkPatch";
            darkPatch.transform.SetParent(parent);
            darkPatch.transform.position = new Vector3(0f, 0.01f, 0f);
            if (useSquarePatch)
            {
                float patchSize = SQUARE_ARENA_HALF_SIZE * 2.8f;
                darkPatch.transform.localScale = new Vector3(patchSize, 0.02f, patchSize);
            }
            else
            {
                darkPatch.transform.localScale = new Vector3(arenaRadius * 2.8f, 0.01f, arenaRadius * 2.8f);
            }
            ApplyColorSmooth(darkPatch, new Color(0.22f, 0.38f, 0.15f), 0.08f);
            Destroy(darkPatch.GetComponent<Collider>());

            // ★ 더 많은 언덕 (자연스러운 지형)
            CreateHill(parent, new Vector3(-70f, 0f, 80f), new Vector3(50f, 5f, 30f), new Color(0.28f, 0.5f, 0.2f));
            CreateHill(parent, new Vector3(80f, 0f, 60f), new Vector3(40f, 4f, 35f), new Color(0.3f, 0.53f, 0.22f));
            CreateHill(parent, new Vector3(60f, 0f, -75f), new Vector3(55f, 6f, 25f), new Color(0.26f, 0.48f, 0.18f));
            CreateHill(parent, new Vector3(-65f, 0f, -65f), new Vector3(35f, 4.5f, 40f), new Color(0.31f, 0.51f, 0.21f));
            CreateHill(parent, new Vector3(0f, 0f, 90f), new Vector3(45f, 3f, 35f), new Color(0.29f, 0.49f, 0.19f));
            CreateHill(parent, new Vector3(-90f, 0f, 0f), new Vector3(38f, 3.5f, 28f), new Color(0.32f, 0.54f, 0.24f));
            CreateHill(parent, new Vector3(85f, 0f, -20f), new Vector3(42f, 4f, 32f), new Color(0.27f, 0.47f, 0.19f));

            // ★ 더 많은 나무들 (더 풍성한 풍경)
            CreateSimpleTree(parent, new Vector3(-35f, 0f, 40f));
            CreateSimpleTree(parent, new Vector3(30f, 0f, 45f));
            CreateSimpleTree(parent, new Vector3(40f, 0f, -35f));
            CreateSimpleTree(parent, new Vector3(-40f, 0f, -30f));
            CreateSimpleTree(parent, new Vector3(-25f, 0f, -45f));
            CreateSimpleTree(parent, new Vector3(45f, 0f, 18f));
            CreateSimpleTree(parent, new Vector3(-50f, 0f, 55f));
            CreateSimpleTree(parent, new Vector3(55f, 0f, -50f));
            CreateSimpleTree(parent, new Vector3(-15f, 0f, 55f));
            CreateSimpleTree(parent, new Vector3(60f, 0f, 35f));

            // ★ 바위 장식
            CreateRock(parent, new Vector3(-45f, 0f, 20f), new Vector3(3f, 2f, 2.5f));
            CreateRock(parent, new Vector3(50f, 0f, -15f), new Vector3(2.5f, 1.5f, 3f));
            CreateRock(parent, new Vector3(-30f, 0f, -50f), new Vector3(4f, 2.5f, 3f));
            CreateRock(parent, new Vector3(35f, 0f, 50f), new Vector3(2f, 1.8f, 2.2f));

            // ★ 꽃/풀 장식 (작은 구체)
            for (int i = 0; i < 30; i++)
            {
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float dist = Random.Range(arenaRadius * 1.5f, arenaRadius * 4f);
                Vector3 pos = new Vector3(Mathf.Cos(angle) * dist, 0.15f, Mathf.Sin(angle) * dist);
                CreateFlower(parent, pos);
            }

            Debug.Log("[ENV] 프리미엄 초원 바닥 생성 완료");
        }

        private void CreateRock(Transform parent, Vector3 pos, Vector3 scale)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock";
            rock.transform.SetParent(parent);
            rock.transform.position = pos;
            rock.transform.localScale = scale;
            rock.transform.rotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-10f, 10f));
            float g = Random.Range(0.4f, 0.55f);
            ApplyColorSmooth(rock, new Color(g, g * 0.95f, g * 0.85f), 0.3f);
            Destroy(rock.GetComponent<Collider>());
        }

        private void CreateFlower(Transform parent, Vector3 pos)
        {
            GameObject flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flower.name = "Flower";
            flower.transform.SetParent(parent);
            flower.transform.position = pos;
            flower.transform.localScale = new Vector3(0.3f, 0.4f, 0.3f);
            Color[] flowerColors = {
                new Color(0.9f, 0.3f, 0.3f), new Color(0.9f, 0.8f, 0.2f),
                new Color(0.8f, 0.3f, 0.7f), new Color(1f, 0.6f, 0.2f),
                new Color(0.4f, 0.6f, 0.9f)
            };
            ApplyColor(flower, flowerColors[Random.Range(0, flowerColors.Length)]);
            Destroy(flower.GetComponent<Collider>());
        }

        private void CreateHill(Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            GameObject hill = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hill.name = "Hill";
            hill.transform.SetParent(parent);
            hill.transform.position = pos;
            hill.transform.localScale = scale;
            ApplyColor(hill, color);
            Destroy(hill.GetComponent<Collider>());
        }

        private void CreateSimpleTree(Transform parent, Vector3 pos)
        {
            GameObject tree = new GameObject("Tree");
            tree.transform.SetParent(parent);
            tree.transform.position = pos;

            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(tree.transform);
            trunk.transform.localPosition = new Vector3(0f, 2f, 0f);
            trunk.transform.localScale = new Vector3(0.6f, 2f, 0.6f);
            ApplyColor(trunk, new Color(0.45f, 0.3f, 0.15f));
            Destroy(trunk.GetComponent<Collider>());

            GameObject leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leaves.name = "Leaves";
            leaves.transform.SetParent(tree.transform);
            leaves.transform.localPosition = new Vector3(0f, 5f, 0f);
            leaves.transform.localScale = new Vector3(4f, 3.5f, 4f);
            ApplyColor(leaves, new Color(0.2f + Random.Range(0f, 0.1f), 0.5f + Random.Range(0f, 0.15f), 0.15f));
            Destroy(leaves.GetComponent<Collider>());
        }

        // ---- 아레나 플랫폼 (프리미엄) ----
        private void BuildArenaPlatform(Transform parent, float arenaRadius, float arenaY)
        {
            float surfaceY = arenaY + 0.5f;

            // ★ 프리미엄 받침: 다단 구조
            float pillarBottom = 0f;
            float pillarTop = arenaY - 0.5f;
            float pillarHeight = pillarTop - pillarBottom;

            if (pillarHeight > 0.1f)
            {
                // 메인 기둥 (어둡고 세련된 색)
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pillar.name = "Arena_Pillar";
                pillar.transform.SetParent(parent);
                pillar.transform.position = new Vector3(0f, (pillarBottom + pillarTop) / 2f, 0f);
                pillar.transform.localScale = new Vector3(arenaRadius * 1.7f, pillarHeight / 2f, arenaRadius * 1.7f);
                ApplyColorSmooth(pillar, new Color(0.35f, 0.33f, 0.3f), 0.4f);
                Destroy(pillar.GetComponent<Collider>());

                // 상단 장식 링
                GameObject topRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                topRing.name = "Arena_TopRing";
                topRing.transform.SetParent(parent);
                topRing.transform.position = new Vector3(0f, pillarTop + 0.05f, 0f);
                topRing.transform.localScale = new Vector3(arenaRadius * 1.85f, 0.05f, arenaRadius * 1.85f);
                ApplyColorSmooth(topRing, new Color(0.6f, 0.55f, 0.4f), 0.7f);
                Destroy(topRing.GetComponent<Collider>());
            }

            // ★ Z-fighting 방지: 레이어 간 0.02 이상 간격
            float layer0 = surfaceY + 0.02f;  // 외곽 링
            float layer1 = surfaceY + 0.04f;  // 경고 링
            float layer2 = surfaceY + 0.06f;  // 라인
            float layer3 = surfaceY + 0.08f;  // 센터 마크
            float layer4 = surfaceY + 0.10f;  // 이너 마크
            float layer5 = surfaceY + 0.12f;  // 스폰 마크

            // ★ 외곽 경고 링 (빨간/주황 그라데이션)
            GameObject outerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            outerRing.name = "Arena_OuterRing";
            outerRing.transform.SetParent(parent);
            outerRing.transform.position = new Vector3(0f, layer0, 0f);
            outerRing.transform.localScale = new Vector3(arenaRadius * 2f + 0.5f, 0.005f, arenaRadius * 2f + 0.5f);
            ApplyColorSmooth(outerRing, new Color(0.85f, 0.15f, 0.08f), 0.6f);
            Destroy(outerRing.GetComponent<Collider>());

            // ★ 경고 구역 링 (80% 반지름)
            GameObject dangerRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dangerRing.name = "Arena_DangerRing";
            dangerRing.transform.SetParent(parent);
            dangerRing.transform.position = new Vector3(0f, layer1, 0f);
            dangerRing.transform.localScale = new Vector3(arenaRadius * 1.6f + 0.2f, 0.004f, arenaRadius * 1.6f + 0.2f);
            ApplyColor(dangerRing, new Color(0.85f, 0.35f, 0.1f, 0.5f));
            Destroy(dangerRing.GetComponent<Collider>());

            // ★ 센터 마크 (이중 원) - 흰색 계열
            GameObject centerMark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            centerMark.name = "Arena_CenterMark";
            centerMark.transform.SetParent(parent);
            centerMark.transform.position = new Vector3(0f, layer3, 0f);
            centerMark.transform.localScale = new Vector3(4f, 0.005f, 4f);
            ApplyColorSmooth(centerMark, new Color(0.9f, 0.9f, 0.92f), 0.8f);
            Destroy(centerMark.GetComponent<Collider>());

            GameObject innerMark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            innerMark.name = "Arena_InnerMark";
            innerMark.transform.SetParent(parent);
            innerMark.transform.position = new Vector3(0f, layer4, 0f);
            innerMark.transform.localScale = new Vector3(2f, 0.005f, 2f);
            ApplyColorSmooth(innerMark, new Color(0.75f, 0.78f, 0.82f), 0.9f);
            Destroy(innerMark.GetComponent<Collider>());

            // ★ 센터 라인 (십자 + 대각선)
            float lineWidth = 0.12f;
            CreateArenaLine(parent, layer2, arenaRadius, 0f, lineWidth);   // X축
            CreateArenaLine(parent, layer2, arenaRadius, 90f, lineWidth);  // Z축
            CreateArenaLine(parent, layer2, arenaRadius * 0.6f, 45f, lineWidth * 0.7f);  // 대각선 1
            CreateArenaLine(parent, layer2, arenaRadius * 0.6f, -45f, lineWidth * 0.7f); // 대각선 2

            // ★ 스폰 존 마크
            float spawnDist = _gameConfig != null ? _gameConfig.spawnDistanceFromCenter : 10f;
            CreateSpawnMark(parent, new Vector3(spawnDist, layer5, 0f), P1_COLOR);
            CreateSpawnMark(parent, new Vector3(-spawnDist, layer5, 0f), P2_COLOR);

            Debug.Log($"[ENV] 프리미엄 아레나 플랫폼 생성 완료 (반지름={arenaRadius}m, 표면 Y={surfaceY})");
        }

        private void CreateArenaLine(Transform parent, float y, float length, float angle, float width)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = $"Arena_Line_{angle:F0}";
            line.transform.SetParent(parent);
            line.transform.position = new Vector3(0f, y, 0f);
            line.transform.localScale = new Vector3(length * 1.8f, 0.01f, width);
            line.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            ApplyColor(line, new Color(0.95f, 0.95f, 0.9f));
            Destroy(line.GetComponent<Collider>());
        }

        private void CreateSpawnMark(Transform parent, Vector3 pos, Color color)
        {
            GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            mark.name = "SpawnMark";
            mark.transform.SetParent(parent);
            mark.transform.position = pos;
            mark.transform.localScale = new Vector3(2.5f, 0.005f, 2.5f);
            Color faded = new Color(color.r, color.g, color.b, 0.5f);
            ApplyColor(mark, faded);
            Destroy(mark.GetComponent<Collider>());
        }

        // ---- 경계 장식 (프리미엄) ----
        private void BuildBoundary(Transform parent, float arenaRadius, float arenaY)
        {
            float surfaceY = arenaY + 0.5f;

            GameObject postsParent = new GameObject("BoundaryPosts");
            postsParent.transform.SetParent(parent);
            int postCount = 28; // 더 많은 포스트

            for (int i = 0; i < postCount; i++)
            {
                float angle = i * (360f / postCount) * Mathf.Deg2Rad;
                float r = arenaRadius + 0.8f;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * r, surfaceY, Mathf.Sin(angle) * r);

                // ★ 기둥 (더 세련된 모양)
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                post.name = $"Post_{i:D2}";
                post.transform.SetParent(postsParent.transform);
                post.transform.position = pos + Vector3.up * 0.5f;
                post.transform.localScale = new Vector3(0.2f, 0.7f, 0.2f);

                Color postColor = i % 2 == 0
                    ? new Color(0.85f, 0.12f, 0.08f)
                    : new Color(0.95f, 0.93f, 0.85f);
                ApplyColorSmooth(post, postColor, 0.5f);
                Destroy(post.GetComponent<Collider>());

                // ★ 상단 구체 (빛나는 효과)
                GameObject top = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                top.name = $"PostTop_{i:D2}";
                top.transform.SetParent(postsParent.transform);
                top.transform.position = pos + Vector3.up * 1.25f;
                top.transform.localScale = new Vector3(0.32f, 0.32f, 0.32f);

                Color topColor = i % 2 == 0
                    ? new Color(1f, 0.3f, 0.2f)
                    : new Color(1f, 0.95f, 0.8f);
                ApplyColorSmooth(top, topColor, 0.7f);
                Destroy(top.GetComponent<Collider>());

                // ★ 기반 원판
                GameObject baseDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseDisc.name = $"PostBase_{i:D2}";
                baseDisc.transform.SetParent(postsParent.transform);
                baseDisc.transform.position = pos + Vector3.up * 0.02f;
                baseDisc.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
                ApplyColorSmooth(baseDisc, new Color(0.4f, 0.38f, 0.35f), 0.6f);
                Destroy(baseDisc.GetComponent<Collider>());
            }

            // ★ 아레나 주변 장식 조명 (4개 코너에 큰 기둥)
            for (int i = 0; i < 4; i++)
            {
                float angle = (i * 90f + 45f) * Mathf.Deg2Rad;
                float r = arenaRadius + 3f;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * r, surfaceY, Mathf.Sin(angle) * r);

                // 대형 장식 기둥
                GameObject bigPost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bigPost.name = $"CornerPillar_{i}";
                bigPost.transform.SetParent(postsParent.transform);
                bigPost.transform.position = pos + Vector3.up * 1.5f;
                bigPost.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
                ApplyColorSmooth(bigPost, new Color(0.45f, 0.42f, 0.38f), 0.5f);
                Destroy(bigPost.GetComponent<Collider>());

                // 상단 빛나는 구체
                GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                beacon.name = $"Beacon_{i}";
                beacon.transform.SetParent(postsParent.transform);
                beacon.transform.position = pos + Vector3.up * 3.8f;
                beacon.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
                ApplyColorSmooth(beacon, new Color(1f, 0.9f, 0.5f), 0.9f);
                Destroy(beacon.GetComponent<Collider>());
            }

            Debug.Log("[ENV] 프리미엄 경계 포스트 생성 완료");
        }

        // ---- 사각형 아레나 (굴곡 바닥) ----
        private void BuildSquareArena(Transform parent, float arenaY)
        {
            float halfSize = SQUARE_ARENA_HALF_SIZE;
            float fullSize = halfSize * 2f;
            float surfaceY = arenaY + 0.5f;

            // ★ 받침 기둥 (사각)
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = "SquareArena_Pillar";
            pillar.transform.SetParent(parent);
            pillar.transform.position = new Vector3(0f, arenaY / 2f, 0f);
            pillar.transform.localScale = new Vector3(fullSize * 0.85f, arenaY, fullSize * 0.85f);
            ApplyColorSmooth(pillar, new Color(0.35f, 0.33f, 0.3f), 0.4f);
            Destroy(pillar.GetComponent<Collider>());

            // ★ 메인 플랫폼 (Cube)
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "SquareArena_Platform";
            platform.transform.SetParent(parent);
            platform.transform.position = new Vector3(0f, arenaY, 0f);
            platform.transform.localScale = new Vector3(fullSize, 1f, fullSize);
            ApplyColorSmooth(platform, new Color(0.5f, 0.48f, 0.44f), 0.3f);

            // ★ 굴곡 바닥: 소수의 큰 언덕만 배치
            Random.State savedState = Random.state;
            Random.InitState(42);

            // 가장자리 근처 큰 언덕 (4변에 각 1개 = 4개)
            float[][] edgeBumps = new float[][] {
                new float[] { 0f, halfSize * 0.7f },         // 북
                new float[] { 0f, -halfSize * 0.7f },        // 남
                new float[] { -halfSize * 0.7f, 0f },        // 서
                new float[] { halfSize * 0.7f, 0f }           // 동
            };
            for (int i = 0; i < 4; i++)
            {
                float bh = Random.Range(0.5f, 0.9f);
                GameObject ridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ridge.name = $"Ridge_{i}";
                ridge.transform.SetParent(parent);
                ridge.transform.position = new Vector3(edgeBumps[i][0], surfaceY + bh / 2f, edgeBumps[i][1]);
                ridge.transform.localScale = new Vector3(
                    Random.Range(6f, 10f), bh, Random.Range(6f, 10f));
                ridge.transform.rotation = Quaternion.Euler(
                    Random.Range(-5f, 5f), Random.Range(0f, 45f), Random.Range(-5f, 5f));
                float g = Random.Range(0.42f, 0.52f);
                ApplyColorSmooth(ridge, new Color(g, g * 0.92f, g * 0.82f), 0.3f);
            }

            // 산개 소형 범프 (4개만)
            for (int i = 0; i < 4; i++)
            {
                float bx = Random.Range(-halfSize * 0.5f, halfSize * 0.5f);
                float bz = Random.Range(-halfSize * 0.5f, halfSize * 0.5f);
                float bh = Random.Range(0.2f, 0.5f);

                GameObject bump = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bump.name = $"Bump_{i}";
                bump.transform.SetParent(parent);
                bump.transform.position = new Vector3(bx, surfaceY + bh / 2f, bz);
                bump.transform.localScale = new Vector3(
                    Random.Range(4f, 7f), bh, Random.Range(4f, 7f));
                bump.transform.rotation = Quaternion.Euler(
                    Random.Range(-5f, 5f), Random.Range(0f, 360f), Random.Range(-5f, 5f));
                float g = Random.Range(0.40f, 0.50f);
                ApplyColorSmooth(bump, new Color(g, g * 0.95f, g * 0.85f), 0.25f);
            }

            Random.state = savedState;

            // ★ 경고 테두리 (4변)
            Color edgeColor = new Color(0.85f, 0.15f, 0.08f);
            float edgeW = 1.5f;
            // 상 (Z+)
            CreateEdgeMark(parent, new Vector3(0f, surfaceY + 0.05f, halfSize - edgeW / 2f), new Vector3(fullSize, 0.02f, edgeW), edgeColor);
            // 하 (Z-)
            CreateEdgeMark(parent, new Vector3(0f, surfaceY + 0.05f, -halfSize + edgeW / 2f), new Vector3(fullSize, 0.02f, edgeW), edgeColor);
            // 좌 (X-)
            CreateEdgeMark(parent, new Vector3(-halfSize + edgeW / 2f, surfaceY + 0.05f, 0f), new Vector3(edgeW, 0.02f, fullSize), edgeColor);
            // 우 (X+)
            CreateEdgeMark(parent, new Vector3(halfSize - edgeW / 2f, surfaceY + 0.05f, 0f), new Vector3(edgeW, 0.02f, fullSize), edgeColor);

            // ★ 센터 마크
            GameObject centerMark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            centerMark.name = "SquareArena_Center";
            centerMark.transform.SetParent(parent);
            centerMark.transform.position = new Vector3(0f, surfaceY + 0.08f, 0f);
            centerMark.transform.localScale = new Vector3(3f, 0.02f, 3f);
            ApplyColorSmooth(centerMark, new Color(1f, 0.85f, 0.15f), 0.8f);
            Destroy(centerMark.GetComponent<Collider>());

            // ★ 스폰 마크
            float spawnDist = _gameConfig != null ? _gameConfig.spawnDistanceFromCenter : 10f;
            CreateSpawnMark(parent, new Vector3(spawnDist, surfaceY + 0.1f, 0f), P1_COLOR);
            CreateSpawnMark(parent, new Vector3(-spawnDist, surfaceY + 0.1f, 0f), P2_COLOR);

            Debug.Log($"[ENV] 스퀘어 아레나 생성 완료 (크기={fullSize}x{fullSize}, 범프 25개)");
        }

        private void CreateEdgeMark(Transform parent, Vector3 pos, Vector3 scale, Color color)
        {
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = "EdgeMark";
            edge.transform.SetParent(parent);
            edge.transform.position = pos;
            edge.transform.localScale = scale;
            ApplyColor(edge, color);
            Destroy(edge.GetComponent<Collider>());
        }

        // ---- 펀칭 아레나 (대형 + 펀칭머신) ----
        private void BuildPunchingArena(Transform parent, float arenaY)
        {
            float arenaRadius = PUNCHING_ARENA_RADIUS;
            float surfaceY = arenaY + 0.5f;

            // ★ 물리 바닥 (Cube = BoxCollider, 어떤 스케일에서도 안정적)
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "PunchArena_Floor";
            floor.transform.SetParent(parent);
            floor.transform.position = new Vector3(0f, arenaY, 0f);
            floor.transform.localScale = new Vector3(arenaRadius * 2.2f, 1f, arenaRadius * 2.2f);
            ApplyColorSmooth(floor, new Color(0.38f, 0.42f, 0.48f), 0.3f);  // 짙은 청회색
            Rigidbody floorRb = floor.AddComponent<Rigidbody>();
            floorRb.isKinematic = true;

            // ★ 장식 플랫폼 + 경계
            BuildArenaPlatform(parent, arenaRadius, arenaY);
            BuildBoundary(parent, arenaRadius, arenaY);

            // ★ 펀칭머신 4개 (N/S/E/W)
            _punchingMachines = new GameObject[4];
            _punchPistons = new GameObject[4];
            _punchPositions = new Vector3[4];
            _punchCooldowns = new float[4];
            _punchAnimTimers = new float[4];

            Vector3[] directions = {
                Vector3.forward,   // N (Z+)
                Vector3.back,      // S (Z-)
                Vector3.right,     // E (X+)
                Vector3.left       // W (X-)
            };

            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = directions[i];
                Vector3 basePos = dir * (arenaRadius - 3f);
                basePos.y = surfaceY;
                _punchPositions[i] = basePos;

                // 머신 본체 (큰 박스)
                GameObject machine = new GameObject($"PunchMachine_{i}");
                machine.transform.SetParent(parent);
                machine.transform.position = basePos;
                _punchingMachines[i] = machine;

                // 본체 박스
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.name = "Body";
                body.transform.SetParent(machine.transform);
                body.transform.localPosition = Vector3.up * 1.5f;
                body.transform.localScale = new Vector3(3f, 3f, 2f);
                ApplyColorSmooth(body, new Color(0.6f, 0.15f, 0.1f), 0.5f);
                Destroy(body.GetComponent<Collider>());

                // 경고 줄무늬
                GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = "Stripe";
                stripe.transform.SetParent(machine.transform);
                stripe.transform.localPosition = Vector3.up * 1.5f + (-dir) * 0.01f;
                stripe.transform.localScale = new Vector3(2.8f, 0.6f, 2.02f);
                ApplyColor(stripe, new Color(1f, 0.85f, 0.1f));
                Destroy(stripe.GetComponent<Collider>());

                // 피스톤 (밀어내는 부분)
                GameObject piston = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piston.name = "Piston";
                piston.transform.SetParent(machine.transform);
                piston.transform.localPosition = (-dir) * 1.2f + Vector3.up * 1f;
                piston.transform.localScale = new Vector3(2f, 1.5f, 1f);
                ApplyColorSmooth(piston, new Color(0.7f, 0.7f, 0.72f), 0.7f);
                Destroy(piston.GetComponent<Collider>());
                _punchPistons[i] = piston;

                // 바닥 경고 원
                GameObject warning = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                warning.name = "Warning";
                warning.transform.SetParent(machine.transform);
                warning.transform.position = basePos + Vector3.up * 0.02f;
                warning.transform.localScale = new Vector3(PUNCH_RANGE * 2f, 0.01f, PUNCH_RANGE * 2f);
                ApplyColor(warning, new Color(1f, 0.2f, 0.1f, 0.3f));
                Destroy(warning.GetComponent<Collider>());

                _punchCooldowns[i] = 0f;
                _punchAnimTimers[i] = 0f;
            }

            Debug.Log($"[ENV] 펀칭 아레나 생성 완료 (반지름={arenaRadius}m, 펀칭머신 4개)");
        }

        // ---- 조명 (프리미엄) ----
        private void BuildLighting()
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    // ★ 따뜻하고 풍부한 주광
                    light.intensity = 1.8f;
                    light.color = new Color(1f, 0.94f, 0.85f);
                    light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
                    light.shadows = LightShadows.Soft;
                    light.shadowStrength = 0.7f;
                }
            }

            // ★ 보조 조명 추가 (블루 필 라이트)
            GameObject fillLightObj = new GameObject("FillLight");
            Light fillLight = fillLightObj.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.4f;
            fillLight.color = new Color(0.7f, 0.8f, 1f); // 블루 톤
            fillLightObj.transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            fillLight.shadows = LightShadows.None;
        }

        // ---- 머테리얼: 기존 머테리얼 복제 후 색상만 변경 (Shader.Find 사용 안 함) ----
        private void ApplyColor(GameObject obj, Color color)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer == null) return;

            Material mat = new Material(renderer.sharedMaterial);
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.15f);
            renderer.material = mat;
        }

        /// <summary>
        /// 프리미엄 머테리얼 적용 (반사도 조절 가능)
        /// </summary>
        private void ApplyColorSmooth(GameObject obj, Color color, float smoothness)
        {
            Renderer renderer = obj.GetComponent<Renderer>();
            if (renderer == null) return;

            Material mat = new Material(renderer.sharedMaterial);
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", smoothness > 0.5f ? 0.3f : 0f);
            renderer.material = mat;
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmos()
        {
            float radius = _gameConfig != null ? _gameConfig.arenaRadius : 15f;
            Vector3 center = _arenaCenter != null ? _arenaCenter.position : Vector3.zero;

            // 아레나 범위
            Gizmos.color = Color.yellow;
            DrawCircle(center, radius, 64);

            // 위험 구역 (80%)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            DrawCircle(center, radius * 0.8f, 64);
        }

        private void DrawCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;
            Vector3 prev = center + new Vector3(radius, 0, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        #endregion
    }
}
