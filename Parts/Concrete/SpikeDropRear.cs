using UnityEngine;
using FishNet.Object;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// A rear part that drops spikes behind the car, slowing enemies who hit them.
    /// Also applies backward force to targets behind the owner.
    /// </summary>
    [CreateAssetMenu(fileName = "SpikeDropRear", menuName = "BattleCarSumo/Parts/Spike Drop Rear")]
    public class SpikeDropRear : BasePart
    {
        /// <summary>
        /// Prefab for the spike object to instantiate.
        /// </summary>
        [SerializeField]
        private GameObject spikePrefab;

        /// <summary>
        /// Force applied backward to targets behind the owner.
        /// </summary>
        [SerializeField]
        private float backwardForce = 8f;

        /// <summary>
        /// Offset behind the owner where spikes are dropped.
        /// </summary>
        [SerializeField]
        private Vector3 spikeDropOffset = new Vector3(0, 0.5f, -2f);

        private void OnEnable()
        {
            // Ensure slot and action type are set correctly
            if (slot != PartSlot.Rear || actionType != PartActionType.SpikeDrop)
            {
                Debug.LogWarning($"{name} has incorrect slot or action type configuration.", this);
            }
        }

        /// <summary>
        /// Drops spikes behind the owner and applies backward force if target is behind.
        /// The spikes slow enemies who collide with them.
        /// </summary>
        public override void OnExecuteServer(NetworkBehaviour owner, Rigidbody ownerRb, Rigidbody targetRb)
        {
            if (ownerRb == null)
            {
                Debug.LogWarning("SpikeDropRear: Owner rigidbody is null.", this);
                return;
            }

            // Drop spike at offset position behind owner
            if (spikePrefab != null)
            {
                Vector3 spikePosition = ownerRb.transform.position + ownerRb.transform.TransformDirection(spikeDropOffset);
                Instantiate(spikePrefab, spikePosition, ownerRb.transform.rotation);
                Debug.Log($"Spike dropped at {spikePosition}");
            }
            else
            {
                Debug.LogWarning("SpikeDropRear: Spike prefab is not assigned.", this);
            }

            // Apply backward force to target if they are behind owner
            if (targetRb != null)
            {
                Vector3 directionToTarget = (targetRb.transform.position - ownerRb.transform.position).normalized;
                Vector3 ownerBackward = -ownerRb.transform.forward;

                // Check if target is behind owner (dot product > 0)
                if (Vector3.Dot(directionToTarget, ownerBackward) > 0)
                {
                    targetRb.AddForce(ownerBackward * backwardForce, ForceMode.Impulse);
                    Debug.Log($"SpikeDropRear backward force applied to {targetRb.name}");
                }
            }

            Debug.Log($"SpikeDropRear executed on owner {ownerRb.name}");
        }

        /// <summary>
        /// Triggers the spike drop animation on the owner.
        /// </summary>
        public override void OnExecuteClientVisual(NetworkBehaviour owner)
        {
            if (owner == null)
            {
                Debug.LogWarning("SpikeDropRear: Owner is null for client visual.", this);
                return;
            }

            Animator animator = owner.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger(ActionAnimationTrigger);
            }

            Debug.Log($"SpikeDropRear animation triggered on {owner.name}");
        }

        /// <summary>
        /// Validates that this part's slot is Rear.
        /// </summary>
        private PartSlot slot => PartSlot.Rear;

        /// <summary>
        /// Validates that this part's action type is SpikeDrop.
        /// </summary>
        private PartActionType actionType => PartActionType.SpikeDrop;
    }
}
