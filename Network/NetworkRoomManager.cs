using UnityEngine;
using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using BattleCarSumo.Data;

namespace BattleCarSumo.Network
{
    /// <summary>
    /// Network behaviour that manages the room/session state for 1v1 matches.
    /// Handles player connections, ready states, and weight class selections.
    /// Uses Fish-Net's SyncVar and RPC system for networked state synchronization.
    /// </summary>
    public class NetworkRoomManager : NetworkBehaviour
    {
        #region Events
        /// <summary>
        /// Fired when a player joins the room.
        /// </summary>
        public event Action<int> OnPlayerJoined;

        /// <summary>
        /// Fired when a player leaves the room.
        /// </summary>
        public event Action<int> OnPlayerLeft;

        /// <summary>
        /// Fired when both players are ready and game is about to start.
        /// </summary>
        public event Action<WeightClass, WeightClass> OnGameStarting;

        /// <summary>
        /// Fired when a player marks themselves as ready.
        /// </summary>
        public event Action<int, bool> OnPlayerReadyChanged;

        /// <summary>
        /// Fired when game state changes (waiting, counting down, playing).
        /// </summary>
        public event Action<GameRoomState> OnRoomStateChanged;
        #endregion

        #region Nested Types
        /// <summary>
        /// Represents the current state of the match room.
        /// </summary>
        public enum GameRoomState
        {
            Waiting,
            CountingDown,
            Playing,
            Completed
        }

        /// <summary>
        /// Data structure for player information synchronized across network.
        /// </summary>
        [System.Serializable]
        public struct PlayerInfo
        {
            public int PlayerId;
            public string PlayerName;
            public bool IsReady;
            public WeightClass SelectedClass;
        }
        #endregion

        public readonly SyncVar<int> _playerCount = new SyncVar<int>();

        public readonly SyncVar<GameRoomState> _roomState = new SyncVar<GameRoomState>();

        public readonly SyncVar<int> _player1Id = new SyncVar<int>();

        public readonly SyncVar<string> _player1Name = new SyncVar<string>();

        public readonly SyncVar<bool> _player1Ready = new SyncVar<bool>();

        public readonly SyncVar<byte> _player1WeightClassValue = new SyncVar<byte>();

        public readonly SyncVar<int> _player2Id = new SyncVar<int>();

        public readonly SyncVar<string> _player2Name = new SyncVar<string>();

        public readonly SyncVar<bool> _player2Ready = new SyncVar<bool>();

        public readonly SyncVar<byte> _player2WeightClassValue = new SyncVar<byte>();

        #region Fields
        private const int MAX_PLAYERS = 2;
        private const float COUNTDOWN_DURATION = 3f;
        private float _countdownTimer = 0f;
        #endregion

        #region Initialization
        private void Awake()
        {
            _playerCount.Value = 0;
            _roomState.Value = GameRoomState.Waiting;
            _player1Id.Value = -1;
            _player1Name.Value = "";
            _player1Ready.Value = false;
            _player1WeightClassValue.Value = 0;
            _player2Id.Value = -1;
            _player2Name.Value = "";
            _player2Ready.Value = false;
            _player2WeightClassValue.Value = 0;

            _playerCount.OnChange += OnPlayerCountChanged;
            _roomState.OnChange += OnRoomStateChangedSync;
            _player1Ready.OnChange += OnPlayer1ReadyChanged;
            _player2Ready.OnChange += OnPlayer2ReadyChanged;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            Debug.Log("[NetworkRoomManager] Network started");
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            Debug.Log("[NetworkRoomManager] Network stopped");
        }
        #endregion

        #region Player Management
        /// <summary>
        /// Called when a player joins the session. Updates player list and notifies others.
        /// </summary>
        private void OnPlayerConnected()
        {
            if (!IsServerInitialized)
                return;

            _playerCount.Value++;
            Debug.Log($"[NetworkRoomManager] Player connected. Total players: {_playerCount.Value}");
            OnPlayerJoined?.Invoke(_playerCount.Value);

            // Register the connecting player
            if (_player1Id.Value == -1)
            {
                _player1Id.Value = (int)LocalConnection.ClientId;
                _player1Name.Value = GetPlayerName();
            }
            else if (_player2Id.Value == -1)
            {
                _player2Id.Value = (int)LocalConnection.ClientId;
                _player2Name.Value = GetPlayerName();
            }

            if (_playerCount.Value >= MAX_PLAYERS)
            {
                SetRoomState(GameRoomState.Waiting);
            }
        }

        /// <summary>
        /// Called when a player disconnects from the session.
        /// </summary>
        private void OnPlayerDisconnected()
        {
            if (!IsServerInitialized)
                return;

            _playerCount.Value--;
            Debug.Log($"[NetworkRoomManager] Player disconnected. Total players: {_playerCount.Value}");
            OnPlayerJoined?.Invoke(_playerCount.Value);

            // Notify all clients of disconnection
            NotifyPlayerDisconnected();

            // Reset room state if player count drops below 2
            if (_playerCount.Value < MAX_PLAYERS)
            {
                ResetRoomState();
            }
        }

        /// <summary>
        /// Gets the current player count.
        /// </summary>
        public int GetPlayerCount() => _playerCount.Value;

        /// <summary>
        /// Checks if room is full.
        /// </summary>
        public bool IsRoomFull() => _playerCount.Value >= MAX_PLAYERS;

        /// <summary>
        /// Gets player info for a specific player ID.
        /// </summary>
        public PlayerInfo GetPlayerInfo(int playerId)
        {
            if (playerId == _player1Id.Value)
            {
                return new PlayerInfo
                {
                    PlayerId = _player1Id.Value,
                    PlayerName = _player1Name.Value,
                    IsReady = _player1Ready.Value,
                    SelectedClass = (WeightClass)_player1WeightClassValue.Value
                };
            }
            else if (playerId == _player2Id.Value)
            {
                return new PlayerInfo
                {
                    PlayerId = _player2Id.Value,
                    PlayerName = _player2Name.Value,
                    IsReady = _player2Ready.Value,
                    SelectedClass = (WeightClass)_player2WeightClassValue.Value
                };
            }

            return default;
        }

        /// <summary>
        /// Gets the player name. Override this to use custom naming logic.
        /// </summary>
        private string GetPlayerName()
        {
            return $"Player{UnityEngine.Random.Range(1000, 9999)}";
        }
        #endregion

        #region Ready State Management
        /// <summary>
        /// ServerRpc called when a player marks themselves as ready with a chosen weight class.
        /// Must be called from the player's client.
        /// </summary>
        /// <param name="selectedClass">The weight class the player selected for their car.</param>
        [ServerRpc(RequireOwnership = true)]
        public void SetPlayerReady(WeightClass selectedClass)
        {
            if (!IsServerInitialized)
                return;

            int clientId = (int)LocalConnection.ClientId;

            // Update appropriate player's ready state
            if (clientId == _player1Id.Value)
            {
                _player1Ready.Value = true;
                _player1WeightClassValue.Value = (byte)selectedClass;
                Debug.Log($"[NetworkRoomManager] Player 1 ready with class: {selectedClass}");
            }
            else if (clientId == _player2Id.Value)
            {
                _player2Ready.Value = true;
                _player2WeightClassValue.Value = (byte)selectedClass;
                Debug.Log($"[NetworkRoomManager] Player 2 ready with class: {selectedClass}");
            }

            CheckBothReady();
        }

        /// <summary>
        /// ServerRpc to reset a player's ready state.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        public void UnsetPlayerReady()
        {
            if (!IsServerInitialized)
                return;

            int clientId = (int)LocalConnection.ClientId;

            if (clientId == _player1Id.Value)
            {
                _player1Ready.Value = false;
                Debug.Log("[NetworkRoomManager] Player 1 no longer ready");
            }
            else if (clientId == _player2Id.Value)
            {
                _player2Ready.Value = false;
                Debug.Log("[NetworkRoomManager] Player 2 no longer ready");
            }
        }

        /// <summary>
        /// Checks if both players are ready. If so, starts the game countdown.
        /// </summary>
        [Server]
        private void CheckBothReady()
        {
            if (_player1Ready.Value && _player2Ready.Value && _playerCount.Value >= MAX_PLAYERS)
            {
                Debug.Log("[NetworkRoomManager] Both players ready! Starting countdown...");
                SetRoomState(GameRoomState.CountingDown);
                _countdownTimer = COUNTDOWN_DURATION;

                // Notify all clients that both players are ready
                WeightClass player1Class = (WeightClass)_player1WeightClassValue.Value;
                WeightClass player2Class = (WeightClass)_player2WeightClassValue.Value;
                NotifyGameStart(player1Class, player2Class);
            }
        }

        /// <summary>
        /// Gets the ready state of a player.
        /// </summary>
        public bool IsPlayerReady(int playerId)
        {
            if (playerId == _player1Id.Value)
                return _player1Ready.Value;
            else if (playerId == _player2Id.Value)
                return _player2Ready.Value;

            return false;
        }

        /// <summary>
        /// Gets both players' ready states.
        /// </summary>
        public bool GetBothPlayersReady() => _player1Ready.Value && _player2Ready.Value;
        #endregion

        #region Room State
        /// <summary>
        /// Sets the room state on the server.
        /// </summary>
        [Server]
        private void SetRoomState(GameRoomState newState)
        {
            if (_roomState.Value == newState)
                return;

            _roomState.Value = newState;
            Debug.Log($"[NetworkRoomManager] Room state changed to: {newState}");
        }

        /// <summary>
        /// Gets the current room state.
        /// </summary>
        public GameRoomState GetRoomState() => _roomState.Value;

        /// <summary>
        /// Resets the room to initial state when players disconnect.
        /// </summary>
        [Server]
        private void ResetRoomState()
        {
            _player1Ready.Value = false;
            _player2Ready.Value = false;
            _countdownTimer = 0f;
            SetRoomState(GameRoomState.Waiting);

            if (_playerCount.Value == 0)
            {
                _player1Id.Value = -1;
                _player1Name.Value = "";
                _player1WeightClassValue.Value = 0;
                _player2Id.Value = -1;
                _player2Name.Value = "";
                _player2WeightClassValue.Value = 0;
            }

            Debug.Log("[NetworkRoomManager] Room reset");
        }
        #endregion

        #region Update Logic
        private void Update()
        {
            if (!IsServerInitialized)
                return;

            // Handle countdown
            if (_roomState.Value == GameRoomState.CountingDown)
            {
                _countdownTimer -= Time.deltaTime;

                if (_countdownTimer <= 0f)
                {
                    SetRoomState(GameRoomState.Playing);
                    NotifyGameStateChanged(GameRoomState.Playing);
                    Debug.Log("[NetworkRoomManager] Game started!");
                }
            }
        }
        #endregion

        #region Network RPCs
        /// <summary>
        /// ObserversRpc to notify all clients that the game is starting.
        /// </summary>
        /// <param name="player1Class">Weight class selected by player 1.</param>
        /// <param name="player2Class">Weight class selected by player 2.</param>
        [ObserversRpc]
        private void NotifyGameStart(WeightClass player1Class, WeightClass player2Class)
        {
            OnGameStarting?.Invoke(player1Class, player2Class);
            Debug.Log($"[NetworkRoomManager] Game starting with classes: P1={player1Class}, P2={player2Class}");
        }

        /// <summary>
        /// ObserversRpc to notify all clients of a player disconnection.
        /// </summary>
        [ObserversRpc]
        private void NotifyPlayerDisconnected()
        {
            OnPlayerLeft?.Invoke(_playerCount.Value);
            Debug.Log("[NetworkRoomManager] Player disconnected notification sent");
        }

        /// <summary>
        /// ObserversRpc to notify all clients of game state changes.
        /// </summary>
        [ObserversRpc]
        private void NotifyGameStateChanged(GameRoomState newState)
        {
            OnRoomStateChanged?.Invoke(newState);
        }
        #endregion

        #region SyncVar Callbacks
        /// <summary>
        /// Called when player count changes.
        /// </summary>
        private void OnPlayerCountChanged(int oldValue, int newValue, bool asServer)
        {
            Debug.Log($"[NetworkRoomManager] Player count: {oldValue} -> {newValue}");
        }

        /// <summary>
        /// Called when room state changes.
        /// </summary>
        private void OnRoomStateChangedSync(GameRoomState oldValue, GameRoomState newValue, bool asServer)
        {
            OnRoomStateChanged?.Invoke(newValue);
            Debug.Log($"[NetworkRoomManager] Room state changed: {oldValue} -> {newValue}");
        }

        /// <summary>
        /// Called when player 1's ready state changes.
        /// </summary>
        private void OnPlayer1ReadyChanged(bool oldValue, bool newValue, bool asServer)
        {
            OnPlayerReadyChanged?.Invoke(_player1Id.Value, newValue);
            Debug.Log($"[NetworkRoomManager] Player 1 ready: {newValue}");
        }

        /// <summary>
        /// Called when player 2's ready state changes.
        /// </summary>
        private void OnPlayer2ReadyChanged(bool oldValue, bool newValue, bool asServer)
        {
            OnPlayerReadyChanged?.Invoke(_player2Id.Value, newValue);
            Debug.Log($"[NetworkRoomManager] Player 2 ready: {newValue}");
        }
        #endregion

        #region Accessors
        /// <summary>
        /// Gets information about player 1.
        /// </summary>
        public PlayerInfo GetPlayer1Info() => new PlayerInfo
        {
            PlayerId = _player1Id.Value,
            PlayerName = _player1Name.Value,
            IsReady = _player1Ready.Value,
            SelectedClass = (WeightClass)_player1WeightClassValue.Value
        };

        /// <summary>
        /// Gets information about player 2.
        /// </summary>
        public PlayerInfo GetPlayer2Info() => new PlayerInfo
        {
            PlayerId = _player2Id.Value,
            PlayerName = _player2Name.Value,
            IsReady = _player2Ready.Value,
            SelectedClass = (WeightClass)_player2WeightClassValue.Value
        };

        /// <summary>
        /// Gets the ID of the opposing player.
        /// </summary>
        public int GetOpponentId(int playerId)
        {
            if (playerId == _player1Id.Value)
                return _player2Id.Value;
            else if (playerId == _player2Id.Value)
                return _player1Id.Value;

            return -1;
        }

        /// <summary>
        /// Checks if a player ID is in the room.
        /// </summary>
        public bool IsPlayerInRoom(int playerId) => playerId == _player1Id.Value || playerId == _player2Id.Value;

        /// <summary>
        /// Gets the countdown timer value (when in countdown state).
        /// </summary>
        public float GetCountdownTimer() => _countdownTimer;
        #endregion
    }
}
