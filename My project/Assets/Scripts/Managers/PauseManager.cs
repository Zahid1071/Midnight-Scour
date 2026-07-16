using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PauseManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject pauseRoot;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SettingsMenu settingsMenu;

    [Header("Scenes")]
    [SerializeField] private string mainMenuSceneName = "Main Menu";

    [Header("Player Control (optional)")]
    [SerializeField] private MonoBehaviour[] disableOnPause;

    [Header("Cursor")]
    [SerializeField] private CursorLockMode lockModeWhilePlaying = CursorLockMode.Locked;

    public static bool IsPaused { get; private set; }

    GameObject _mainButtons;

    void OnDestroy()
    {
        if (IsPaused)
            PlayerControlGate.Release(PlayerControlGate.ReasonPause);
    }

    void Awake()
    {
        ResolvePauseUi();
        WireSettingsBackButton();

        if (pauseRoot) pauseRoot.SetActive(false);
        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons) _mainButtons.SetActive(true);
        Resume_Internal();
    }

    void LateUpdate()
    {
        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
            return;

        var demoEnd = DemoEndScreenUI.Instance;
        if (demoEnd != null && demoEnd.IsShowing)
            return;

        if (IsPaused)
        {
            if (IsSettingsOpen())
                OnCloseSettings();
            else
                OnContinue();
        }
        else
        {
            Pause_Internal();
        }
    }

    void ResolvePauseUi()
    {
        if (pauseRoot == null)
            return;

        var buttons = pauseRoot.transform.Find("Main Buttons");
        if (buttons != null)
            _mainButtons = buttons.gameObject;

        if (settingsPanel == null)
        {
            foreach (var t in pauseRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Settings  Panel" || t.name == "Settings  Panel Pause")
                {
                    settingsPanel = t.gameObject;
                    break;
                }
            }
        }

        if (settingsMenu == null && settingsPanel != null)
            settingsMenu = settingsPanel.GetComponent<SettingsMenu>()
                ?? settingsPanel.GetComponentInChildren<SettingsMenu>(true);
    }

    void WireSettingsBackButton()
    {
        if (settingsPanel == null)
            return;

        foreach (var button in settingsPanel.GetComponentsInChildren<Button>(true))
        {
            if (button == null || button.gameObject.name != "Back Button")
                continue;

            button.onClick.RemoveListener(OnCloseSettings);
            button.onClick.AddListener(OnCloseSettings);
            break;
        }
    }

    public void OnContinue()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons) _mainButtons.SetActive(true);
        if (pauseRoot) pauseRoot.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        Resume_Internal();
    }

    public void OnOpenSettings()
    {
        if (settingsPanel == null)
        {
            ResolvePauseUi();
            if (settingsPanel == null) return;
        }

        HudManager.Instance?.ClearSubtitle();

        if (_mainButtons != null)
            _mainButtons.SetActive(false);
        else if (pauseRoot != null
                 && (settingsPanel == null || !settingsPanel.transform.IsChildOf(pauseRoot.transform)))
            pauseRoot.SetActive(false);

        if (pauseRoot != null
            && settingsPanel != null
            && settingsPanel.transform.IsChildOf(pauseRoot.transform)
            && !pauseRoot.activeSelf)
            pauseRoot.SetActive(true);

        settingsPanel.SetActive(true);
        settingsMenu?.BeginEditing();
    }

    public void OnCloseSettings()
    {
        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons != null)
            _mainButtons.SetActive(true);
        else if (pauseRoot)
            pauseRoot.SetActive(true);

        settingsMenu?.CancelEditing();
    }

    public void SaveGameFromPauseMenu()
    {
        SaveManager.Instance?.SaveGame();
    }

    public void OnQuitToMenu()
    {
        if (SaveManager.Instance != null && GameSettingsManager.IsAutoSavePersistenceEnabled())
            SaveManager.Instance.SaveGame();

        PlayerControlGate.Clear();

        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        LoadingScreenController.LoadScene(mainMenuSceneName);
    }

    void Pause_Internal()
    {
        IsPaused = true;
        PlayerControlGate.Acquire(PlayerControlGate.ReasonPause);
        Time.timeScale = 0f;
        AudioListener.pause = true;

        HudManager.Instance?.ClearSubtitle();

        if (settingsPanel) settingsPanel.SetActive(false);
        if (_mainButtons) _mainButtons.SetActive(true);
        if (pauseRoot) pauseRoot.SetActive(true);

        foreach (var mb in disableOnPause)
            if (mb) mb.enabled = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    void Resume_Internal()
    {
        IsPaused = false;
        PlayerControlGate.Release(PlayerControlGate.ReasonPause);
        Time.timeScale = 1f;
        AudioListener.pause = false;

        foreach (var mb in disableOnPause)
            if (mb) mb.enabled = true;

        Cursor.visible = false;
        Cursor.lockState = lockModeWhilePlaying;
    }

    bool IsSettingsOpen() =>
        settingsPanel != null && settingsPanel.activeSelf && settingsPanel.activeInHierarchy;
}
