using UnityEngine;

/// <summary>
/// Minimal controller for the Credits scene.
/// Wire the "Back to Menu" button to OnBackToMenu().
/// </summary>
public class CreditsUI : MonoBehaviour
{
    [SerializeField] private string mainMenuScene = "Main Menu";

    public void OnBackToMenu()
    {
        LoadingScreenController.LoadScene(mainMenuScene);
    }
}
