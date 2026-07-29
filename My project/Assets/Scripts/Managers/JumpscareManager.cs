using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class JumpscareManager : MonoBehaviour
{
    public static JumpscareManager Instance;

    public CinemachineCamera jumpscareCam;

    [SerializeField] private GameObject wishlistBanner;
    [SerializeField] private DemoEndScreenUI demoEndScreen;
    [SerializeField] private string reloadSceneName = "Main Scene";
    [SerializeField] private float endGameDelaySeconds = 4f;

    [Header("Impact punch (axe hit)")]
    [Tooltip("Degrees FOV narrows on impact, then overshoots the other way.")]
    [SerializeField] float impactFovPunchIn = 10f;
    [SerializeField] float impactFovPunchOut = 4f;
    [SerializeField] float impactFovInSeconds = 0.06f;
    [SerializeField] float impactFovOutSeconds = 0.1f;
    [SerializeField] float impactFovSettleSeconds = 0.18f;
    [Tooltip("Max Dutch (roll) wobble in degrees during the shake.")]
    [SerializeField] float impactShakeDegrees = 1.8f;
    [SerializeField] float impactShakeSeconds = 0.22f;

    Coroutine _endGameRoutine;
    Coroutine _impactPunchRoutine;
    float _impactBaseFov = 60f;
    float _impactBaseDutch;

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

        if (jumpscareCam != null)
        {
            _impactBaseFov = jumpscareCam.Lens.FieldOfView;
            _impactBaseDutch = jumpscareCam.Lens.Dutch;
        }

        if (_endGameRoutine != null)
            StopCoroutine(_endGameRoutine);

        _endGameRoutine = StartCoroutine(EndGameAfterDelay());
    }

    /// <summary>
    /// Called from the axe impact animation event — FOV kick + short roll shake.
    /// </summary>
    public void PlayImpactPunch()
    {
        if (jumpscareCam == null)
            return;

        if (_impactPunchRoutine != null)
            StopCoroutine(_impactPunchRoutine);

        _impactPunchRoutine = StartCoroutine(ImpactPunchRoutine());
    }

    IEnumerator ImpactPunchRoutine()
    {
        float baseFov = _impactBaseFov;
        float baseDutch = _impactBaseDutch;
        float punchedIn = Mathf.Max(20f, baseFov - impactFovPunchIn);
        float punchedOut = baseFov + impactFovPunchOut;

        yield return LerpLens(punchedIn, baseDutch, impactFovInSeconds, shake: true);
        yield return LerpLens(punchedOut, baseDutch, impactFovOutSeconds, shake: true);
        yield return LerpLens(baseFov, baseDutch, impactFovSettleSeconds, shake: false);

        SetLens(baseFov, baseDutch);
        _impactPunchRoutine = null;
    }

    IEnumerator LerpLens(float targetFov, float targetDutch, float duration, bool shake)
    {
        float startFov = jumpscareCam.Lens.FieldOfView;
        float startDutch = jumpscareCam.Lens.Dutch;
        float dur = Mathf.Max(0.01f, duration);
        float elapsed = 0f;
        float shakeWindow = Mathf.Max(0.01f, impactShakeSeconds);

        while (elapsed < dur)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / dur);
            float eased = 1f - (1f - t) * (1f - t);

            float fov = Mathf.Lerp(startFov, targetFov, eased);
            float dutch = Mathf.Lerp(startDutch, targetDutch, eased);

            if (shake)
            {
                float shakeT = 1f - Mathf.Clamp01(elapsed / shakeWindow);
                dutch += Random.Range(-impactShakeDegrees, impactShakeDegrees) * shakeT * shakeT;
                fov += Random.Range(-0.6f, 0.6f) * shakeT;
            }

            SetLens(fov, dutch);
            yield return null;
        }

        SetLens(targetFov, targetDutch);
    }

    void SetLens(float fov, float dutch)
    {
        if (jumpscareCam == null)
            return;

        var lens = jumpscareCam.Lens;
        lens.FieldOfView = fov;
        lens.Dutch = dutch;
        jumpscareCam.Lens = lens;
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

        LoadReloadScene();
        _endGameRoutine = null;
    }

    public void EndJumpscare()
    {
        if (_impactPunchRoutine != null)
        {
            StopCoroutine(_impactPunchRoutine);
            _impactPunchRoutine = null;
        }

        if (jumpscareCam != null)
        {
            SetLens(_impactBaseFov, _impactBaseDutch);
            jumpscareCam.Priority = 0;
        }
    }

    public void LoadReloadScene()
    {
        PlayerControlGate.Clear();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        LoadingScreenController.LoadScene(reloadSceneName);
    }

    /// <summary>Legacy name — same as reloading the gameplay scene after capture.</summary>
    public void LoadMenuScene() => LoadReloadScene();
}
