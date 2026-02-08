using FishNet.Object;
using FishNet.Connection;
using FishNet.Transporting;
using UnityEngine;
using System.Collections.Generic;
using BattleCarSumo.Data;
using BattleCarSumo.Vehicle;
using BattleCarSumo.Parts;

namespace BattleCarSumo.GameLoop
{
    /// <summary>
    /// Spawns and manages player vehicle instances on the network.
    /// Handles vehicle creation, configuration, and cleanup.
    /// </summary>
    public class PlayerSpawner : NetworkBehaviour
    {
        [SerializeField]
        private GameObject _vehiclePrefab;

        [SerializeField]
        private Transform[] _spawnPoints = new Transform[2];

        [SerializeField]
        private GameConfig _gameConfig;

        [SerializeField]
        private ArenaManager _arenaManager;

        // Dictionary to track spawned vehicles by connection
        private Dictionary<NetworkConnection, NetworkObject> _spawnedVehicles = new Dictionary<NetworkConnection, NetworkObject>();

        // Track player indices
        private Dictionary<NetworkConnection, int> _playerIndices = new Dictionary<NetworkConnection, int>();
        private int _nextPlayerIndex = 0;

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

            if (_vehiclePrefab == null)
            {
                Debug.LogError("[PlayerSpawner] Vehicle prefab is not assigned!");
                return;
            }

            if (_spawnPoints.Length != 2 || _spawnPoints[0] == null || _spawnPoints[1] == null)
            {
                Debug.LogError("[PlayerSpawner] Spawn points are not properly assigned!");
                return;
            }

            if (_gameConfig == null)
            {
                Debug.LogError("[PlayerSpawner] GameConfig is not assigned!");
                return;
            }

            if (_arenaManager == null)
            {
                Debug.LogError("[PlayerSpawner] ArenaManager is not assigned!");
                return;
            }

            // Subscribe to client connection events
            ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;

            Debug.Log("[PlayerSpawner] Initialized and listening for client connections");
        }

        private void OnDisable()
        {
            if (IsServerInitialized)
            {
                ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
            }
        }

        /// <summary>
        /// Handle client connection and disconnection
        /// </summary>
        private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (!IsServerInitialized)
                return;

            switch (args.ConnectionState)
            {
                case RemoteConnectionState.Started:
                    HandleClientConnected(conn);
                    break;

                case RemoteConnectionState.Stopped:
                    HandleClientDisconnected(conn);
                    break;
            }
        }

        /// <summary>
        /// Called when a client connects
        /// </summary>
        private void HandleClientConnected(NetworkConnection conn)
        {
            Debug.Log($"[PlayerSpawner] Client connected: {conn.ClientId}");

            // Assign player index
            int playerIndex = _nextPlayerIndex;
            _nextPlayerIndex++;

            if (playerIndex >= 2)
            {
                Debug.LogWarning("[PlayerSpawner] Too many players connected! Only 2 players allowed.");
                conn.Disconnect(true);
                return;
            }

            _playerIndices[conn] = playerIndex;

            // Spawn vehicle for this player with default weight class
            WeightClass defaultWeightClass = WeightClass.Middle;
            SpawnVehicle(conn, playerIndex, defaultWeightClass);
        }

        /// <summary>
        /// Called when a client disconnects
        /// </summary>
        private void HandleClientDisconnected(NetworkConnection conn)
        {
            Debug.Log($"[PlayerSpawner] Client disconnected: {conn.ClientId}");

            DespawnVehicle(conn);
            _playerIndices.Remove(conn);
        }

        /// <summary>
        /// Spawn a vehicle for a player at the appropriate spawn point
        /// </summary>
        [Server]
        public void SpawnVehicle(NetworkConnection conn, int playerIndex, WeightClass weightClass)
        {
            if (!IsServerInitialized)
            {
                Debug.LogWarning("[PlayerSpawner] SpawnVehicle called on non-server!");
                return;
            }

            if (playerIndex < 0 || playerIndex >= _spawnPoints.Length)
            {
                Debug.LogError($"[PlayerSpawner] Invalid player index: {playerIndex}");
                return;
            }

            if (_spawnedVehicles.ContainsKey(conn))
            {
                Debug.LogWarning($"[PlayerSpawner] Vehicle already spawned for connection {conn.ClientId}");
                return;
            }

            try
            {
                // Instantiate vehicle at spawn point
                Transform spawnPoint = _spawnPoints[playerIndex];
                GameObject vehicleInstance = Instantiate(
                    _vehiclePrefab,
                    spawnPoint.position,
                    spawnPoint.rotation
                );

                if (vehicleInstance == null)
                {
                    Debug.LogError("[PlayerSpawner] Failed to instantiate vehicle prefab");
                    return;
                }

                // Configure ServerVehicleController
                ServerVehicleController vehicleController = vehicleInstance.GetComponent<ServerVehicleController>();
                if (vehicleController == null)
                {
                    Debug.LogError("[PlayerSpawner] Vehicle prefab does not have ServerVehicleController component!");
                    Destroy(vehicleInstance);
                    return;
                }

                vehicleController.SetWeightClass(weightClass);

                // PartManager의 기본 부품은 프리팹에 미리 설정됨
                // 추가 설정이 필요하면 여기서 처리
                PartManager partManager = vehicleInstance.GetComponent<PartManager>();
                if (partManager == null)
                {
                    Debug.LogWarning("[PlayerSpawner] Vehicle prefab does not have PartManager component");
                }

                // Spawn on network
                NetworkObject networkObject = vehicleInstance.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError("[PlayerSpawner] Vehicle prefab does not have NetworkObject component!");
                    Destroy(vehicleInstance);
                    return;
                }

                ServerManager.Spawn(vehicleInstance, conn);

                // Track the spawned vehicle
                _spawnedVehicles[conn] = networkObject;

                // Register with arena manager
                _arenaManager.RegisterPlayer(playerIndex, vehicleController);

                Debug.Log($"[PlayerSpawner] Spawned vehicle for Player {playerIndex} (Weight: {weightClass})");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[PlayerSpawner] Exception while spawning vehicle: {ex.Message}");
            }
        }

        /// <summary>
        /// Despawn a player's vehicle when they disconnect
        /// </summary>
        [Server]
        public void DespawnVehicle(NetworkConnection conn)
        {
            if (!IsServerInitialized)
                return;

            if (!_spawnedVehicles.TryGetValue(conn, out NetworkObject networkObject))
            {
                return; // No vehicle to despawn
            }

            if (networkObject != null && networkObject.gameObject != null)
            {
                ServerManager.Despawn(networkObject.gameObject);
                Debug.Log($"[PlayerSpawner] Despawned vehicle for connection {conn.ClientId}");
            }

            _spawnedVehicles.Remove(conn);
        }

        /// <summary>
        /// Get the player index for a connection
        /// </summary>
        public bool TryGetPlayerIndex(NetworkConnection conn, out int playerIndex)
        {
            return _playerIndices.TryGetValue(conn, out playerIndex);
        }

        /// <summary>
        /// Get the spawned vehicle for a connection
        /// </summary>
        public bool TryGetSpawnedVehicle(NetworkConnection conn, out NetworkObject networkObject)
        {
            return _spawnedVehicles.TryGetValue(conn, out networkObject);
        }

        /// <summary>
        /// Get total number of connected players
        /// </summary>
        public int GetConnectedPlayerCount()
        {
            return _playerIndices.Count;
        }

        /// <summary>
        /// Check if both players are connected
        /// </summary>
        public bool AreBothPlayersConnected()
        {
            return GetConnectedPlayerCount() == 2;
        }

        /// <summary>
        /// Reset the spawner (useful for resetting between matches)
        /// </summary>
        [Server]
        protected override void Reset()
        {
            if (!IsServerInitialized)
                return;

            _nextPlayerIndex = 0;
            _playerIndices.Clear();
            _spawnedVehicles.Clear();

            Debug.Log("[PlayerSpawner] Reset player spawner state");
        }
    }
}
