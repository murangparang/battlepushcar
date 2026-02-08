using System.Collections.Generic;
using UnityEngine;
using BattleCarSumo.Parts;

namespace BattleCarSumo.Data
{
    /// <summary>
    /// ScriptableObject database containing all available car parts for Battle Car Sumo.
    /// Provides convenient access to parts by index, slot, or direct reference.
    /// </summary>
    [CreateAssetMenu(fileName = "PartDatabase", menuName = "BattleCarSumo/Part Database")]
    public class PartDatabase : ScriptableObject
    {
        /// <summary>
        /// List of all available parts in the game.
        /// Order matters as parts are referenced by index.
        /// </summary>
        [SerializeField]
        private List<BasePart> parts = new List<BasePart>();

        /// <summary>
        /// Gets the total number of parts in the database.
        /// </summary>
        public int PartCount => parts.Count;

        /// <summary>
        /// Gets the part at the specified index in the database.
        /// </summary>
        /// <param name="index">The index of the part to retrieve.</param>
        /// <returns>The BasePart at the index, or null if index is invalid.</returns>
        public BasePart GetPartByIndex(int index)
        {
            if (index < 0 || index >= parts.Count)
            {
                Debug.LogWarning($"PartDatabase: Invalid part index {index}. Database contains {parts.Count} parts.");
                return null;
            }

            return parts[index];
        }

        /// <summary>
        /// Gets all parts that fit in the specified slot.
        /// </summary>
        /// <param name="slot">The part slot to filter by.</param>
        /// <returns>A list of BaseParts that can be equipped in the specified slot.</returns>
        public List<BasePart> GetPartsForSlot(PartSlot slot)
        {
            List<BasePart> slotsPartsForSlot = new List<BasePart>();

            foreach (BasePart part in parts)
            {
                if (part != null && part.Slot == slot)
                {
                    slotsPartsForSlot.Add(part);
                }
            }

            if (slotsPartsForSlot.Count == 0)
            {
                Debug.LogWarning($"PartDatabase: No parts found for slot {slot}");
            }

            return slotsPartsForSlot;
        }

        /// <summary>
        /// Gets the index of a specific part in the database.
        /// Useful for setting up initial configurations or swapping parts by reference.
        /// </summary>
        /// <param name="part">The BasePart to find the index of.</param>
        /// <returns>The index of the part, or -1 if not found.</returns>
        public int GetPartIndex(BasePart part)
        {
            if (part == null)
            {
                Debug.LogWarning("PartDatabase: Cannot get index of null part");
                return -1;
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == part)
                {
                    return i;
                }
            }

            Debug.LogWarning($"PartDatabase: Part {part.PartName} not found in database");
            return -1;
        }

        /// <summary>
        /// Gets a part by its name.
        /// </summary>
        /// <param name="partName">The display name of the part to find.</param>
        /// <returns>The BasePart with the matching name, or null if not found.</returns>
        public BasePart GetPartByName(string partName)
        {
            if (string.IsNullOrEmpty(partName))
            {
                Debug.LogWarning("PartDatabase: Cannot search for null or empty part name");
                return null;
            }

            foreach (BasePart part in parts)
            {
                if (part != null && part.PartName == partName)
                {
                    return part;
                }
            }

            Debug.LogWarning($"PartDatabase: Part with name '{partName}' not found in database");
            return null;
        }

        /// <summary>
        /// Gets all parts with a specific action type.
        /// </summary>
        /// <param name="actionType">The action type to filter by.</param>
        /// <returns>A list of BaseParts that perform the specified action type.</returns>
        public List<BasePart> GetPartsByActionType(PartActionType actionType)
        {
            List<BasePart> partsWithAction = new List<BasePart>();

            foreach (BasePart part in parts)
            {
                if (part != null && part.ActionType == actionType)
                {
                    partsWithAction.Add(part);
                }
            }

            if (partsWithAction.Count == 0)
            {
                Debug.LogWarning($"PartDatabase: No parts found with action type {actionType}");
            }

            return partsWithAction;
        }

        /// <summary>
        /// Validates that all parts in the database are properly configured.
        /// Should be called during development/testing to catch configuration errors.
        /// </summary>
        public void ValidateDatabase()
        {
            int errorCount = 0;

            for (int i = 0; i < parts.Count; i++)
            {
                BasePart part = parts[i];

                if (part == null)
                {
                    Debug.LogError($"PartDatabase: Part at index {i} is null");
                    errorCount++;
                    continue;
                }

                if (string.IsNullOrEmpty(part.PartName))
                {
                    Debug.LogError($"PartDatabase: Part at index {i} has no name");
                    errorCount++;
                }

                if (part.Weight <= 0)
                {
                    Debug.LogWarning($"PartDatabase: Part '{part.PartName}' at index {i} has invalid weight ({part.Weight})");
                }

                if (part.ActionCooldown < 0)
                {
                    Debug.LogWarning($"PartDatabase: Part '{part.PartName}' at index {i} has negative cooldown ({part.ActionCooldown})");
                }

                if (part.PartPrefab == null)
                {
                    Debug.LogWarning($"PartDatabase: Part '{part.PartName}' at index {i} has no prefab assigned");
                }
            }

            if (errorCount == 0)
            {
                Debug.Log($"PartDatabase: Validation successful. Database contains {parts.Count} parts.");
            }
            else
            {
                Debug.LogError($"PartDatabase: Validation failed with {errorCount} errors");
            }
        }
    }
}
