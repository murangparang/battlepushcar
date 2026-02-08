using UnityEngine;
using UnityEngine.EventSystems;

namespace BattleCarSumo.UI
{
    /// <summary>
    /// Mobile virtual joystick for controlling vehicle movement.
    /// Provides normalized input direction from -1 to 1 on both axes.
    /// Y-axis represents throttle (forward/backward), X-axis represents steering (left/right).
    /// </summary>
    public class VirtualJoystickUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        [SerializeField]
        private RectTransform background;

        [SerializeField]
        private RectTransform handle;

        [SerializeField]
        private float handleRange = 50f;

        private Vector2 inputDirection = Vector2.zero;
        private Vector2 startPosition;
        private bool isActive;

        /// <summary>
        /// Gets the normalized input direction from the joystick.
        /// X: steering (-1 left, 0 center, 1 right)
        /// Y: throttle (-1 backward, 0 center, 1 forward)
        /// </summary>
        public Vector2 InputDirection
        {
            get { return inputDirection; }
        }

        /// <summary>
        /// Gets whether the joystick is currently being touched.
        /// </summary>
        public bool IsActive
        {
            get { return isActive; }
        }

        private void Start()
        {
            if (background == null)
            {
                Debug.LogWarning("VirtualJoystickUI: Background RectTransform not assigned");
            }

            if (handle == null)
            {
                Debug.LogWarning("VirtualJoystickUI: Handle RectTransform not assigned");
            }

            ResetJoystick();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (background == null || handle == null)
            {
                return;
            }

            isActive = true;
            startPosition = eventData.position;

            // Ensure background is visible
            if (background.gameObject != null)
            {
                background.gameObject.SetActive(true);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (background == null || handle == null)
            {
                return;
            }

            if (!isActive)
            {
                return;
            }

            // Calculate offset from start position
            Vector2 offset = eventData.position - startPosition;

            // Clamp offset to handle range
            offset = Vector2.ClampMagnitude(offset, handleRange);

            // Update handle position
            handle.anchoredPosition = offset;

            // Calculate normalized input direction
            inputDirection = (offset.magnitude > 0f) ? (offset / handleRange) : Vector2.zero;
            inputDirection = Vector2.ClampMagnitude(inputDirection, 1f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            ResetJoystick();
        }

        /// <summary>
        /// Resets the joystick to center position and clears input.
        /// </summary>
        private void ResetJoystick()
        {
            isActive = false;
            inputDirection = Vector2.zero;

            if (handle != null)
            {
                handle.anchoredPosition = Vector2.zero;
            }

            // Optionally hide background when not in use
            if (background != null && background.gameObject != null)
            {
                background.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Manually sets the joystick to inactive state (useful for cleanup).
        /// </summary>
        public void SetInactive()
        {
            ResetJoystick();
        }
    }
}
