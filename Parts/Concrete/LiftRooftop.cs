using UnityEngine;
using FishNet.Object;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// A rooftop part that lifts opponents upward, making them easier to push off the arena.
    /// </summary>
    [CreateAssetMenu(fileName = "LiftRooftop", menuName = "BattleCarSumo/Parts/Lift Rooftop")]
    public class LiftRooftop : BasePart
    {
        /// <summary>
        /// Maximum range in units within which the lift can affect targets.
        /// </summary>
        [SerializeField]
        private float liftRange = 5f;

        /// <summary>
        /// Upward force applied to lifted opponents.
        /// </summary>
        [SerializeField]
        private float liftForce = 15f;

        private void OnEnable()
        {
            // Ensure slot and action type are set correctly
            if (slot != PartSlot.Rooftop || actionType != PartActionType.Lift)
            {
                Debug.LogWarning($"{name} has incorrect slot or action type configuration.", this);
            }
        }

        /// <summary>
        /// Applies an upward force to the target if they are within lift range.
        /// This lifts the opponent, making them easier to push off the arena.
        /// </summary>
        public override void OnExecuteServer(NetworkBehaviour owner, Rigidbody ownerRb, Rigidbody targetRb)
        {
            if (ownerRb == null || targetRb == null)
            {
                Debug.LogWarning("LiftRooftop: Owner or target rigidbody is null.", this);
                return;
            }

            // Check if target is within range
            float distanceToTarget = Vector3.Distance(ownerRb.transform.position, targetRb.transform.position);
            if (distanceToTarget > liftRange)
            {
                Debug.Log($"LiftRooftop: Target is out of range ({distanceToTarget:F2} > {liftRange})");
                return;
            }

            // Apply upward force to target
            targetRb.linearVelocity = new Vector3(targetRb.linearVelocity.x, 0, targetRb.linearVelocity.z); // Reset vertical velocity
            targetRb.AddForce(Vector3.up * liftForce, ForceMode.Impulse);

            Debug.Log($"LiftRooftop executed on {targetRb.name} with upward force {liftForce}");
        }

        /// <summary>
        /// Triggers the lift animation on the owner's animator.
        /// </summary>
        public override void OnExecuteClientVisual(NetworkBehaviour owner)
        {
            if (owner == null)
            {
                Debug.LogWarning("LiftRooftop: Owner is null for client visual.", this);
                return;
            }

            Animator animator = owner.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger(ActionAnimationTrigger);
            }

            Debug.Log($"LiftRooftop animation triggered on {owner.name}");
        }

        /// <summary>
        /// Validates that this part's slot is Rooftop.
        /// </summary>
        private PartSlot slot => PartSlot.Rooftop;

        /// <summary>
        /// Validates that this part's action type is Lift.
        /// </summary>
        private PartActionType actionType => PartActionType.Lift;
    }
}
