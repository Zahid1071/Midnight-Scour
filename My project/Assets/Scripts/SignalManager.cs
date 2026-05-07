using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Timeline Signal Receiver UnityEvent ile bağlanır: sahne geçişi ve oyuncu kilidi.
/// </summary>
public class SignalManager : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string mainSceneName = "Main Scene";

    [Header("Options")]
    [SerializeField] private bool useLoadingScreen = true;

    public void LoadMainScene()
    {
        if (useLoadingScreen)
            LoadingScreenController.LoadScene(mainSceneName);
        else
            SceneManager.LoadScene(mainSceneName);
    }

    public void OnCinematicFinished() => LoadMainScene();

    public void LockPlayer() => PlayerControlGate.Acquire(PlayerControlGate.ReasonCinematic);

    public void UnlockPlayer() => PlayerControlGate.Release(PlayerControlGate.ReasonCinematic);
}
