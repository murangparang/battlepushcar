using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using BattleCarSumo.Data;
using BattleCarSumo.Parts;

namespace BattleCarSumo.UI
{
    /// <summary>
    /// UI for the 30-second intermission period where players can swap car parts.
    /// Displays available parts for each slot (Front, Rooftop, Rear) within weight class limits.
    /// </summary>
    public class IntermissionUI : MonoBehaviour
    {
        [SerializeField]
        private Transform[] slotContainers;

        [SerializeField]
        private GameObject partOptionPrefab;

        [SerializeField]
        private TextMeshProUGUI weightText;

        [SerializeField]
        private TextMeshProUGUI timerText;

        [SerializeField]
        private CanvasGroup panelCanvasGroup;

        private PartManager localPlayerPartManager;
        private WeightClass currentWeightClass;
        private Dictionary<PartSlot, BasePart> currentEquippedParts = new Dictionary<PartSlot, BasePart>();
        private int currentTotalWeight;
        private int maxWeightForClass;

        /// <summary>
        /// GameConfig structure containing part definitions and weight limits.
        /// This would be injected or loaded from a config file.
        /// </summary>
        public struct GameConfig
        {
            public Dictionary<WeightClass, int> MaxWeights;
            public Dictionary<WeightClass, List<BasePart>> AvailableParts;
        }

        private void OnEnable()
        {
            if (panelCanvasGroup == null)
            {
                panelCanvasGroup = GetComponent<CanvasGroup>();
            }
        }

        /// <summary>
        /// Initializes and shows the intermission panel with available parts.
        /// </summary>
        public void Show(PartManager pm, WeightClass weightClass, GameConfig config)
        {
            if (pm == null)
            {
                Debug.LogWarning("IntermissionUI: Attempted to show with null PartManager");
                return;
            }

            localPlayerPartManager = pm;
            currentWeightClass = weightClass;

            // Set weight limits based on weight class
            if (config.MaxWeights != null && config.MaxWeights.ContainsKey(weightClass))
            {
                maxWeightForClass = config.MaxWeights[weightClass];
            }
            else
            {
                maxWeightForClass = 1800; // Default fallback
            }

            // Get currently equipped parts
            GetCurrentEquippedParts();

            // Clear previous UI
            ClearSlotContainers();

            // Populate slots with available parts
            if (config.AvailableParts != null)
            {
                PopulateSlot(PartSlot.Front, config.AvailableParts.ContainsKey(weightClass) ? config.AvailableParts[weightClass] : new List<BasePart>());
                PopulateSlot(PartSlot.Rooftop, config.AvailableParts.ContainsKey(weightClass) ? config.AvailableParts[weightClass] : new List<BasePart>());
                PopulateSlot(PartSlot.Rear, config.AvailableParts.ContainsKey(weightClass) ? config.AvailableParts[weightClass] : new List<BasePart>());
            }

            UpdateWeightDisplay();

            // Show panel
            gameObject.SetActive(true);
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// Populates a slot container with available part options.
        /// </summary>
        private void PopulateSlot(PartSlot slot, List<BasePart> availableParts)
        {
            if (slotContainers == null || slotContainers.Length == 0)
            {
                Debug.LogWarning("IntermissionUI: Slot containers not assigned");
                return;
            }

            int slotIndex = (int)slot;
            if (slotIndex < 0 || slotIndex >= slotContainers.Length || slotContainers[slotIndex] == null)
            {
                Debug.LogWarning($"IntermissionUI: Invalid slot index {slotIndex}");
                return;
            }

            Transform container = slotContainers[slotIndex];

            // Clear existing buttons
            foreach (Transform child in container)
            {
                Destroy(child.gameObject);
            }

            if (availableParts == null || availableParts.Count == 0)
            {
                Debug.LogWarning($"IntermissionUI: No available parts for slot {slot}");
                return;
            }

            // Create button for each available part
            for (int i = 0; i < availableParts.Count; i++)
            {
                BasePart part = availableParts[i];
                if (part == null)
                {
                    continue;
                }

                GameObject buttonGO = Instantiate(partOptionPrefab, container);
                if (buttonGO == null)
                {
                    continue;
                }

                Button button = buttonGO.GetComponent<Button>();
                TextMeshProUGUI labelText = buttonGO.GetComponentInChildren<TextMeshProUGUI>();
                Image partIcon = buttonGO.GetComponent<Image>();

                if (labelText != null)
                {
                    labelText.text = $"{part.PartName}\n({part.Weight}kg)";
                }

                // PartIcon property does not exist on BasePart, skipping icon assignment
                // if (partIcon != null && part.PartIcon != null)
                // {
                //     partIcon.sprite = part.PartIcon;
                // }

                // Highlight currently equipped part
                bool isEquipped = currentEquippedParts.ContainsKey(slot) && currentEquippedParts[slot] == part;
                if (button != null)
                {
                    ColorBlock colors = button.colors;
                    if (isEquipped)
                    {
                        colors.normalColor = Color.cyan;
                    }

                    button.colors = colors;

                    // Check if selecting this part would exceed weight limit
                    int newWeight = CalculateWeightIfPartSelected(slot, part);
                    bool wouldExceedLimit = newWeight > maxWeightForClass;

                    if (wouldExceedLimit)
                    {
                        button.interactable = false;
                        ColorBlock disabledColors = button.colors;
                        disabledColors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
                        button.colors = disabledColors;

                        if (labelText != null)
                        {
                            labelText.text += "\n[TOO HEAVY]";
                        }
                    }
                    else
                    {
                        int partIndex = i;
                        button.onClick.AddListener(() => OnPartSelected(slot, partIndex));
                    }
                }
            }
        }

        /// <summary>
        /// Called when a part option is selected.
        /// </summary>
        private void OnPartSelected(PartSlot slot, int partIndex)
        {
            if (localPlayerPartManager == null)
            {
                Debug.LogWarning("IntermissionUI: PartManager is null");
                return;
            }

            // Swap the part through the PartManager
            localPlayerPartManager.SwapPartServerRpc(slot, partIndex);

            // Update weight and UI
            UpdateWeightDisplay();
        }

        /// <summary>
        /// Updates the weight display showing current and maximum weight.
        /// </summary>
        public void UpdateWeightDisplay()
        {
            if (weightText == null)
            {
                return;
            }

            GetCurrentEquippedParts();
            currentTotalWeight = 0;

            foreach (var part in currentEquippedParts.Values)
            {
                if (part != null)
                {
                    currentTotalWeight += (int)part.Weight;
                }
            }

            weightText.text = string.Format("총 무게: {0}/{1} kg", currentTotalWeight, maxWeightForClass);

            // Change color if over limit
            if (currentTotalWeight > maxWeightForClass)
            {
                weightText.color = Color.red;
            }
            else
            {
                weightText.color = Color.white;
            }
        }

        /// <summary>
        /// Updates the intermission timer display.
        /// </summary>
        public void UpdateTimer(float remainingTime)
        {
            if (timerText == null)
            {
                return;
            }

            int seconds = Mathf.FloorToInt(remainingTime);
            timerText.text = string.Format("시간 남음: {0}초", seconds);
        }

        /// <summary>
        /// Hides the intermission panel.
        /// </summary>
        public void Hide()
        {
            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = 0f;
            }

            gameObject.SetActive(false);
        }

        private void GetCurrentEquippedParts()
        {
            currentEquippedParts.Clear();

            if (localPlayerPartManager == null)
            {
                return;
            }

            currentEquippedParts[PartSlot.Front] = localPlayerPartManager.GetCurrentPart(PartSlot.Front);
            currentEquippedParts[PartSlot.Rooftop] = localPlayerPartManager.GetCurrentPart(PartSlot.Rooftop);
            currentEquippedParts[PartSlot.Rear] = localPlayerPartManager.GetCurrentPart(PartSlot.Rear);
        }

        private int CalculateWeightIfPartSelected(PartSlot slot, BasePart newPart)
        {
            int totalWeight = 0;

            foreach (PartSlot s in System.Enum.GetValues(typeof(PartSlot)))
            {
                if (s == slot)
                {
                    if (newPart != null)
                    {
                        totalWeight += (int)newPart.Weight;
                    }
                }
                else
                {
                    BasePart currentPart = currentEquippedParts.ContainsKey(s) ? currentEquippedParts[s] : null;
                    if (currentPart != null)
                    {
                        totalWeight += (int)currentPart.Weight;
                    }
                }
            }

            return totalWeight;
        }

        private void ClearSlotContainers()
        {
            if (slotContainers == null)
            {
                return;
            }

            foreach (Transform container in slotContainers)
            {
                if (container == null)
                {
                    continue;
                }

                foreach (Transform child in container)
                {
                    Destroy(child.gameObject);
                }
            }
        }
    }
}
