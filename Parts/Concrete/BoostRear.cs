using UnityEngine;
using FishNet.Object;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// A rear part that provides a strong forward boost to the owner, like a nitro effect.
    /// </summary>
    [CreateAssetMenu(fileName = "BoostRear", menuName = "BattleCarSumo/Parts/Boost Rear")]
    public class BoostRear : BasePart
    {
        /// <summary>
        /// Additional force multiplier specific to boost (scales the base ActionForce).
        /// </summary>
        [SerializeField]
        private float boostForceMultiplier = 1.5f;

        private void OnEnable()
        {
            // Ensure slot and action type are set correctly
            if (slot != PartSlot.Rear || actionType != PartActionType.Boost)
            {
                Debug.LogWarning($"{name} has incorrect slot or action type configuration.", this);
            }
        }

        /// <summary>
        /// Applies a strong forward force to the owner (self-boost), propelling them forward like nitro.
        /// </summary>
        public override void OnExecuteServer(NetworkBehaviour owner, Rigidbody ownerRb, Rigidbody targetRb)
        {
            if (ownerRb == null)
            {
                Debug.LogWarning("BoostRear: Owner rigidbody is null.", this);
                return;
            }

            // Calculate boost force based on owner's forward direction
            Vector3 boostDirection = ownerRb.transform.forward;
            float boostForce = ActionForce * boostForceMultiplier;

            // Apply forward impulse force to owner
            ownerRb.AddForce(boostDirection * boostForce, ForceMode.Impulse);

            Debug.Log($"BoostRear executed on {ownerRb.name} with forward force {boostForce}");
        }

        /// <summary>
        /// Triggers the boost VFX and animation on the owner.
        /// </summary>
        public override void OnExecuteClientVisual(NetworkBehaviour owner)
        {
            if (owner == null)
            {
                Debug.LogWarning("BoostRear: Owner is null for client visual.", this);
                return;
            }

            Animator animator = owner.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger(ActionAnimationTrigger);
            }

            Debug.Log($"BoostRear animation/VFX triggered on {owner.name}");
        }

        /// <summary>
        /// Validates that this part's slot is Rear.
        /// </summary>
        private PartSlot slot => PartSlot.Rear;

        /// <summary>
        /// Validates that this part's action type is Boost.
        /// </summary>
        private PartActionType actionType => PartActionType.Boost;
    }
}
