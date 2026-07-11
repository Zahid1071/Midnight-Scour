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

        ResolveWishlistBanner();

        if (wishlistBanner != null)
            wishlistBanner.SetActive(false);

        if (demoEndScreen == null)
            demoEndScreen = FindFirstObjectByType<DemoEndScreenUI>(FindObjectsInactive.Include);
    }

    void ResolveWishlistBanner()
    {
        if (wishlistBanner != null)
            return;

        var transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in transforms)
        {
            if (t == null || t.name != "Wishlist Banner")
                continue;

            // Wishlist Banner lives under _UI Canvas, not under this manager.
            if (!t.gameObject.scene.IsValid())
                continue;

            wishlistBanner = t.gameObject;
            return;
        }
    }

    void ShowWishlistBanner()
    {
        ResolveWishlistBanner();

        if (wishlistBanner == null)
            return;

        wishlistBanner.SetActive(true);

        var anim = wishlistBanner.GetComponent<Animation>();
        if (anim != null && anim.clip != null)
            anim.Play(anim.clip.name);
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
            ShowWishlistBanner();
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
