using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SettingsMenu settingsMenu;

    [Header("Buttons")]
    [SerializeField] private Button continueButton;

    [Header("Scenes")]
    [SerializeField] private string gameSceneName = "Cinematics";
    [SerializeField] private string creditsSceneName = "CreditsScene";

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

        // Grey-out Continue when there is nothing to resume
        bool hasSave = SaveManager.Instance != null && SaveManager.Instance.HasSave();
        if (continueButton != null)
            continueButton.interactable = hasSave;
    }

    // ───── Button callbacks ─────

    /// <summary>Resume from the last save.</summary>
    public void ContinueGame()
    {
        if (SaveManager.Instance == null || !SaveManager.Instance.HasSave()) return;
        SaveManager.Instance.LoadGame();
    }

    /// <summary>Start a brand-new game (overwrites any existing save).</summary>
    public void NewGame()
    {


        if (SaveManager.Instance != null)
            SaveManager.Instance.DeleteSave();

        GameplayHintManager.ResetAllHints();

        if (InventoryManager.Instance != null)
            InventoryManager.Instance.ClearInventory();

        SceneManager.LoadScene(gameSceneName);

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
        SceneManager.LoadScene(creditsSceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
