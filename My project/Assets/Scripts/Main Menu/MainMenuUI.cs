using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;   // Play / Multiplayer / Settings / Quit
    [SerializeField] private GameObject settingsPanel;   // The white settings card in your screenshot
    [SerializeField] private SettingsMenu settingsMenu;  // Script below, on the settings panel

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Main Scene"; // change to your scene name

    private void Start()
    {
        mainMenuPanel.SetActive(true);
        settingsPanel.SetActive(false);
    }

    public void PlayGame() => SceneManager.LoadScene(gameSceneName);

    public void OpenSettings()
    {
        mainMenuPanel.SetActive(false);
        settingsPanel.SetActive(true);
        settingsMenu.BeginEditing(); // load last saved values into the UI
    }

    // Called by the Back button **inside** Settings
    public void CloseSettingsWithoutApply()
    {
        settingsMenu.CancelEditing(); // throw away unsaved changes
        settingsPanel.SetActive(false);
        mainMenuPanel.SetActive(true);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
