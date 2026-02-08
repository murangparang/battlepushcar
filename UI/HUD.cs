using UnityEngine;
using TMPro;
using BattleCarSumo.Data;
using BattleCarSumo.GameLoop;

namespace BattleCarSumo.UI
{
    /// <summary>
    /// Main in-game HUD displaying timer, score, round information, and game state messages.
    /// Manages visibility and interaction of action buttons, joystick, and other UI elements
    /// based on the current game state.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI timerText;

        [SerializeField]
        private TextMeshProUGUI scoreText;

        [SerializeField]
        private TextMeshProUGUI roundText;

        [SerializeField]
        private TextMeshProUGUI stateMessageText;

        [SerializeField]
        private ActionButtonUI[] actionButtons;

        [SerializeField]
        private VirtualJoystickUI joystick;

        [SerializeField]
        private CanvasGroup edgeWarningVisual;

        [SerializeField]
        private TextMeshProUGUI countdownText;

        [SerializeField]
        private CanvasGroup countdownPanel;

        private GameStateManager gameStateManager;
        private Coroutine countdownCoroutine;

        private void OnEnable()
        {
            FindGameStateManager();

            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged += HandleGameStateChanged;
            }
        }

        private void OnDisable()
        {
            if (gameStateManager != null)
            {
                gameStateManager.OnStateChanged -= HandleGameStateChanged;
            }

            if (countdownCoroutine != null)
            {
                StopCoroutine(countdownCoroutine);
                countdownCoroutine = null;
            }
        }

        private void FindGameStateManager()
        {
            if (gameStateManager != null)
            {
                return;
            }

            gameStateManager = FindFirstObjectByType<GameStateManager>();

            if (gameStateManager == null)
            {
                Debug.LogWarning("HUD: GameStateManager not found in scene");
            }
        }

        /// <summary>
        /// Updates the timer display in MM:SS format.
        /// </summary>
        public void UpdateTimer(float remainingTime)
        {
            if (timerText == null)
            {
                return;
            }

            int minutes = Mathf.FloorToInt(remainingTime / 60f);
            int seconds = Mathf.FloorToInt(remainingTime % 60f);

            timerText.text = string.Format("{0:D2}:{1:D2}", minutes, seconds);
        }

        /// <summary>
        /// Updates the score display showing both players' scores.
        /// </summary>
        public void UpdateScore(int p1Score, int p2Score)
        {
            if (scoreText == null)
            {
                return;
            }

            scoreText.text = string.Format("P1: {0} - P2: {1}", p1Score, p2Score);
        }

        /// <summary>
        /// Updates the round display.
        /// </summary>
        public void UpdateRound(int currentRound, int totalRounds = 3)
        {
            if (roundText == null)
            {
                return;
            }

            roundText.text = string.Format("Round {0}/{1}", currentRound, totalRounds);
        }

        /// <summary>
        /// Displays a centered state message on screen.
        /// Examples: "ROUND 1", "FIGHT!", "KO!", "YOU WIN!", "MATCH DRAW"
        /// </summary>
        public void ShowStateMessage(string message)
        {
            if (stateMessageText == null)
            {
                return;
            }

            stateMessageText.text = message;
            stateMessageText.gameObject.SetActive(true);
        }

        /// <summary>
        /// Hides the state message.
        /// </summary>
        public void HideStateMessage()
        {
            if (stateMessageText != null)
            {
                stateMessageText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Shows a large countdown timer (3, 2, 1, GO!).
        /// </summary>
        public void ShowCountdown(int seconds)
        {
            if (countdownCoroutine != null)
            {
                StopCoroutine(countdownCoroutine);
            }

            countdownCoroutine = StartCoroutine(CountdownRoutine(seconds));
        }

        /// <summary>
        /// Shows or hides the edge warning visual (red border flash).
        /// </summary>
        public void SetEdgeWarningActive(bool active)
        {
            if (edgeWarningVisual != null)
            {
                edgeWarningVisual.alpha = active ? 0.5f : 0f;
            }
        }

        /// <summary>
        /// Sets all action buttons to interactable or non-interactable.
        /// </summary>
        public void SetActionButtonsInteractable(bool interactable)
        {
            if (actionButtons == null || actionButtons.Length == 0)
            {
                return;
            }

            foreach (var button in actionButtons)
            {
                if (button != null)
                {
                    button.SetInteractable(interactable);
                }
            }
        }

        /// <summary>
        /// Shows or hides the virtual joystick.
        /// </summary>
        public void SetJoystickActive(bool active)
        {
            if (joystick != null)
            {
                joystick.gameObject.SetActive(active);
            }
        }

        /// <summary>
        /// Shows or hides the timer display.
        /// </summary>
        public void SetTimerActive(bool active)
        {
            if (timerText != null)
            {
                timerText.gameObject.SetActive(active);
            }
        }

        /// <summary>
        /// Shows or hides the score display.
        /// </summary>
        public void SetScoreActive(bool active)
        {
            if (scoreText != null)
            {
                scoreText.gameObject.SetActive(active);
            }
        }

        private void HandleGameStateChanged(GameState newState)
        {
            switch (newState)
            {
                case GameState.Playing:
                    SetActionButtonsInteractable(true);
                    SetJoystickActive(true);
                    SetTimerActive(true);
                    SetScoreActive(true);
                    HideStateMessage();
                    break;

                case GameState.Intermission:
                    SetActionButtonsInteractable(false);
                    SetJoystickActive(false);
                    SetTimerActive(true);
                    SetScoreActive(true);
                    break;

                case GameState.RoundEnd:
                    SetActionButtonsInteractable(false);
                    SetJoystickActive(false);
                    SetTimerActive(false);
                    break;

                case GameState.MatchEnd:
                    SetActionButtonsInteractable(false);
                    SetJoystickActive(false);
                    SetTimerActive(false);
                    break;

                case GameState.Countdown:
                    SetActionButtonsInteractable(false);
                    SetJoystickActive(false);
                    SetTimerActive(false);
                    break;

                case GameState.WaitingForPlayers:
                    SetActionButtonsInteractable(false);
                    SetJoystickActive(false);
                    SetTimerActive(false);
                    SetScoreActive(false);
                    break;
            }
        }

        private System.Collections.IEnumerator CountdownRoutine(int startSeconds)
        {
            if (countdownPanel != null)
            {
                countdownPanel.gameObject.SetActive(true);
            }

            for (int i = startSeconds; i > 0; i--)
            {
                if (countdownText != null)
                {
                    countdownText.text = i.ToString();
                }

                yield return new WaitForSeconds(1f);
            }

            if (countdownText != null)
            {
                countdownText.text = "GO!";
            }

            yield return new WaitForSeconds(0.5f);

            if (countdownPanel != null)
            {
                countdownPanel.gameObject.SetActive(false);
            }

            countdownCoroutine = null;
        }
    }
}
