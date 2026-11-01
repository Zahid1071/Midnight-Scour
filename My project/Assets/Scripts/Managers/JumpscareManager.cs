using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class JumpscareManager : MonoBehaviour
{
    public static JumpscareManager Instance;

    /// <summary>True after <see cref="BeginCaptureUi"/> until game-over / reload.</summary>
    public static bool CaptureUiActive { get; private set; }

    /// <summary>True once the jumpscare camera / end-game wait has started.</summary>
    public bool IsJumpscarePlaying =>
        _endGameRoutine != null
        || (jumpscareCam != null && jumpscareCam.Priority.Value > 0);

    public CinemachineCamera jumpscareCam;

    [SerializeField] private string reloadSceneName = "Main Scene";
    [SerializeField] private string mainMenuSceneName = "Main Menu";
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
    }

    public void TriggerJumpscare(Transform jumpscarePoint, Transform npcFace)
    {
        BeginCaptureUi();

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
    /// Strip gameplay HUD, hints, subtitles, and cursor for capture / jumpscare.
    /// Safe to call before the hide-exit wait so UI never flashes on screen.
    /// </summary>
    public static void BeginCaptureUi()
    {
        CaptureUiActive = true;

        // Block inventory / interact for the whole capture (including hide-exit wait).
        if (!PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            PlayerControlGate.Acquire(PlayerControlGate.ReasonCaptured);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        GameplayHudVisibility.Instance?.HideGameplayHud();
        GameplayHintManager.Instance?.HideAll();
        HudManager.Instance?.ClearSubtitle(HudManager.PriorityHigh);

        var controlsHint = Object.FindFirstObjectByType<ControlsPanelHint>(FindObjectsInactive.Include);
        controlsHint?.ForceHide();

        if (ReadPanelUI.Instance != null && ReadPanelUI.Instance.IsOpen)
            ReadPanelUI.Instance.Close();

        Object.FindFirstObjectByType<PauseManager>(FindObjectsInactive.Include)?.DismissForGameOver();
    }

    /// <summary>Undo <see cref="BeginCaptureUi"/> when capture was aborted (e.g. not at hide spot yet).</summary>
    public static void CancelCaptureUi()
    {
        CaptureUiActive = false;
        PlayerControlGate.Release(PlayerControlGate.ReasonCaptured);
        GameplayHudVisibility.Instance?.ClearHidingLatch();
        GameplayHudVisibility.Instance?.ShowGameplayHud();
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

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        CaptureReason reason = CaptureContext.Consume();
        var gameOver = GameOverUI.Instance;
        if (gameOver == null)
            gameOver = FindFirstObjectByType<GameOverUI>(FindObjectsInactive.Include);
        if (gameOver == null)
        {
            var go = new GameObject("GameOverUI");
            gameOver = go.AddComponent<GameOverUI>();
        }

        yield return StartCoroutine(gameOver.WaitForChoiceRoutine(reason));

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

        CaptureUiActive = false;

        // Drop hide-spot latch; keep gameplay HUD off until game-over owns presentation.
        GameplayHudVisibility.Instance?.ClearHidingLatch();
        GameplayHudVisibility.Instance?.HideGameplayHud();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void LoadReloadScene()
    {
        CaptureUiActive = false;
        PlayerControlGate.Clear();
        InventoryManager.ResetForNewGame();
        GameplayHintManager.ResetAllHints();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        Checkpoint.RequestRestore();
        LoadingScreenController.LoadScene(reloadSceneName);
    }

    public void LoadMenuScene()
    {
        CaptureUiActive = false;
        PlayerControlGate.Clear();
        InventoryManager.ResetForNewGame();
        GameplayHintManager.ResetAllHints();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        LoadingScreenController.LoadScene(mainMenuSceneName);
    }
}
