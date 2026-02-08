using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BattleCarSumo.Data;
using BattleCarSumo.Network;

namespace BattleCarSumo.UI
{
    /// <summary>
    /// Main menu and matchmaking UI for the Battle Car Sumo game.
    /// Handles weight class selection, hosting/joining matches, and status display.
    /// Flow: Main Menu → Select Weight Class → Host/Join → Waiting → Match Start
    /// </summary>
    public class MatchmakingUI : MonoBehaviour
    {
        [SerializeField]
        private GameObject mainMenuPanel;

        [SerializeField]
        private GameObject hostPanel;

        [SerializeField]
        private GameObject joinPanel;

        [SerializeField]
        private GameObject waitingPanel;

        [SerializeField]
        private TMP_InputField joinCodeInput;

        [SerializeField]
        private TextMeshProUGUI joinCodeDisplayText;

        [SerializeField]
        private TextMeshProUGUI statusText;

        [SerializeField]
        private Button hostButton;

        [SerializeField]
        private Button joinButton;

        [SerializeField]
        private Button cancelButton;

        [SerializeField]
        private Button copyCodeButton;

        [SerializeField]
        private Button[] weightClassButtons;

        private MatchmakingManager matchmakingManager;
        private RelayManager relayManager;
        private WeightClass selectedWeightClass = WeightClass.Middle;
        private string currentJoinCode;

        private void OnEnable()
        {
            FindNetworkManagers();
            SubscribeToEvents();

            // Initialize UI state
            ShowPanel("MainMenu");
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private void FindNetworkManagers()
        {
            if (matchmakingManager == null)
            {
                matchmakingManager = FindFirstObjectByType<MatchmakingManager>();
            }

            if (relayManager == null)
            {
                relayManager = FindFirstObjectByType<RelayManager>();
            }

            if (matchmakingManager == null)
            {
                Debug.LogWarning("MatchmakingUI: MatchmakingManager not found in scene");
            }

            if (relayManager == null)
            {
                Debug.LogWarning("MatchmakingUI: RelayManager not found in scene");
            }
        }

        private void SubscribeToEvents()
        {
            if (hostButton != null)
            {
                hostButton.onClick.AddListener(OnHostClicked);
            }

            if (joinButton != null)
            {
                joinButton.onClick.AddListener(OnJoinClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.AddListener(OnCancelClicked);
            }

            if (copyCodeButton != null)
            {
                copyCodeButton.onClick.AddListener(CopyJoinCodeToClipboard);
            }

            if (weightClassButtons != null)
            {
                for (int i = 0; i < weightClassButtons.Length; i++)
                {
                    int index = i;
                    if (weightClassButtons[i] != null)
                    {
                        weightClassButtons[i].onClick.AddListener(() => SelectWeightClass((WeightClass)index));
                    }
                }
            }

            if (matchmakingManager != null)
            {
                matchmakingManager.OnMatchCreated += HandleMatchCreated;
                matchmakingManager.OnMatchJoined += HandleMatchJoined;
                matchmakingManager.OnMatchFailed += HandleMatchFailed;
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (hostButton != null)
            {
                hostButton.onClick.RemoveListener(OnHostClicked);
            }

            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(OnJoinClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(OnCancelClicked);
            }

            if (copyCodeButton != null)
            {
                copyCodeButton.onClick.RemoveListener(CopyJoinCodeToClipboard);
            }

            if (matchmakingManager != null)
            {
                matchmakingManager.OnMatchCreated -= HandleMatchCreated;
                matchmakingManager.OnMatchJoined -= HandleMatchJoined;
                matchmakingManager.OnMatchFailed -= HandleMatchFailed;
            }
        }

        /// <summary>
        /// Called when the Host button is clicked.
        /// </summary>
        private void OnHostClicked()
        {
            if (matchmakingManager == null)
            {
                Debug.LogError("MatchmakingUI: MatchmakingManager not available");
                return;
            }

            Debug.Log($"MatchmakingUI: Hosting match with weight class {selectedWeightClass}");
            matchmakingManager.HostMatch(selectedWeightClass);
            ShowPanel("Waiting");
        }

        /// <summary>
        /// Called when the Join button is clicked.
        /// </summary>
        private void OnJoinClicked()
        {
            if (matchmakingManager == null)
            {
                Debug.LogError("MatchmakingUI: MatchmakingManager not available");
                return;
            }

            if (joinCodeInput == null || string.IsNullOrEmpty(joinCodeInput.text))
            {
                if (statusText != null)
                {
                    statusText.text = "코드를 입력해주세요";
                }
                return;
            }

            string joinCode = joinCodeInput.text.ToUpper();
            Debug.Log($"MatchmakingUI: Joining match with code {joinCode}");
            matchmakingManager.JoinMatch(joinCode, selectedWeightClass);
            ShowPanel("Waiting");
        }

        /// <summary>
        /// Called when the Cancel button is clicked.
        /// </summary>
        private void OnCancelClicked()
        {
            if (matchmakingManager != null)
            {
                matchmakingManager.CancelMatchmaking();
            }

            ShowPanel("MainMenu");
        }

        /// <summary>
        /// Selects a weight class and updates button visuals.
        /// </summary>
        private void SelectWeightClass(WeightClass weightClass)
        {
            selectedWeightClass = weightClass;

            // Update button visuals to show selection
            if (weightClassButtons != null)
            {
                for (int i = 0; i < weightClassButtons.Length; i++)
                {
                    if (weightClassButtons[i] != null)
                    {
                        ColorBlock colors = weightClassButtons[i].colors;

                        if (i == (int)weightClass)
                        {
                            // Highlight selected button
                            colors.normalColor = Color.yellow;
                            colors.selectedColor = Color.yellow;
                        }
                        else
                        {
                            // Reset unselected buttons
                            colors.normalColor = Color.white;
                            colors.selectedColor = Color.white;
                        }

                        weightClassButtons[i].colors = colors;
                    }
                }
            }

            Debug.Log($"MatchmakingUI: Selected weight class {weightClass}");
        }

        /// <summary>
        /// Shows a specific UI panel and hides others.
        /// </summary>
        private void ShowPanel(string panelName)
        {
            // Hide all panels
            SetPanelActive(mainMenuPanel, false);
            SetPanelActive(hostPanel, false);
            SetPanelActive(joinPanel, false);
            SetPanelActive(waitingPanel, false);

            // Show requested panel
            switch (panelName.ToLower())
            {
                case "mainmenu":
                    SetPanelActive(mainMenuPanel, true);
                    break;

                case "host":
                    SetPanelActive(hostPanel, true);
                    break;

                case "join":
                    SetPanelActive(joinPanel, true);
                    break;

                case "waiting":
                    SetPanelActive(waitingPanel, true);
                    break;

                default:
                    Debug.LogWarning($"MatchmakingUI: Unknown panel {panelName}");
                    break;
            }
        }

        /// <summary>
        /// Activates or deactivates a panel.
        /// </summary>
        private void SetPanelActive(GameObject panel, bool active)
        {
            if (panel != null)
            {
                panel.SetActive(active);
            }
        }

        /// <summary>
        /// Called when a match is created (player is hosting).
        /// </summary>
        private void HandleMatchCreated(string joinCode)
        {
            currentJoinCode = joinCode;

            if (joinCodeDisplayText != null)
            {
                joinCodeDisplayText.text = $"코드: {joinCode}";
            }

            if (statusText != null)
            {
                statusText.text = "상대방을 기다리는 중...";
            }

            Debug.Log($"MatchmakingUI: Match created with join code {joinCode}");
        }

        /// <summary>
        /// Called when a match is joined (player is joining or opponent found).
        /// </summary>
        private void HandleMatchJoined()
        {
            if (statusText != null)
            {
                statusText.text = "상대방을 찾았습니다!\n매치를 시작합니다...";
            }

            Debug.Log("MatchmakingUI: Match joined, starting...");

            // Transition to game scene after a brief delay
            StartCoroutine(TransitionToGameCoroutine());
        }

        /// <summary>
        /// Called when matchmaking fails.
        /// </summary>
        private void HandleMatchFailed(string error)
        {
            Debug.LogError($"MatchmakingUI: Match failed with error: {error}");

            if (statusText != null)
            {
                statusText.text = $"오류: {error}";
            }

            // Return to main menu after 3 seconds
            StartCoroutine(ReturnToMainMenuCoroutine(3f));
        }

        /// <summary>
        /// Copies the join code to the system clipboard.
        /// </summary>
        private void CopyJoinCodeToClipboard()
        {
            if (string.IsNullOrEmpty(currentJoinCode))
            {
                Debug.LogWarning("MatchmakingUI: No join code to copy");
                return;
            }

            TextEditor te = new TextEditor();
            te.text = currentJoinCode;
            te.SelectAll();
            te.Copy();

            if (statusText != null)
            {
                statusText.text = "코드가 복사되었습니다!";
            }

            Debug.Log($"MatchmakingUI: Copied join code to clipboard: {currentJoinCode}");
        }

        /// <summary>
        /// Transitions to the game scene.
        /// </summary>
        private System.Collections.IEnumerator TransitionToGameCoroutine()
        {
            yield return new WaitForSeconds(2f);

            // Load the main game scene
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
        }

        /// <summary>
        /// Returns to the main menu after a delay.
        /// </summary>
        private System.Collections.IEnumerator ReturnToMainMenuCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            ShowPanel("MainMenu");
        }
    }
}
