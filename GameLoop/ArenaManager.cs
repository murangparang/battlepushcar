using FishNet.Object;
using UnityEngine;
using BattleCarSumo.Data;
using BattleCarSumo.Vehicle;

namespace BattleCarSumo.GameLoop
{
    /// <summary>
    /// Manages the circular arena bounds and player safety checks.
    /// Detects when players fall off and notifies the GameStateManager.
    /// </summary>
    public class ArenaManager : NetworkBehaviour
    {
        /// <summary>
        /// Raised when a player falls off the arena
        /// </summary>
        public event System.Action<int> OnPlayerFallOff;

        [SerializeField]
        private GameConfig _gameConfig;

        [SerializeField]
        private Transform _arenaCenter;

        [SerializeField]
        private float _fallThreshold = -2f;

        [SerializeField]
        private float _edgeWarningThreshold = 0.85f;

        private GameStateManager _gameStateManager;

        // Player vehicle references
        private ServerVehicleController[] _playerVehicles = new ServerVehicleController[2];
        private bool[] _playerRegistered = new bool[2];
        private bool[] _edgeWarningShown = new bool[2];

        private float _arenaRadius;
        private float _spawnDistance;

        public float ArenaRadius => _arenaRadius;
        public Transform ArenaCenter => _arenaCenter;

        protected override void OnValidate()
        {
            if (_gameConfig == null)
            {
                _gameConfig = Resources.Load<GameConfig>("GameConfig");
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_gameConfig == null)
            {
                Debug.LogError("[ArenaManager] GameConfig is not assigned!");
                return;
            }

            if (_arenaCenter == null)
            {
                _arenaCenter = transform;
            }

            _arenaRadius = _gameConfig.ArenaRadius;
            _spawnDistance = _gameConfig.SpawnDistanceFromCenter;

            _playerVehicles = new ServerVehicleController[2];
            _playerRegistered = new bool[2];
            _edgeWarningShown = new bool[2];

            Debug.Log($"[ArenaManager] Initialized with radius {_arenaRadius} and spawn distance {_spawnDistance}");
        }

        /// <summary>
        /// Register a player vehicle with the arena manager
        /// </summary>
        [Server]
        public void RegisterPlayer(int playerIndex, ServerVehicleController vehicle)
        {
            if (!IsServerInitialized)
                return;

            if (playerIndex < 0 || playerIndex > 1)
            {
                Debug.LogWarning($"[ArenaManager] Invalid player index: {playerIndex}");
                return;
            }

            _playerVehicles[playerIndex] = vehicle;
            _playerRegistered[playerIndex] = true;
            _edgeWarningShown[playerIndex] = false;

            Debug.Log($"[ArenaManager] Player {playerIndex} vehicle registered");
        }

        /// <summary>
        /// Check arena bounds and detect players falling off
        /// Called during Playing state only
        /// </summary>
        [Server]
        public void CheckBounds()
        {
            if (!IsServerInitialized)
                return;

            for (int i = 0; i < 2; i++)
            {
                if (!_playerRegistered[i] || _playerVehicles[i] == null)
                    continue;

                Transform vehicleTransform = _playerVehicles[i].transform;
                Vector3 relativePos = vehicleTransform.position - _arenaCenter.position;

                // Check if below fall threshold (fell through arena)
                if (vehicleTransform.position.y < _fallThreshold)
                {
                    OnPlayerFallOffArena(i);
                    continue;
                }

                // Check if outside arena radius
                float horizontalDistance = new Vector2(relativePos.x, relativePos.z).magnitude;

                if (horizontalDistance >= _arenaRadius)
                {
                    OnPlayerFallOffArena(i);
                    continue;
                }

                // Check for edge warning
                float distanceRatio = horizontalDistance / _arenaRadius;
                if (distanceRatio >= _edgeWarningThreshold && !_edgeWarningShown[i])
                {
                    _edgeWarningShown[i] = true;
                    ShowEdgeWarning(i, distanceRatio);
                }
                else if (distanceRatio < _edgeWarningThreshold && _edgeWarningShown[i])
                {
                    _edgeWarningShown[i] = false;
                }
            }
        }

        /// <summary>
        /// Handle a player falling off the arena
        /// </summary>
        private void OnPlayerFallOffArena(int playerIndex)
        {
            Debug.Log($"[ArenaManager] Player {playerIndex} fell off the arena!");

            OnPlayerFallOff?.Invoke(playerIndex);

            if (_gameStateManager != null)
                _gameStateManager.OnPlayerFellOff(playerIndex);
            else
                Debug.LogWarning("[ArenaManager] GameStateManager가 설정되지 않았습니다!");
        }

        /// <summary>
        /// Reset both player positions to spawn points at the start of each round
        /// </summary>
        [Server]
        public void ResetPlayerPositions()
        {
            if (!IsServerInitialized)
                return;

            for (int i = 0; i < 2; i++)
            {
                if (!_playerRegistered[i] || _playerVehicles[i] == null)
                    continue;

                Transform vehicleTransform = _playerVehicles[i].transform;
                Rigidbody vehicleRigidbody = _playerVehicles[i].GetComponent<Rigidbody>();

                // Player 1: spawn at positive Z, facing center
                // Player 2: spawn at negative Z, facing center
                float zOffset = i == 0 ? _spawnDistance : -_spawnDistance;

                vehicleTransform.position = _arenaCenter.position + Vector3.forward * zOffset;
                vehicleTransform.rotation = i == 0
                    ? Quaternion.LookRotation(Vector3.back)  // Facing down (negative Z)
                    : Quaternion.LookRotation(Vector3.forward); // Facing up (positive Z)

                // Reset velocity
                if (vehicleRigidbody != null)
                {
                    vehicleRigidbody.linearVelocity = Vector3.zero;
                    vehicleRigidbody.angularVelocity = Vector3.zero;
                }

                _edgeWarningShown[i] = false;

                Debug.Log($"[ArenaManager] Reset Player {i} position");
            }
        }

        /// <summary>
        /// Notify clients of edge warning visual effect
        /// </summary>
        [ObserversRpc]
        private void ShowEdgeWarning(int playerIndex, float distanceRatio)
        {
            if (IsServerInitialized)
                return; // Server doesn't need visual notification

            Debug.Log($"[ArenaManager] Showing edge warning for Player {playerIndex} (distance ratio: {distanceRatio:F2})");
            // Clients will show visual/UI warning based on this notification
        }

        /// <summary>
        /// Set the reference to GameStateManager (called by GameStateManager)
        /// </summary>
        public void SetGameStateManager(GameStateManager gameStateManager)
        {
            _gameStateManager = gameStateManager;
        }

        /// <summary>
        /// Draw arena bounds in editor for debugging
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (_gameConfig == null)
                return;

            Transform center = _arenaCenter != null ? _arenaCenter : transform;
            float radius = _gameConfig.ArenaRadius;

            // Draw arena circle
            Gizmos.color = Color.green;
            DrawCircle(center.position, radius, 32);

            // Draw spawn points
            Gizmos.color = Color.blue;
            float spawnDist = _gameConfig.SpawnDistanceFromCenter;
            Gizmos.DrawSphere(center.position + Vector3.forward * spawnDist, 0.5f);
            Gizmos.DrawSphere(center.position - Vector3.forward * spawnDist, 0.5f);

            // Draw fall threshold
            Gizmos.color = Color.red;
            DrawCircle(center.position + Vector3.up * _fallThreshold, radius, 16);
        }

        /// <summary>
        /// Helper method to draw a circle in the editor
        /// </summary>
        private void DrawCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;
            Vector3 prevPoint = center + new Vector3(radius, 0, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 newPoint = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prevPoint, newPoint);
                prevPoint = newPoint;
            }
        }
    }
}
