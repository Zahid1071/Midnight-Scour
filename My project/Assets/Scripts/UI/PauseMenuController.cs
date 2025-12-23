/*
 * =============================================================================
 *                       PAUSE MENU CONTROLLER
 *                    (Resident Evil Style)
 * =============================================================================
 * 
 * PURPOSE:
 * Handles the in-game pause/ESC menu. Shows when ESC is pressed during gameplay,
 * allows player to resume, access settings, or quit to main menu.
 * 
 * RESPONSIBILITIES:
 * -----------------
 * - Detect ESC key press
 * - Toggle pause state (Time.timeScale = 0/1)
 * - Show/hide pause panel
 * - Open SettingsMenuController when Settings is clicked
 * - Handle Resume and Quit buttons
 * 
 * FOR OTHER TEAM MEMBERS:
 * -----------------------
 * You can interact with this controller via:
 * 
 *     PauseMenuController.IsPaused              // Check if game is paused
 *     PauseMenuController.Instance.ShowPauseMenu()  // Force show pause menu
 *     PauseMenuController.Instance.HidePauseMenu()  // Force hide and resume
 * 
 * The pause menu does NOT modify settings directly - it delegates to SettingsMenuController.
 * Time.timeScale is managed ONLY here, not in SettingsMenuController.
 * 
 * SETUP:
 * ------
 * 1. Create a Canvas with Screen Space - Overlay
 * 2. Add this script to a manager object
 * 3. Create pause panel with Resume/Settings/Quit buttons
 * 4. Assign SettingsMenuController reference (can be same instance as main menu)
 * 5. Assign any player scripts to disable while paused (e.g. movement, camera)
 * 
 * =============================================================================
 */

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class PauseMenuController : MonoBehaviour
{
    // =========================================================================
    // SINGLETON
    // =========================================================================
    
    public static PauseMenuController Instance { get; private set; }
    
    /// <summary>
    /// Returns true if the game is currently paused.
    /// Other systems can check this to skip updates, etc.
    /// </summary>
    public static bool IsPaused { get; private set; }

    // =========================================================================
    // INSPECTOR REFERENCES
    // =========================================================================
    
    [Header("UI Panels")]
    [Tooltip("The main pause panel with Resume/Settings/Quit buttons")]
    [SerializeField] private GameObject pausePanel;
    
    [Tooltip("Optional dark overlay behind pause menu")]
    [SerializeField] private GameObject darkOverlay;
    
    [Tooltip("CanvasGroup for fade animation (optional)")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Settings Menu")]
    [Tooltip("Reference to the SettingsMenuController (can be shared with main menu)")]
    [SerializeField] private SettingsMenuController settingsMenu;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;    // Devam Et
    [SerializeField] private Button settingsButton;  // Ayarlar
    [SerializeField] private Button quitButton;      // Çıkış

    [Header("Scene Names")]
    [SerializeField] private string mainMenuSceneName = "Main Menu";

    [Header("Player Control")]
    [Tooltip("Scripts to disable while paused (e.g. movement, camera look)")]
    [SerializeField] private MonoBehaviour[] disableOnPause;
    
    [Header("UI to Hide While Paused")]
    [Tooltip("GameObjects to hide when paused (e.g. Inventory Canvas, HUD)")]
    [SerializeField] private GameObject[] hideOnPause;
    
    [Tooltip("If true, automatically finds and hides InventoryUI")]
    [SerializeField] private bool autoHideInventory = true;

    [Header("Cursor Settings")]
    [SerializeField] private CursorLockMode cursorModeWhilePlaying = CursorLockMode.Locked;
    [SerializeField] private bool hideCursorWhilePlaying = true;

    [Header("Audio")]
    [Tooltip("Pause all audio while game is paused")]
    [SerializeField] private bool pauseAudio = true;

    // =========================================================================
    // PRIVATE STATE
    // =========================================================================
    
    private bool _settingsWasOpenedFromPause = false;
    private bool _inventoryWasActive = false;

    // =========================================================================
    // UNITY LIFECYCLE
    // =========================================================================
    
    private void Awake()
    {
        // Singleton (but don't persist across scenes - each gameplay scene should have its own)
        if (Instance != null && Instance != this)
        {
            // Only destroy if in the same scene
            if (Instance.gameObject.scene == gameObject.scene)
            {
                Destroy(gameObject);
                return;
            }
        }
        Instance = this;
        
        // Setup button listeners
        SetupButtons();
        
        // Subscribe to settings back event
        if (settingsMenu != null)
        {
            settingsMenu.OnBackPressed.AddListener(OnSettingsBackPressed);
        }
        
        // Ensure everything is hidden at start
        if (pausePanel != null) pausePanel.SetActive(false);
        if (darkOverlay != null) darkOverlay.SetActive(false);
        
        // Reset pause state on scene load
        IsPaused = false;
        SetGameplayState(true);
    }

    private void OnDestroy()
    {
        // Cleanup singleton
        if (Instance == this)
            Instance = null;
        
        // Unsubscribe from settings
        if (settingsMenu != null)
        {
            settingsMenu.OnBackPressed.RemoveListener(OnSettingsBackPressed);
        }
        
        // Ensure time is restored if this object is destroyed while paused
        if (IsPaused)
        {
            Time.timeScale = 1f;
            if (pauseAudio) AudioListener.pause = false;
            IsPaused = false;
        }
    }

    private void Update()
    {
        // Check for ESC key
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            HandleEscapePressed();
        }
    }

    private void SetupButtons()
    {
        if (resumeButton != null)
            resumeButton.onClick.AddListener(OnResumeClicked);
        
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsClicked);
        
        if (quitButton != null)
            quitButton.onClick.AddListener(OnQuitClicked);
    }

    // =========================================================================
    // PUBLIC API
    // =========================================================================
    
    /// <summary>
    /// Shows the pause menu and pauses the game.
    /// Can be called from other scripts to force pause.
    /// </summary>
    public void ShowPauseMenu()
    {
        if (IsPaused) return;
        
        IsPaused = true;
        
        // Stop time
        Time.timeScale = 0f;
        if (pauseAudio) AudioListener.pause = true;
        
        // Disable player controls
        SetGameplayState(false);
        
        // Hide gameplay UI (inventory, HUD, etc.)
        //HideGameplayUI();
        
        // Show cursor
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        
        // Show UI
        if (darkOverlay != null) darkOverlay.SetActive(true);
        if (pausePanel != null) pausePanel.SetActive(true);
        
        // Animate if we have a canvas group
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            StartCoroutine(FadeCanvasGroup(canvasGroup, 0f, 1f, 0.2f));
        }
        
        PlayUIClick();
        Debug.Log("[PauseMenuController] Game paused");
    }
    
    /// <summary>
    /// Hides the pause menu and resumes the game.
    /// Can be called from other scripts to force resume.
    /// </summary>
    public void HidePauseMenu()
    {
        if (!IsPaused) return;
        
        IsPaused = false;
        
        // Close settings if open
        if (settingsMenu != null && settingsMenu.IsOpen)
        {
            settingsMenu.Close();
        }
        _settingsWasOpenedFromPause = false;
        
        // Resume time
        Time.timeScale = 1f;
        if (pauseAudio) AudioListener.pause = false;
        
        // Re-enable player controls
        SetGameplayState(true);
        
        // Restore gameplay UI (inventory, HUD, etc.)
        RestoreGameplayUI();
        
        // Hide cursor (for FPS gameplay)
        Cursor.visible = !hideCursorWhilePlaying;
        Cursor.lockState = cursorModeWhilePlaying;
        
        // Hide UI
        if (pausePanel != null) pausePanel.SetActive(false);
        if (darkOverlay != null) darkOverlay.SetActive(false);
        
        Debug.Log("[PauseMenuController] Game resumed");
    }

    /// <summary>
    /// Toggles pause state.
    /// </summary>
    public void TogglePause()
    {
        if (IsPaused)
            HidePauseMenu();
            
        else
            ShowPauseMenu();
    }

    // =========================================================================
    // BUTTON HANDLERS
    // =========================================================================
    
    private void OnResumeClicked()
    {
        PlayUIClick();
        HidePauseMenu();
    }
    
    private void OnSettingsClicked()
    {
        PlayUIClick();
        
        if (settingsMenu == null)
        {
            Debug.LogWarning("[PauseMenuController] SettingsMenuController not assigned!");
            return;
        }
        
        _settingsWasOpenedFromPause = true;
        
        // Hide pause panel but keep dark overlay
        if (pausePanel != null) pausePanel.SetActive(false);
        
        // Open settings
        settingsMenu.Open();
    }
    
    private void OnQuitClicked()
    {
        PlayUIClick();
        
        // Show confirmation dialog
        ConfirmationDialog.ShowReturnToMenuConfirmation(ConfirmQuit);
    }
    
    private void ConfirmQuit()
    {
        // Restore normal state before scene change
        Time.timeScale = 1f;
        if (pauseAudio) AudioListener.pause = false;
        IsPaused = false;
        
        // Ensure cursor is visible for menu
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        
        // Load main menu with loading screen
        if (LoadingScreenController.Instance != null)
        {
            LoadingScreenController.LoadScene(mainMenuSceneName);
        }
        else
        {
            SceneManager.LoadScene(mainMenuSceneName);
        }
    }

    // =========================================================================
    // EVENT HANDLERS
    // =========================================================================
    
    private void HandleEscapePressed()
    {
        // If settings is open, let it handle ESC (close settings, show pause menu)
        if (settingsMenu != null && settingsMenu.IsOpen)
        {
            settingsMenu.Close();
            OnSettingsBackPressed();
            return;
        }
        
        // Otherwise toggle pause
        TogglePause();
    }
    
    private void OnSettingsBackPressed()
    {
        // Called when Back is pressed in settings
        if (_settingsWasOpenedFromPause)
        {
            _settingsWasOpenedFromPause = false;
            
            // Show pause panel again
            if (pausePanel != null) pausePanel.SetActive(true);
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================
    
    private void SetGameplayState(bool enabled)
    {
        foreach (var mb in disableOnPause)
        {
            if (mb != null)
                mb.enabled = enabled;
        }
    }
    
    private void HideGameplayUI()
    {
        // Hide manually assigned GameObjects
        foreach (var go in hideOnPause)
        {
            if (go != null)
                go.SetActive(false);
        }
        
        // Auto-hide InventoryUI if enabled
        if (autoHideInventory && InventoryUI.Instance != null)
        {
            // Just hide the InventoryUI GameObject itself, not the parent canvas
            // (hiding parent canvas would hide other UI elements on the same canvas)
            _inventoryWasActive = InventoryUI.Instance.gameObject.activeSelf;
            InventoryUI.Instance.gameObject.SetActive(false);
        }
    }
    
    private void RestoreGameplayUI()
    {
        // Restore manually assigned GameObjects
        foreach (var go in hideOnPause)
        {
            if (go != null)
                go.SetActive(true);
        }
        
        // Restore InventoryUI if it was hidden
        if (autoHideInventory && InventoryUI.Instance != null && _inventoryWasActive)
        {
            InventoryUI.Instance.gameObject.SetActive(true);
        }
        
        _inventoryWasActive = false;
    }
    
    private void PlayUIClick()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayUIClick();
    }
    
    private System.Collections.IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        float elapsed = 0f;
        cg.alpha = from;
        
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        
        cg.alpha = to;
    }
}

