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

    private void Start()
    {
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
        settingsMenu.CancelEditing();
        settingsPanel.SetActive(false);
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
