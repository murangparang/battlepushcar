using System.Collections.Generic;
using UnityEngine;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// Manages the parts equipped on a car and handles part action execution.
    /// Synchronizes part configuration across the network using Fish-Net.
    /// </summary>
    public class PartManager : NetworkBehaviour
    {
        /// <summary>
        /// Reference to the PartDatabase containing all available parts.
        /// </summary>
        [SerializeField]
        private PartDatabase partDatabase;

        /// <summary>
        /// Reference to the game configuration.
        /// </summary>
        [SerializeField]
        private GameConfig gameConfig;

        /// <summary>
        /// Index of the currently equipped front part. Synced across network.
        /// </summary>
        public readonly SyncVar<int> frontPartIndex = new SyncVar<int>();

        /// <summary>
        /// Index of the currently equipped rooftop part. Synced across network.
        /// </summary>
        public readonly SyncVar<int> rooftopPartIndex = new SyncVar<int>();

        /// <summary>
        /// Index of the currently equipped rear part. Synced across network.
        /// </summary>
        public readonly SyncVar<int> rearPartIndex = new SyncVar<int>();

        /// <summary>
        /// Attachment point transforms on the car (Inspector에서 설정).
        /// 인덱스: 0=Front, 1=Rooftop, 2=Rear
        /// </summary>
        [SerializeField]
        private Transform[] partAttachmentTransforms = new Transform[3];

        /// <summary>
        /// 슬롯별 부착 포인트 딕셔너리 (런타임에 초기화)
        /// </summary>
        private Dictionary<PartSlot, Transform> partAttachmentPoints = new Dictionary<PartSlot, Transform>();

        /// <summary>
        /// Dictionary tracking the last execution time for each part slot (used for cooldown validation).
        /// </summary>
        private Dictionary<PartSlot, float> lastActionTimes = new Dictionary<PartSlot, float>();

        /// <summary>
        /// Dictionary storing instantiated part visual objects.
        /// </summary>
        private Dictionary<PartSlot, GameObject> instantiatedParts = new Dictionary<PartSlot, GameObject>();

        /// <summary>
        /// Cached rigidbody of the owner car.
        /// </summary>
        private Rigidbody cachedRigidbody;

        /// <summary>
        /// Cached transform of the owner car.
        /// </summary>
        private Transform cachedTransform;

        private void Awake()
        {
            frontPartIndex.Value = 0;
            rooftopPartIndex.Value = 1;
            rearPartIndex.Value = 2;
        }

        private void OnEnable()
        {
            // Initialize last action times to allow immediate first action
            InitializeLastActionTimes();

            // Inspector의 Transform 배열을 Dictionary로 변환
            if (partAttachmentTransforms != null)
            {
                if (partAttachmentTransforms.Length > 0 && partAttachmentTransforms[0] != null)
                    partAttachmentPoints[PartSlot.Front] = partAttachmentTransforms[0];
                if (partAttachmentTransforms.Length > 1 && partAttachmentTransforms[1] != null)
                    partAttachmentPoints[PartSlot.Rooftop] = partAttachmentTransforms[1];
                if (partAttachmentTransforms.Length > 2 && partAttachmentTransforms[2] != null)
                    partAttachmentPoints[PartSlot.Rear] = partAttachmentTransforms[2];
            }
        }

        private void Start()
        {
            // Cache commonly used components
            cachedRigidbody = GetComponent<Rigidbody>();
            cachedTransform = transform;

            if (IsServerInitialized)
            {
                // Spawn visual parts on server startup
                SpawnPartVisuals();
            }
        }

        /// <summary>
        /// Initializes the last action times dictionary for all part slots.
        /// </summary>
        private void InitializeLastActionTimes()
        {
            lastActionTimes[PartSlot.Front] = -999f;
            lastActionTimes[PartSlot.Rooftop] = -999f;
            lastActionTimes[PartSlot.Rear] = -999f;
        }

        /// <summary>
        /// Request to execute a part action. Server-side validation and execution.
        /// </summary>
        /// <param name="slot">The part slot to execute action for.</param>
        [ServerRpc]
        public void RequestActionServerRpc(PartSlot slot)
        {
            if (cachedRigidbody == null || partDatabase == null)
            {
                Debug.LogWarning("PartManager: Missing cached rigidbody or part database.", this);
                return;
            }

            // Get the current part for this slot
            BasePart currentPart = GetCurrentPart(slot);
            if (currentPart == null)
            {
                Debug.LogWarning($"PartManager: No part found for slot {slot}", this);
                return;
            }

            // Validate cooldown
            if (!lastActionTimes.ContainsKey(slot))
            {
                lastActionTimes[slot] = -999f;
            }

            if (!currentPart.CanExecute(lastActionTimes[slot]))
            {
                Debug.Log($"PartManager: Part {currentPart.PartName} is on cooldown");
                return;
            }

            // Find the opponent (simplified - in a real game, this would use a proper opponent finder)
            Rigidbody targetRigidbody = FindOpponent();

            // Execute the part's server-side logic
            currentPart.OnExecuteServer(this, cachedRigidbody, targetRigidbody);

            // Update last action time
            lastActionTimes[slot] = Time.time;

            // Call the visual RPC for all clients
            PlayActionVisualObserversRpc(slot);

            Debug.Log($"PartManager: Executed {currentPart.PartName} on {gameObject.name}");
        }

        /// <summary>
        /// RPC called on all clients to play visual effects and animations for part actions.
        /// </summary>
        /// <param name="slot">The part slot to play visuals for.</param>
        [ObserversRpc]
        private void PlayActionVisualObserversRpc(PartSlot slot)
        {
            BasePart currentPart = GetCurrentPart(slot);
            if (currentPart != null)
            {
                currentPart.OnExecuteClientVisual(this);
            }
        }

        /// <summary>
        /// Swaps the part in the specified slot with a new part by index.
        /// Validates weight limits before performing the swap.
        /// </summary>
        /// <param name="slot">The slot to swap the part in.</param>
        /// <param name="newPartIndex">The index of the new part in the part database.</param>
        [ServerRpc(RequireOwnership = true)]
        public void SwapPartServerRpc(PartSlot slot, int newPartIndex)
        {
            if (!IsServerInitialized)
                return;

            if (partDatabase == null)
            {
                Debug.LogWarning("PartManager: Part database is null.", this);
                return;
            }

            BasePart newPart = partDatabase.GetPartByIndex(newPartIndex);
            if (newPart == null)
            {
                Debug.LogWarning($"PartManager: Invalid part index {newPartIndex}", this);
                return;
            }

            // Calculate weight if we swap this part
            float currentTotalWeight = CalculateTotalPartsWeight();
            float oldPartWeight = GetCurrentPart(slot)?.Weight ?? 0;
            float newTotalWeight = currentTotalWeight - oldPartWeight + newPart.Weight;

            // Validate against weight limit (체급별 최대 무게 기준)
            // Note: 체급은 ServerVehicleController에서 관리. 여기서는 일반적 무게 검증.
            // 실제 체급별 검증은 ServerVehicleController.RecalculateTotalWeight()에서 수행.
            float maxWeight = gameConfig != null ? gameConfig.GetMaxWeightForClass(Data.WeightClass.Heavy) : float.MaxValue;
            if (newTotalWeight > maxWeight)
            {
                Debug.Log($"PartManager: Cannot equip {newPart.PartName}. Would exceed weight limit ({newTotalWeight} > {maxWeight})");
                return;
            }

            // Perform the swap
            switch (slot)
            {
                case PartSlot.Front:
                    frontPartIndex.Value = newPartIndex;
                    break;
                case PartSlot.Rooftop:
                    rooftopPartIndex.Value = newPartIndex;
                    break;
                case PartSlot.Rear:
                    rearPartIndex.Value = newPartIndex;
                    break;
                default:
                    Debug.LogWarning($"PartManager: Unknown part slot {slot}", this);
                    return;
            }

            // Respawn visuals with new part
            SpawnPartVisuals();

            Debug.Log($"PartManager: Swapped {slot} to {newPart.PartName}. Total weight: {newTotalWeight}");
        }

        /// <summary>
        /// Gets the BasePart currently equipped in the specified slot.
        /// </summary>
        /// <param name="slot">The slot to get the part from.</param>
        /// <returns>The BasePart in the slot, or null if not found.</returns>
        public BasePart GetCurrentPart(PartSlot slot)
        {
            if (partDatabase == null)
                return null;

            int partIndex = slot switch
            {
                PartSlot.Front => frontPartIndex.Value,
                PartSlot.Rooftop => rooftopPartIndex.Value,
                PartSlot.Rear => rearPartIndex.Value,
                _ => -1
            };

            if (partIndex < 0)
                return null;

            return partDatabase.GetPartByIndex(partIndex);
        }

        /// <summary>
        /// Calculates the total weight of all currently equipped parts.
        /// </summary>
        /// <returns>Sum of weights of all equipped parts.</returns>
        public float CalculateTotalPartsWeight()
        {
            float totalWeight = 0;

            BasePart frontPart = GetCurrentPart(PartSlot.Front);
            if (frontPart != null)
                totalWeight += frontPart.Weight;

            BasePart rooftopPart = GetCurrentPart(PartSlot.Rooftop);
            if (rooftopPart != null)
                totalWeight += rooftopPart.Weight;

            BasePart rearPart = GetCurrentPart(PartSlot.Rear);
            if (rearPart != null)
                totalWeight += rearPart.Weight;

            return totalWeight;
        }

        /// <summary>
        /// Spawns or respawns the visual prefabs for all currently equipped parts.
        /// Instantiates part prefabs at the correct attachment points.
        /// </summary>
        private void SpawnPartVisuals()
        {
            if (!IsServerInitialized)
                return;

            // Clear existing visuals
            foreach (var visual in instantiatedParts.Values)
            {
                if (visual != null)
                    Destroy(visual);
            }
            instantiatedParts.Clear();

            // Spawn new visuals for each slot
            SpawnPartVisualForSlot(PartSlot.Front);
            SpawnPartVisualForSlot(PartSlot.Rooftop);
            SpawnPartVisualForSlot(PartSlot.Rear);
        }

        /// <summary>
        /// Spawns the visual prefab for a specific part slot.
        /// </summary>
        private void SpawnPartVisualForSlot(PartSlot slot)
        {
            BasePart part = GetCurrentPart(slot);
            if (part == null || part.PartPrefab == null)
                return;

            Transform attachmentPoint = null;
            if (partAttachmentPoints.TryGetValue(slot, out var point))
            {
                attachmentPoint = point;
            }

            Transform parentTransform = attachmentPoint ?? cachedTransform;
            GameObject visualInstance = Instantiate(part.PartPrefab, parentTransform.position, parentTransform.rotation, parentTransform);
            instantiatedParts[slot] = visualInstance;

            Debug.Log($"PartManager: Spawned visual for {part.PartName} at {slot}");
        }

        /// <summary>
        /// Finds an opponent rigidbody. In a real implementation, this would use proper opponent tracking.
        /// </summary>
        /// <returns>A target opponent's rigidbody, or null if none found.</returns>
        private Rigidbody FindOpponent()
        {
            // Simplified implementation - finds the first car with a different owner
            PartManager[] allPartManagers = FindObjectsByType<PartManager>(FindObjectsSortMode.None);
            foreach (PartManager pm in allPartManagers)
            {
                if (pm.gameObject != gameObject && pm.GetComponent<Rigidbody>() != null)
                {
                    return pm.GetComponent<Rigidbody>();
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the front part index.
        /// </summary>
        public int FrontPartIndex => frontPartIndex.Value;

        /// <summary>
        /// Gets the rooftop part index.
        /// </summary>
        public int RooftopPartIndex => rooftopPartIndex.Value;

        /// <summary>
        /// Gets the rear part index.
        /// </summary>
        public int RearPartIndex => rearPartIndex.Value;
    }
}
