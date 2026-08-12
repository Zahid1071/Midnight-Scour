using UnityEngine;

public class MainMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SettingsMenu settingsMenu;
    [SerializeField] private GameObject creditsPanel;

    [Header("Scenes")]
    [SerializeField] private string gameSceneName = "Cinematics";

    void Awake() => ResolveReferences();

#if UNITY_EDITOR
    void OnValidate() => ResolveReferences();
#endif

    void ResolveReferences()
    {
        if (settingsMenu == null)
            settingsMenu = transform.root.GetComponentInChildren<SettingsMenu>(true);

        if (settingsMenu != null)
            settingsPanel = settingsMenu.gameObject;
        else if (settingsPanel != null)
            settingsMenu = settingsPanel.GetComponent<SettingsMenu>();

        if (mainMenuPanel == null)
        {
            var buttons = transform.Find("Buttons");
            if (buttons != null)
                mainMenuPanel = buttons.gameObject;
        }
    }

    private void Start()
    {
        ResolveReferences();

        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
        if (settingsPanel) settingsPanel.SetActive(false);

        // Safety net: PlayerControlGate is a static lock that persists across scene loads
        // (only a full process restart resets it). If a previous gameplay session ended
        // abnormally (e.g. an exception mid-capture/jumpscare) without releasing a lock
        // such as "captured", it would otherwise stay stuck for every future playthrough
        // in this process, silently freezing the NPC on its next capture attempt.
        PlayerControlGate.Clear();
        LockpickExtraLife.Clear();
    }

    // ───── Button callbacks ─────

    /// <summary>Start a brand-new game.</summary>
    public void NewGame()
    {
        PlayerControlGate.Clear();
        GameplayHintManager.ResetAllHints();
        InventoryManager.ResetForNewGame();

        // Async load behind the loading screen (also warms up shaders there)
        // instead of an instant load that stalls on first-time shader compilation.
        LoadingScreenController.LoadScene(gameSceneName);
    }

    /// <summary>Placeholder — wire to your online / multiplayer flow later.</summary>
    public void OnOnline()
    {
        Debug.Log("[MainMenu] Online — not implemented yet.");
    }

    public void OpenSettings()
    {
        if (settingsPanel == null || settingsMenu == null) return;
        mainMenuPanel.SetActive(false);
        settingsPanel.SetActive(true);
        settingsMenu.BeginEditing();
    }

    public void CloseSettingsWithoutApply()
    {
        if (settingsMenu != null)
            settingsMenu.CancelEditing();
        if (settingsPanel != null)
            settingsPanel.SetActive(false);
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
    }

    public void OpenCredits()
    {
        if (creditsPanel == null) return;
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        creditsPanel.SetActive(true);
    }

    public void CloseCredits()
    {
        if (creditsPanel != null) creditsPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
