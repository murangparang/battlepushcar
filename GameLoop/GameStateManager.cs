// ============================================================
// GameStateManager.cs - 게임 상태 머신 (서버 권위적)
// Battle Car Sumo - 1v1 Server Authoritative Physics Battle
// ============================================================

using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine;
using BattleCarSumo.Data;

namespace BattleCarSumo.GameLoop
{
    /// <summary>
    /// 메인 게임 상태 머신. 서버에서 라운드 진행을 관리하고,
    /// Fish-Net SyncVar/RPC를 통해 클라이언트에 상태를 동기화합니다.
    ///
    /// 상태 흐름:
    /// WaitingForPlayers → Countdown → Playing → RoundEnd → Intermission → Countdown → ... → MatchEnd
    /// </summary>
    public class GameStateManager : NetworkBehaviour
    {
        #region Events (클라이언트 UI 바인딩용)

        /// <summary>게임 상태 변경 시 발생</summary>
        public event System.Action<GameState> OnStateChanged;

        /// <summary>라운드 종료 시 발생 (round, result, p1Score, p2Score)</summary>
        public event System.Action<int, RoundResult, int, int> OnRoundEnded;

        /// <summary>매치 종료 시 발생 (winnerPlayerIndex: 0 또는 1)</summary>
        public event System.Action<int> OnMatchEnded;

        /// <summary>타이머 업데이트 시 발생</summary>
        public event System.Action<float> OnTimerUpdated;

        #endregion

        #region Inspector References

        [SerializeField]
        private GameConfig _gameConfig;

        [SerializeField]
        private ArenaManager _arenaManager;

        #endregion

        public readonly SyncVar<GameState> _currentState = new SyncVar<GameState>();

        public readonly SyncVar<int> _currentRound = new SyncVar<int>();

        public readonly SyncVar<int> _player1Score = new SyncVar<int>();

        public readonly SyncVar<int> _player2Score = new SyncVar<int>();

        public readonly SyncVar<float> _stateTimer = new SyncVar<float>();

        #region Server-Only Fields

        private float _serverStateTimer = 0f;
        private float _timerUpdateInterval = 0.1f;
        private float _timeSinceLastUpdate = 0f;
        private RoundResult _currentRoundResult = RoundResult.None;
        private bool _roundEnded = false;

        #endregion

        #region Public Properties

        public GameState CurrentState => _currentState.Value;
        public int CurrentRound => _currentRound.Value;
        public int Player1Score => _player1Score.Value;
        public int Player2Score => _player2Score.Value;
        public float StateTimer => _stateTimer.Value;

        #endregion

        #region Fish-Net Lifecycle

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_gameConfig == null)
            {
                Debug.LogError("[GameStateManager] GameConfig이 할당되지 않았습니다!");
                return;
            }

            if (_arenaManager == null)
            {
                _arenaManager = FindFirstObjectByType<ArenaManager>();
                if (_arenaManager == null)
                {
                    Debug.LogError("[GameStateManager] ArenaManager를 찾을 수 없습니다!");
                    return;
                }
            }

            _currentState.Value = GameState.WaitingForPlayers;
            _serverStateTimer = 0f;

            Debug.Log("[GameStateManager] 서버 초기화 완료. 플레이어 대기 중...");
        }

        #endregion

        private void Awake()
        {
            _currentState.Value = GameState.WaitingForPlayers;
            _currentRound.Value = 1;
            _player1Score.Value = 0;
            _player2Score.Value = 0;
            _stateTimer.Value = 0f;

            _currentState.OnChange += OnCurrentStateChanged;
        }

        #region Update Loop (Server-Only)

        private void FixedUpdate()
        {
            if (!IsServerInitialized)
                return;

            // 타이머 갱신 (대기/매치 종료 상태 제외)
            if (_currentState.Value != GameState.WaitingForPlayers && _currentState.Value != GameState.MatchEnd)
            {
                _serverStateTimer -= Time.fixedDeltaTime;
                if (_serverStateTimer < 0f)
                    _serverStateTimer = 0f;

                // 주기적으로 클라이언트에 타이머 동기화
                _timeSinceLastUpdate += Time.fixedDeltaTime;
                if (_timeSinceLastUpdate >= _timerUpdateInterval)
                {
                    _stateTimer.Value = _serverStateTimer;
                    OnTimerUpdated?.Invoke(_stateTimer.Value);
                    _timeSinceLastUpdate = 0f;
                }
            }

            // 상태 전이 처리
            ProcessStateTransitions();

            // 경기 중이면 경기장 범위 체크
            if (_currentState.Value == GameState.Playing && _arenaManager != null)
            {
                _arenaManager.CheckBounds();
            }
        }

        /// <summary>
        /// 타이머 기반 자동 상태 전이 처리
        /// </summary>
        private void ProcessStateTransitions()
        {
            switch (_currentState.Value)
            {
                case GameState.Countdown:
                    if (_serverStateTimer <= 0f)
                        TransitionToPlaying();
                    break;

                case GameState.Playing:
                    if (_serverStateTimer <= 0f && !_roundEnded)
                        EndRound(RoundResult.TimeExpired);
                    break;

                case GameState.RoundEnd:
                    if (_serverStateTimer <= 0f)
                    {
                        if (HasMatchWinner())
                            EndMatch();
                        else
                            TransitionToIntermission();
                    }
                    break;

                case GameState.Intermission:
                    if (_serverStateTimer <= 0f)
                        StartNextRound();
                    break;
            }
        }

        #endregion

        #region Server Methods - 게임 흐름 제어

        /// <summary>
        /// 매치 시작. 양측 플레이어가 준비 완료 시 호출.
        /// </summary>
        [Server]
        public void StartMatch()
        {
            _player1Score.Value = 0;
            _player2Score.Value = 0;
            _currentRound.Value = 1;
            _roundEnded = false;

            if (_arenaManager != null)
                _arenaManager.ResetPlayerPositions();

            TransitionToCountdown();
            Debug.Log("[GameStateManager] 매치 시작!");
        }

        /// <summary>
        /// ArenaManager에서 플레이어가 경기장 밖으로 떨어졌을 때 호출.
        /// </summary>
        /// <param name="playerIndex">떨어진 플레이어 인덱스 (0 또는 1)</param>
        [Server]
        public void OnPlayerFellOff(int playerIndex)
        {
            if (_currentState.Value != GameState.Playing || _roundEnded)
                return;

            // 떨어진 플레이어의 상대방이 승리
            RoundResult result = playerIndex == 0 ? RoundResult.Player2Win : RoundResult.Player1Win;
            EndRound(result);

            Debug.Log($"[GameStateManager] 플레이어 {playerIndex + 1} 탈락! 라운드 승자: 플레이어 {(playerIndex == 0 ? 2 : 1)}");
        }

        /// <summary>
        /// 라운드 종료 처리 (점수 업데이트 및 상태 전이)
        /// </summary>
        [Server]
        public void EndRound(RoundResult result)
        {
            if (_roundEnded)
                return;

            _roundEnded = true;
            _currentRoundResult = result;

            // 점수 갱신
            if (result == RoundResult.Player1Win)
                _player1Score.Value++;
            else if (result == RoundResult.Player2Win)
                _player2Score.Value++;
            // TimeExpired/Draw = 무승부, 점수 변동 없음

            TransitionToRoundEnd();

            Debug.Log($"[GameStateManager] 라운드 {_currentRound.Value} 종료: {result}. 스코어: P1={_player1Score.Value} P2={_player2Score.Value}");
        }

        /// <summary>
        /// 다음 라운드 시작 (인터미션 후)
        /// </summary>
        [Server]
        public void StartNextRound()
        {
            _currentRound.Value++;
            _roundEnded = false;

            if (_arenaManager != null)
                _arenaManager.ResetPlayerPositions();

            TransitionToCountdown();

            Debug.Log($"[GameStateManager] 라운드 {_currentRound.Value} 시작 준비");
        }

        /// <summary>
        /// 매치 종료 (최종 승자 결정)
        /// </summary>
        [Server]
        public void EndMatch()
        {
            int winnerIndex = _player1Score.Value >= _gameConfig.roundsToWin ? 0 : 1;

            _currentState.Value = GameState.MatchEnd;
            _stateTimer.Value = 0f;

            NotifyMatchResultObserversRpc(winnerIndex);
            OnMatchEnded?.Invoke(winnerIndex);
            OnStateChanged?.Invoke(_currentState.Value);

            Debug.Log($"[GameStateManager] 매치 종료! 승자: 플레이어 {winnerIndex + 1}");
        }

        #endregion

        #region State Transitions

        private void TransitionToCountdown()
        {
            _currentState.Value = GameState.Countdown;
            _serverStateTimer = _gameConfig.CountdownDuration;
            _stateTimer.Value = _serverStateTimer;

            NotifyStateChangeObserversRpc(_currentState.Value, _stateTimer.Value);
            OnStateChanged?.Invoke(_currentState.Value);
        }

        private void TransitionToPlaying()
        {
            _currentState.Value = GameState.Playing;
            _serverStateTimer = _gameConfig.RoundDuration;
            _stateTimer.Value = _serverStateTimer;

            NotifyStateChangeObserversRpc(_currentState.Value, _stateTimer.Value);
            OnStateChanged?.Invoke(_currentState.Value);
        }

        private void TransitionToRoundEnd()
        {
            _currentState.Value = GameState.RoundEnd;
            _serverStateTimer = _gameConfig.RoundResultDisplayDuration;
            _stateTimer.Value = _serverStateTimer;

            NotifyStateChangeObserversRpc(_currentState.Value, _stateTimer.Value);
            NotifyRoundResultObserversRpc(_currentRound.Value, _currentRoundResult, _player1Score.Value, _player2Score.Value);
            OnRoundEnded?.Invoke(_currentRound.Value, _currentRoundResult, _player1Score.Value, _player2Score.Value);
        }

        private void TransitionToIntermission()
        {
            _currentState.Value = GameState.Intermission;
            _serverStateTimer = _gameConfig.IntermissionDuration;
            _stateTimer.Value = _serverStateTimer;

            NotifyStateChangeObserversRpc(_currentState.Value, _stateTimer.Value);
            OnStateChanged?.Invoke(_currentState.Value);
        }

        #endregion

        #region Utility

        /// <summary>
        /// 매치 승자가 결정되었는지 확인 (2승 이상)
        /// </summary>
        private bool HasMatchWinner()
        {
            return _player1Score.Value >= _gameConfig.roundsToWin || _player2Score.Value >= _gameConfig.roundsToWin;
        }

        /// <summary>
        /// SyncVar 콜백 - 클라이언트에서 상태 변경 감지
        /// </summary>
        private void OnCurrentStateChanged(GameState prev, GameState next, bool asServer)
        {
            if (!asServer)
            {
                OnStateChanged?.Invoke(next);
            }
        }

        #endregion

        #region RPCs

        /// <summary>
        /// 모든 클라이언트에 상태 변경 알림
        /// </summary>
        [ObserversRpc(BufferLast = true)]
        private void NotifyStateChangeObserversRpc(GameState newState, float timer)
        {
            // 클라이언트에서 UI 업데이트 등에 활용
            Debug.Log($"[GameStateManager] 상태 변경: {newState}, 타이머: {timer:F1}s");
        }

        /// <summary>
        /// 모든 클라이언트에 라운드 결과 알림
        /// </summary>
        [ObserversRpc]
        private void NotifyRoundResultObserversRpc(int round, RoundResult result, int p1Score, int p2Score)
        {
            OnRoundEnded?.Invoke(round, result, p1Score, p2Score);
            Debug.Log($"[GameStateManager] 라운드 {round} 결과: {result}. P1={p1Score} P2={p2Score}");
        }

        /// <summary>
        /// 모든 클라이언트에 매치 결과 알림
        /// </summary>
        [ObserversRpc]
        private void NotifyMatchResultObserversRpc(int winnerPlayerIndex)
        {
            OnMatchEnded?.Invoke(winnerPlayerIndex);
            Debug.Log($"[GameStateManager] 매치 결과: 플레이어 {winnerPlayerIndex + 1} 승리!");
        }

        #endregion
    }
}
