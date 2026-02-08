using System.Collections;
using UnityEngine;
using FishNet.Object;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// A front bumper part that raises a temporary shield, increasing the owner's mass and reducing knockback.
    /// </summary>
    [CreateAssetMenu(fileName = "ShieldBumper", menuName = "BattleCarSumo/Parts/Shield Bumper")]
    public class ShieldBumper : BasePart
    {
        /// <summary>
        /// Duration in seconds that the shield effect lasts.
        /// </summary>
        [SerializeField]
        private float shieldDuration = 2f;

        /// <summary>
        /// Multiplier applied to the owner's mass while shield is active (1.5 = 50% increase).
        /// </summary>
        [SerializeField]
        private float massMultiplier = 1.5f;

        /// <summary>
        /// Reference to the shield coroutine for cleanup if part is replaced.
        /// </summary>
        private Coroutine activeShieldCoroutine;

        private void OnEnable()
        {
            // Ensure slot and action type are set correctly
            if (slot != PartSlot.Front || actionType != PartActionType.Shield)
            {
                Debug.LogWarning($"{name} has incorrect slot or action type configuration.", this);
            }
        }

        /// <summary>
        /// Temporarily increases the owner's mass to reduce knockback received.
        /// Uses a coroutine concept to track state duration.
        /// </summary>
        public override void OnExecuteServer(NetworkBehaviour owner, Rigidbody ownerRb, Rigidbody targetRb)
        {
            if (ownerRb == null)
            {
                Debug.LogWarning("ShieldBumper: Owner rigidbody is null.", this);
                return;
            }

            // Store original mass
            float originalMass = ownerRb.mass;
            float shieldMass = originalMass * massMultiplier;

            // Increase mass temporarily
            ownerRb.mass = shieldMass;

            // Start coroutine to restore mass after duration
            if (owner is MonoBehaviour monoBehaviour)
            {
                if (activeShieldCoroutine != null)
                {
                    monoBehaviour.StopCoroutine(activeShieldCoroutine);
                }
                activeShieldCoroutine = monoBehaviour.StartCoroutine(RestoreMassAfterDelay(ownerRb, originalMass, shieldDuration));
            }

            Debug.Log($"ShieldBumper activated on {ownerRb.name}. Mass: {originalMass} -> {shieldMass}");
        }

        /// <summary>
        /// Triggers the shield animation on the owner's animator.
        /// </summary>
        public override void OnExecuteClientVisual(NetworkBehaviour owner)
        {
            if (owner == null)
            {
                Debug.LogWarning("ShieldBumper: Owner is null for client visual.", this);
                return;
            }

            Animator animator = owner.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetTrigger(ActionAnimationTrigger);
            }

            Debug.Log($"ShieldBumper animation triggered on {owner.name}");
        }

        /// <summary>
        /// Coroutine that restores the owner's mass after the shield duration expires.
        /// </summary>
        private IEnumerator RestoreMassAfterDelay(Rigidbody rigidbody, float originalMass, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (rigidbody != null)
            {
                rigidbody.mass = originalMass;
                Debug.Log($"Shield expired on {rigidbody.name}. Mass restored to {originalMass}");
            }

            activeShieldCoroutine = null;
        }

        /// <summary>
        /// Validates that this part's slot is Front.
        /// </summary>
        private PartSlot slot => PartSlot.Front;

        /// <summary>
        /// Validates that this part's action type is Shield.
        /// </summary>
        private PartActionType actionType => PartActionType.Shield;
    }
}
