using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PauseManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseRoot;       // fullscreen overlay with Continue/Settings/Quit
    [SerializeField] private GameObject settingsPanel;   // reuse your Settings panel prefab
    [SerializeField] private SettingsMenu settingsMenu;  // your existing script on settings panel

    [Header("Scenes")]
    [SerializeField] private string mainMenuSceneName = "Main Menu";

    [Header("Player Control (optional)")]
    [Tooltip("Any scripts you want disabled while paused (e.g., look/move, PlayerInteractor, etc.).")]
    [SerializeField] private MonoBehaviour[] disableOnPause;

    [Header("Cursor")]
    [SerializeField] private CursorLockMode lockModeWhilePlaying = CursorLockMode.Locked;

    public static bool IsPaused { get; private set; }

    void Awake()
    {
        if (pauseRoot) pauseRoot.SetActive(false);
        if (settingsPanel) settingsPanel.SetActive(false);
        Resume_Internal(); // ensure correct timescale & cursor on scene load
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            if (IsPaused) OnContinue();
            else Pause_Internal();
        }
    }
    

    // --- Buttons ---
    public void OnContinue()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
        if (pauseRoot) pauseRoot.SetActive(false);
        Resume_Internal();
    }

    public void OnOpenSettings()
    {
        if (settingsPanel == null) return;
        if (pauseRoot) pauseRoot.SetActive(false);
        settingsPanel.SetActive(true);
        settingsMenu?.BeginEditing();
    }

    public void OnCloseSettings()
    {
        settingsMenu?.CancelEditing();
        if (settingsPanel) settingsPanel.SetActive(false);
        if (pauseRoot) pauseRoot.SetActive(true);
    }

    public void OnQuitToMenu()
    {
        // Persist progress before leaving the game scene
        if (SaveManager.Instance != null)
            SaveManager.Instance.SaveGame();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        SceneManager.LoadScene(mainMenuSceneName);
    }


    // --- Core pause/resume ---
    private void Pause_Internal()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        if (pauseRoot) pauseRoot.SetActive(true);

        foreach (var mb in disableOnPause) if (mb) mb.enabled = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void Resume_Internal()
    {
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        foreach (var mb in disableOnPause) if (mb) mb.enabled = true;

        Cursor.visible = false;
        Cursor.lockState = lockModeWhilePlaying;
    }

    
}
