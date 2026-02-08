using System.Collections;
using UnityEngine;
using FishNet.Object;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// A rooftop part that slams opponents downward, temporarily increasing their friction.
    /// </summary>
    [CreateAssetMenu(fileName = "SlamRooftop", menuName = "BattleCarSumo/Parts/Slam Rooftop")]
    public class SlamRooftop : BasePart
    {
        /// <summary>
        /// Downward force applied to slammed opponents.
        /// </summary>
        [SerializeField]
        private float slamForce = 20f;

        /// <summary>
        /// Duration in seconds that the increased friction effect lasts.
        /// </summary>
        [SerializeField]
        private float frictionDuration = 2f;

        /// <summary>
        /// Friction multiplier applied while slam effect is active (higher = more friction/slowdown).
        /// </summary>
        [SerializeField]
        private float frictionMultiplier = 2f;

        private void OnEnable()
        {
            // Ensure slot and action type are set correctly
            if (slot != PartSlot.Rooftop || actionType != PartActionType.Slam)
            {
                Debug.LogWarning($"{name} has incorrect slot or action type configuration.", this);
            }
        }

        /// <summary>
        /// Applies a downward force to the target and temporarily increases their friction.
        /// </summary>
        public override void OnExecuteServer(NetworkBehaviour owner, Rigidbody ownerRb, Rigidbody targetRb)
        {
            if (ownerRb == null || targetRb == null)
            {
                Debug.LogWarning("SlamRooftop: Owner or target rigidbody is null.", this);
                return;
            }

            // Apply downward force to target
            targetRb.linearVelocity = new Vector3(targetRb.linearVelocity.x, 0, targetRb.linearVelocity.z); // Reset vertical velocity
            targetRb.AddForce(Vector3.down * slamForce, ForceMode.Impulse);

            // Store original drag value
            float originalDrag = targetRb.linearDamping;
            float increasedDrag = originalDrag * frictionMultiplier;

            // Increase friction temporarily
            targetRb.linearDamping = increasedDrag;

            // Start coroutine to restore original drag after duration
            if (owner is MonoBehaviour monoBehaviour)
            {
                monoBehaviour.StartCoroutine(RestoreFrictionAfterDelay(targetRb, originalDrag, frictionDuration));
            }

            Debug.Log($"SlamRooftop executed on {targetRb.name} with downward force {slamForce}. Drag: {originalDrag} -> {increasedDrag}");
        }

        /// <summary>
        /// Triggers the slam animation on the owner's animator.
        /// </summary>
        public override void OnExecuteClientVisual(NetworkBehaviour owner)
        {
            if (owner == null)
            {
                Debug.LogWarning("SlamRooftop: Owner is null for client visual.", this);
                return;
            }

            Animator animator = owner.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger(ActionAnimationTrigger);
            }

            Debug.Log($"SlamRooftop animation triggered on {owner.name}");
        }

        /// <summary>
        /// Coroutine that restores the target's original drag after the slam effect expires.
        /// </summary>
        private IEnumerator RestoreFrictionAfterDelay(Rigidbody rigidbody, float originalDrag, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (rigidbody != null)
            {
                rigidbody.linearDamping = originalDrag;
                Debug.Log($"Slam friction effect expired on {rigidbody.name}. Drag restored to {originalDrag}");
            }
        }

        /// <summary>
        /// Validates that this part's slot is Rooftop.
        /// </summary>
        private PartSlot slot => PartSlot.Rooftop;

        /// <summary>
        /// Validates that this part's action type is Slam.
        /// </summary>
        private PartActionType actionType => PartActionType.Slam;
    }
}
