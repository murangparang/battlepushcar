using UnityEngine;
using System;
using System.Threading.Tasks;
using FishNet;
using FishNet.Managing;   // ← 이 줄 추가
using BattleCarSumo.Data;

namespace BattleCarSumo.Network
{
    public enum MatchType
    {
        None,
        Host,
        Client
    }
    /// <summary>
    /// Manages the matchmaking flow using join codes for simple P2P hosting.
    /// Handles hosting matches (creating relay) and joining matches (via join code).
    /// Integrates RelayManager for UGS Relay operations and Fish-Net networking.
    /// </summary>
    public class MatchmakingManager : MonoBehaviour
    {
        #region Events
        /// <summary>
        /// Fired when the local player successfully creates a match and obtains a join code.
        /// </summary>
        public event Action<string> OnMatchCreated;

        /// <summary>
        /// Fired when the local player successfully joins a match.
        /// </summary>
        public event Action OnMatchJoined;

        /// <summary>
        /// Fired when matchmaking fails for any reason.
        /// </summary>
        public event Action<string> OnMatchFailed;

        /// <summary>
        /// Fired when the player cancels matchmaking or disconnects.
        /// </summary>
        public event Action OnMatchCancelled;

        /// <summary>
        /// Fired when matchmaking status updates (for UI display).
        /// </summary>
        public event Action<string> OnStatusUpdated;
        #endregion

        #region Fields
        private RelayManager _relayManager;
        private NetworkManager _networkManager;
        private bool _isMatchmakingInProgress = false;
        private MatchType _currentMatchType;

        [SerializeField]
        private bool _autoInitializeUGS = true;



        
        #endregion

        #region Initialization
        private void Awake()
        {
            _relayManager = RelayManager.Instance;
            _networkManager = FindFirstObjectByType<NetworkManager>();

            if (_networkManager == null)
            {
                Debug.LogError("[MatchmakingManager] NetworkManager not found in scene");
            }

            // Subscribe to relay events
            if (_relayManager != null)
            {
                _relayManager.OnRelayCreated += HandleRelayCreated;
                _relayManager.OnRelayJoined += HandleRelayJoined;
                _relayManager.OnConnectionFailed += HandleConnectionFailed;
            }
        }

        private void OnDestroy()
        {
            if (_relayManager != null)
            {
                _relayManager.OnRelayCreated -= HandleRelayCreated;
                _relayManager.OnRelayJoined -= HandleRelayJoined;
                _relayManager.OnConnectionFailed -= HandleConnectionFailed;
            }
        }

        /// <summary>
        /// Initializes UGS if autoInitialize is enabled and not yet initialized.
        /// Should be called before hosting or joining.
        /// </summary>
        public async Task<bool> EnsureUGSInitialized()
        {
            if (_relayManager == null)
            {
                HandleConnectionFailed("RelayManager not available");
                return false;
            }

            if (_relayManager.IsInitialized)
            {
                return true;
            }

            if (_autoInitializeUGS)
            {
                OnStatusUpdated?.Invoke("Initializing services...");
                bool initSuccess = await _relayManager.InitializeUGS();

                if (!initSuccess)
                {
                    HandleConnectionFailed("Failed to initialize UGS");
                    return false;
                }

                return true;
            }

            return false;
        }
        #endregion

        #region Host Match
        /// <summary>
        /// Initiates matchmaking as a host.
        /// Creates a relay allocation, generates a join code, and starts Fish-Net as host.
        /// </summary>
        /// <param name="weightClass">The weight class the host player has selected.</param>
        public async void HostMatch(WeightClass weightClass)
        {
            if (_isMatchmakingInProgress)
            {
                OnMatchFailed?.Invoke("Matchmaking already in progress");
                return;
            }

            if (_networkManager == null)
            {
                OnMatchFailed?.Invoke("NetworkManager not found");
                return;
            }

            _isMatchmakingInProgress = true;
            _currentMatchType = MatchType.Host;
            OnStatusUpdated?.Invoke("Creating match...");

            try
            {
                // Ensure UGS is initialized
                bool ugsReady = await EnsureUGSInitialized();
                if (!ugsReady)
                {
                    _isMatchmakingInProgress = false;
                    return;
                }

                OnStatusUpdated?.Invoke("Setting up relay...");

                // Create relay allocation and get join code
                string joinCode = await _relayManager.CreateRelay();

                if (string.IsNullOrEmpty(joinCode))
                {
                    _isMatchmakingInProgress = false;
                    OnMatchFailed?.Invoke("Failed to generate join code");
                    return;
                }

                OnStatusUpdated?.Invoke("Starting network host...");

                // Start Fish-Net as host
                _relayManager.StartHost();

                OnStatusUpdated?.Invoke("Match created successfully");
                OnMatchCreated?.Invoke(joinCode);

                Debug.Log($"[MatchmakingManager] Match hosted with join code: {joinCode}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MatchmakingManager] Error hosting match: {ex.Message}");
                _isMatchmakingInProgress = false;
                OnMatchFailed?.Invoke($"Error hosting match: {ex.Message}");
            }
        }
        #endregion

        #region Join Match
        /// <summary>
        /// Initiates matchmaking as a client by joining via a join code.
        /// Joins a relay allocation and starts Fish-Net as client.
        /// </summary>
        /// <param name="joinCode">The join code provided by the host.</param>
        /// <param name="weightClass">The weight class the joining player has selected.</param>
        public async void JoinMatch(string joinCode, WeightClass weightClass)
        {
            if (_isMatchmakingInProgress)
            {
                OnMatchFailed?.Invoke("Matchmaking already in progress");
                return;
            }

            if (_networkManager == null)
            {
                OnMatchFailed?.Invoke("NetworkManager not found");
                return;
            }

            if (string.IsNullOrEmpty(joinCode))
            {
                OnMatchFailed?.Invoke("Invalid join code");
                return;
            }

            _isMatchmakingInProgress = true;
            _currentMatchType = MatchType.Client;
            OnStatusUpdated?.Invoke("Joining match...");

            try
            {
                // Ensure UGS is initialized
                bool ugsReady = await EnsureUGSInitialized();
                if (!ugsReady)
                {
                    _isMatchmakingInProgress = false;
                    return;
                }

                OnStatusUpdated?.Invoke("Contacting relay server...");

                // Join relay with the provided code
                bool joinSuccess = await _relayManager.JoinRelay(joinCode);

                if (!joinSuccess)
                {
                    _isMatchmakingInProgress = false;
                    OnMatchFailed?.Invoke("Failed to join relay");
                    return;
                }

                OnStatusUpdated?.Invoke("Starting network client...");

                // Start Fish-Net as client
                _relayManager.StartClient();

                OnStatusUpdated?.Invoke("Connected to match");
                OnMatchJoined?.Invoke();

                Debug.Log("[MatchmakingManager] Successfully joined match");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MatchmakingManager] Error joining match: {ex.Message}");
                _isMatchmakingInProgress = false;
                OnMatchFailed?.Invoke($"Error joining match: {ex.Message}");
            }
        }
        #endregion

        #region Matchmaking Control
        /// <summary>
        /// Cancels ongoing matchmaking and disconnects from the network.
        /// </summary>
        public void CancelMatchmaking()
        {
            if (!_isMatchmakingInProgress)
                return;

            try
            {
                _isMatchmakingInProgress = false;

                if (_relayManager != null && _relayManager.IsConnected)
                {
                    _relayManager.StopConnection();
                }

                OnStatusUpdated?.Invoke("Matchmaking cancelled");
                OnMatchCancelled?.Invoke();

                Debug.Log("[MatchmakingManager] Matchmaking cancelled and connection stopped");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MatchmakingManager] Error cancelling matchmaking: {ex.Message}");
            }
        }

        /// <summary>
        /// Disconnects from an active match.
        /// </summary>
        public void DisconnectFromMatch()
        {
            try
            {
                if (_relayManager != null && _relayManager.IsConnected)
                {
                    _relayManager.StopConnection();
                }

                _isMatchmakingInProgress = false;
                _currentMatchType = MatchType.None;

                OnStatusUpdated?.Invoke("Disconnected from match");
                OnMatchCancelled?.Invoke();

                Debug.Log("[MatchmakingManager] Disconnected from match");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MatchmakingManager] Error disconnecting: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if matchmaking is currently in progress.
        /// </summary>
        public bool IsMatchmakingInProgress() => _isMatchmakingInProgress;

        /// <summary>
        /// Gets the current match type (Host, Client, or None).
        /// </summary>
        public MatchType GetCurrentMatchType() => _currentMatchType;

        /// <summary>
        /// Checks if the local player is hosting.
        /// </summary>
        public bool IsHosting() => _currentMatchType == MatchType.Host;

        /// <summary>
        /// Checks if the local player is a client.
        /// </summary>
        public bool IsClient() => _currentMatchType == MatchType.Client;
        #endregion

        #region Event Handlers
        /// <summary>
        /// Internal event handler when relay is created.
        /// </summary>
        private void HandleRelayCreated(string joinCode)
        {
            Debug.Log($"[MatchmakingManager] Relay created with code: {joinCode}");
        }

        /// <summary>
        /// Internal event handler when relay is joined.
        /// </summary>
        private void HandleRelayJoined()
        {
            Debug.Log("[MatchmakingManager] Relay joined successfully");
        }

        /// <summary>
        /// Internal event handler when connection fails.
        /// </summary>
        private void HandleConnectionFailed(string reason)
        {
            Debug.LogError($"[MatchmakingManager] Connection failed: {reason}");
            _isMatchmakingInProgress = false;
            OnMatchFailed?.Invoke(reason);
        }
        #endregion

        #region Utility Methods
        /// <summary>
        /// Gets the current join code if hosting.
        /// </summary>
        public string GetCurrentJoinCode()
        {
            if (_currentMatchType == MatchType.Host && _relayManager != null)
            {
                return _relayManager.GetCurrentJoinCode();
            }
            return "";
        }

        /// <summary>
        /// Checks if connected to network.
        /// </summary>
        public bool IsConnected()
        {
            return _relayManager != null && _relayManager.IsConnected;
        }

        /// <summary>
        /// Gets connection status for UI display.
        /// </summary>
        public string GetConnectionStatus()
        {
            if (!_isMatchmakingInProgress && !IsConnected())
                return "Disconnected";

            if (_currentMatchType == MatchType.Host)
                return $"Hosting (Code: {GetCurrentJoinCode()})";

            if (_currentMatchType == MatchType.Client)
                return "Connected as Client";

            if (_isMatchmakingInProgress)
                return "Matchmaking in progress...";

            return "Unknown";
        }
        #endregion

        #region Debug
#if UNITY_EDITOR
        /// <summary>
        /// Debug method to simulate hosting (editor only).
        /// </summary>
        [ContextMenu("Debug/Host Match")]
        private void DebugHostMatch()
        {
            HostMatch(WeightClass.Middle);
        }

        /// <summary>
        /// Debug method to simulate joining with hardcoded code (editor only).
        /// </summary>
        [ContextMenu("Debug/Join Match (Code: TEST1234)")]
        private void DebugJoinMatch()
        {
            JoinMatch("TEST1234", WeightClass.Middle);
        }

        /// <summary>
        /// Debug method to cancel matchmaking (editor only).
        /// </summary>
        [ContextMenu("Debug/Cancel Matchmaking")]
        private void DebugCancelMatchmaking()
        {
            CancelMatchmaking();
        }
#endif
        #endregion
    }
}
