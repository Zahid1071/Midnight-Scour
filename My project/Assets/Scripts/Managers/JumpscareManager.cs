using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class JumpscareManager : MonoBehaviour
{
    public static JumpscareManager Instance;

    public CinemachineCamera jumpscareCam;

    [SerializeField] private GameObject wishlistBanner;
    [SerializeField] private DemoEndScreenUI demoEndScreen;
    [SerializeField] private string menuSceneName = "Main Menu";
    [SerializeField] private float endGameDelaySeconds = 4f;

    Coroutine _endGameRoutine;

    void Awake()
    {
        Instance = this;

        if (wishlistBanner == null)
        {
            var found = GameObject.Find("Wishlist Banner");
            if (found != null)
                wishlistBanner = found;
        }

        if (wishlistBanner != null)
            wishlistBanner.SetActive(false);

        if (demoEndScreen == null)
            demoEndScreen = FindFirstObjectByType<DemoEndScreenUI>(FindObjectsInactive.Include);
    }

    public void TriggerJumpscare(Transform jumpscarePoint, Transform npcFace)
    {
        if (npcFace != null)
        {
            var lookDirector = npcFace.GetComponent<POILookDirector>()
                ?? npcFace.GetComponentInChildren<POILookDirector>()
                ?? npcFace.root.GetComponentInChildren<POILookDirector>();
            lookDirector?.CancelHoldAndClearLook();
        }

        jumpscareCam.transform.position = jumpscarePoint.position;
        jumpscareCam.Priority = 50;

        if (wishlistBanner != null)
            wishlistBanner.SetActive(true);

        if (_endGameRoutine != null)
            StopCoroutine(_endGameRoutine);

        _endGameRoutine = StartCoroutine(EndGameAfterDelay());
    }

    IEnumerator EndGameAfterDelay()
    {
        yield return new WaitForSecondsRealtime(endGameDelaySeconds);

        EndJumpscare();

        if (demoEndScreen == null)
            demoEndScreen = FindFirstObjectByType<DemoEndScreenUI>(FindObjectsInactive.Include);

        if (DemoEndDirector.IsDemoFinaleActive && demoEndScreen != null)
        {
            if (wishlistBanner != null)
                wishlistBanner.SetActive(false);

            yield return StartCoroutine(demoEndScreen.WaitForExitRoutine());
        }

        LoadMenuScene();
        _endGameRoutine = null;
    }

    public void EndJumpscare()
    {
        if (jumpscareCam != null)
            jumpscareCam.Priority = 0;
    }

    public void LoadMenuScene()
    {
        PlayerControlGate.Clear();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        LoadingScreenController.LoadScene(menuSceneName);
    }
}
