using UnityEngine;
using FishNet.Object;
using BattleCarSumo.Data;

namespace BattleCarSumo.Parts
{
    /// <summary>
    /// Abstract base class for all car parts in Battle Car Sumo.
    /// Defines the interface for offensive/defensive actions and common properties.
    /// </summary>
    public abstract class BasePart : ScriptableObject
    {
        /// <summary>
        /// Display name of the part.
        /// </summary>
        [SerializeField]
        private string partName = "Part";

        /// <summary>
        /// Weight contribution of this part to the car's total weight.
        /// </summary>
        [SerializeField]
        private float weight = 1f;

        /// <summary>
        /// The slot this part occupies on the car (Front, Rooftop, Rear).
        /// </summary>
        [SerializeField]
        private PartSlot slot = PartSlot.Front;

        /// <summary>
        /// The type of action this part performs (Punch, Shield, Lift, etc.).
        /// </summary>
        [SerializeField]
        private PartActionType actionType = PartActionType.Punch;

        /// <summary>
        /// Prefab to instantiate for visual representation of this part.
        /// </summary>
        [SerializeField]
        private GameObject partPrefab;

        /// <summary>
        /// Cooldown duration in seconds between action uses.
        /// </summary>
        [SerializeField]
        private float actionCooldown = 1f;

        /// <summary>
        /// Force magnitude applied when executing this part's action.
        /// </summary>
        [SerializeField]
        private float actionForce = 10f;

        /// <summary>
        /// Animation trigger name to play on the owner when executing this action.
        /// </summary>
        [SerializeField]
        private string actionAnimationTrigger = "Action";

        /// <summary>
        /// Gets the display name of this part.
        /// </summary>
        public string PartName => partName;

        /// <summary>
        /// Gets the weight contribution of this part.
        /// </summary>
        public float Weight => weight;

        /// <summary>
        /// Gets the slot this part occupies.
        /// </summary>
        public PartSlot Slot => slot;

        /// <summary>
        /// Gets the action type this part performs.
        /// </summary>
        public PartActionType ActionType => actionType;

        /// <summary>
        /// Gets the prefab for this part's visual representation.
        /// </summary>
        public GameObject PartPrefab => partPrefab;

        /// <summary>
        /// Gets the cooldown duration for this action.
        /// </summary>
        public float ActionCooldown => actionCooldown;

        /// <summary>
        /// Gets the force magnitude for this action.
        /// </summary>
        public float ActionForce => actionForce;

        /// <summary>
        /// Gets the animation trigger name.
        /// </summary>
        public string ActionAnimationTrigger => actionAnimationTrigger;

        /// <summary>
        /// Executes the server-side logic for this part's action.
        /// Applies physics forces and modifies game state.
        /// </summary>
        /// <param name="owner">The NetworkBehaviour owning this part (the car).</param>
        /// <param name="ownerRb">The rigidbody of the owner.</param>
        /// <param name="targetRb">The rigidbody of the target, if applicable.</param>
        public abstract void OnExecuteServer(NetworkBehaviour owner, Rigidbody ownerRb, Rigidbody targetRb);

        /// <summary>
        /// Executes the client-side visual logic for this part's action.
        /// Plays animations and visual effects.
        /// </summary>
        /// <param name="owner">The NetworkBehaviour owning this part.</param>
        public abstract void OnExecuteClientVisual(NetworkBehaviour owner);

        /// <summary>
        /// Validates whether this part's action can be executed based on cooldown.
        /// </summary>
        /// <param name="lastActionTime">The last time this action was executed (in Time.time).</param>
        /// <returns>True if enough time has passed to execute the action again, false otherwise.</returns>
        public virtual bool CanExecute(float lastActionTime)
        {
            return Time.time - lastActionTime >= actionCooldown;
        }
    }
}
