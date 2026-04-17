/*
 * =============================================================================
 *                       MAIN MENU CONTROLLER
 *                    (Resident Evil Style)
 * =============================================================================
 * 
 * PURPOSE:
 * Controls the main menu UI. Handles navigation between main menu panel and
 * settings panel. Provides Play, Multiplayer (placeholder), Settings, and Quit.
 * 
 * SETUP:
 * ------
 * 1. Attach to a manager object in the Main Menu scene
 * 2. Assign the main menu panel reference
 * 3. Assign the SettingsMenuController reference
 * 4. Set the game scene name to load
 * 
 * =============================================================================
 */

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class MainMenuController : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    
    [Header("Panels")]
    [Tooltip("Main menu panel with Play/Multiplayer/Settings/Quit buttons")]
    [SerializeField] private GameObject mainMenuPanel;
    
    [Tooltip("Reference to the SettingsMenuController (can be a child or separate prefab)")]
    [SerializeField] private SettingsMenuController settingsMenu;

    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button multiplayerButton;  // Placeholder
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;

    [Header("Scene Names")]
    [SerializeField] private string gameSceneName = "Main Scene";

    [Header("Optional - Title Animation")]
    [SerializeField] private CanvasGroup titleCanvasGroup;
    [SerializeField] private float titleFadeInDuration = 1.0f;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Start()
    {
        // Ensure proper state
        Time.timeScale = 1f;
        AudioListener.pause = false;
        
        // Show cursor
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        
        // Setup initial panel state
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
        
        // Setup button listeners
        SetupButtons();
        
        // Subscribe to settings back event
        if (settingsMenu != null)
        {
            settingsMenu.OnBackPressed.AddListener(OnSettingsBackPressed);
        }
        
        // Optional title fade in
        if (titleCanvasGroup != null)
        {
            StartCoroutine(FadeTitleIn());
        }
    }

    private void OnDestroy()
    {
        // Cleanup event subscription
        if (settingsMenu != null)
        {
            settingsMenu.OnBackPressed.RemoveListener(OnSettingsBackPressed);
        }
    }

    private void SetupButtons()
    {
        if (playButton != null)
            playButton.onClick.AddListener(OnPlayClicked);
        
        if (multiplayerButton != null)
            multiplayerButton.onClick.AddListener(OnMultiplayerClicked);
        
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsClicked);
        
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);
    }

    // =========================================================================
    // BUTTON HANDLERS
    // =========================================================================
    
    private void OnPlayClicked()
    {
        PlayUIClick();
        
        // Load the game scene with loading screen
        if (LoadingScreenController.Instance != null)
        {
            LoadingScreenController.LoadScene(gameSceneName);
        }
        else
        {
            SceneManager.LoadScene(gameSceneName);
        }
    }
    
    private void OnMultiplayerClicked()
    {
        PlayUIClick();
        
        // Placeholder - show coming soon message or open multiplayer UI
        Debug.Log("[MainMenuController] Multiplayer clicked - not implemented yet");
        
        // You could show a popup here:
        // ShowPopup("Çok Oyunculu mod yakında geliyor!");
    }
    
    private void OnSettingsClicked()
    {
        PlayUIClick();
        
        if (settingsMenu == null)
        {
            Debug.LogWarning("[MainMenuController] SettingsMenuController not assigned!");
            return;
        }
        
        // Hide main menu
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        // Open settings
        settingsMenu.Open();
    }
    
    private void OnQuitClicked()
    {
        PlayUIClick();
        
        // Show confirmation dialog
        ConfirmationDialog.ShowQuitConfirmation(ConfirmQuit);
    }
    
    private void ConfirmQuit()
    {
        Debug.Log("[MainMenuController] Quitting game...");
        
        Application.Quit();
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
        #endif
    }

    // =========================================================================
    // EVENT HANDLERS
    // =========================================================================
    
    private void OnSettingsBackPressed()
    {
        // Settings closed, show main menu again
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================
    
    private void PlayUIClick()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayUIClick();
    }
    
    private System.Collections.IEnumerator FadeTitleIn()
    {
        titleCanvasGroup.alpha = 0f;
        
        float elapsed = 0f;
        while (elapsed < titleFadeInDuration)
        {
            elapsed += Time.deltaTime;
            titleCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / titleFadeInDuration);
            yield return null;
        }
        
        titleCanvasGroup.alpha = 1f;
    }
}

