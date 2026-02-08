using UnityEngine;
using FishNet.Object;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// A front bumper part that punches opponents forward, dealing knockback damage.
    /// </summary>
    [CreateAssetMenu(fileName = "PunchBumper", menuName = "BattleCarSumo/Parts/Punch Bumper")]
    public class PunchBumper : BasePart
    {
        private void OnEnable()
        {
            // Ensure slot and action type are set correctly
            if (slot != PartSlot.Front || actionType != PartActionType.Punch)
            {
                Debug.LogWarning($"{name} has incorrect slot or action type configuration.", this);
            }
        }

        /// <summary>
        /// Applies a forward impulse force to the target rigidbody, pushing the opponent away.
        /// The direction is calculated from the owner to the target.
        /// </summary>
        public override void OnExecuteServer(NetworkBehaviour owner, Rigidbody ownerRb, Rigidbody targetRb)
        {
            if (ownerRb == null || targetRb == null)
            {
                Debug.LogWarning("PunchBumper: Owner or target rigidbody is null.", this);
                return;
            }

            // Calculate direction from owner to target
            Vector3 direction = (targetRb.transform.position - ownerRb.transform.position).normalized;

            // Apply impulse force to target
            targetRb.linearVelocity = Vector3.zero; // Reset velocity for consistent punch
            targetRb.AddForce(direction * ActionForce, ForceMode.Impulse);

            Debug.Log($"PunchBumper executed on {targetRb.name} with force {ActionForce}");
        }

        /// <summary>
        /// Triggers the punch animation on the owner's animator.
        /// </summary>
        public override void OnExecuteClientVisual(NetworkBehaviour owner)
        {
            if (owner == null)
            {
                Debug.LogWarning("PunchBumper: Owner is null for client visual.", this);
                return;
            }

            Animator animator = owner.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger(ActionAnimationTrigger);
            }

            Debug.Log($"PunchBumper animation triggered on {owner.name}");
        }

        /// <summary>
        /// Validates that this part's slot is Front.
        /// </summary>
        private PartSlot slot => PartSlot.Front;

        /// <summary>
        /// Validates that this part's action type is Punch.
        /// </summary>
        private PartActionType actionType => PartActionType.Punch;
    }
}
