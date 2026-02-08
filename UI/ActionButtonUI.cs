using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BattleCarSumo.Data;
using BattleCarSumo.Parts;

namespace BattleCarSumo.UI
{
    /// <summary>
    /// UI script for a single action button representing a car part action.
    /// Displays the part icon, name, and manages cooldown visuals.
    /// </summary>
    public class ActionButtonUI : MonoBehaviour
    {
        [SerializeField]
        private PartSlot assignedSlot;

        [SerializeField]
        private Button button;

        [SerializeField]
        private Image iconImage;

        [SerializeField]
        private Image cooldownOverlay;

        [SerializeField]
        private TextMeshProUGUI partNameText;

        private PartManager partManager;
        private float cooldownEndTime;
        private bool isOnCooldown;

        private void OnEnable()
        {
            if (button != null)
            {
                button.onClick.AddListener(OnActionButtonClicked);
            }
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(OnActionButtonClicked);
            }
        }

        private void Update()
        {
            if (isOnCooldown)
            {
                UpdateCooldownDisplay();
            }
        }

        /// <summary>
        /// Sets the PartManager reference for this button.
        /// Called when the local player vehicle is spawned.
        /// </summary>
        public void SetPartManager(PartManager pm)
        {
            if (pm == null)
            {
                Debug.LogWarning("ActionButtonUI: Attempted to set null PartManager");
                return;
            }
            partManager = pm;
        }

        /// <summary>
        /// Updates the button visuals with the new part information.
        /// Called when parts are swapped during intermission.
        /// </summary>
        public void UpdatePartInfo(BasePart part)
        {
            if (part == null)
            {
                if (partNameText != null)
                {
                    partNameText.text = "---";
                }
                if (iconImage != null)
                {
                    iconImage.sprite = null;
                }
                return;
            }

            if (partNameText != null)
            {
                partNameText.text = part.PartName;
            }

            if (iconImage != null)
            {
                // PartIcon property does not exist on BasePart, skipping icon assignment
                // iconImage.sprite = part.PartIcon;
            }
        }

        /// <summary>
        /// Enables or disables button interactivity based on game state.
        /// </summary>
        public void SetInteractable(bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        /// <summary>
        /// Initiates a cooldown period for this button.
        /// </summary>
        public void StartCooldown(float cooldownDuration)
        {
            if (cooldownDuration <= 0f)
            {
                return;
            }

            cooldownEndTime = Time.time + cooldownDuration;
            isOnCooldown = true;
            SetInteractable(false);
        }

        private void OnActionButtonClicked()
        {
            if (partManager == null)
            {
                Debug.LogWarning("ActionButtonUI: PartManager not set, cannot request action");
                return;
            }

            if (isOnCooldown)
            {
                return;
            }

            partManager.RequestActionServerRpc(assignedSlot);
        }

        private void UpdateCooldownDisplay()
        {
            float remainingTime = cooldownEndTime - Time.time;

            if (remainingTime <= 0f)
            {
                isOnCooldown = false;
                SetInteractable(true);

                if (cooldownOverlay != null)
                {
                    cooldownOverlay.fillAmount = 0f;
                }

                return;
            }

            if (cooldownOverlay != null)
            {
                // Fill from 1 (full cooldown) to 0 (ready)
                float cooldownDuration = cooldownEndTime - (Time.time - remainingTime);
                cooldownOverlay.fillAmount = remainingTime / cooldownDuration;
            }
        }

        /// <summary>
        /// Resets the button state (removes cooldown, enables interaction).
        /// </summary>
        public void ResetButton()
        {
            isOnCooldown = false;
            cooldownEndTime = 0f;
            SetInteractable(true);

            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = 0f;
            }
        }
    }
}
