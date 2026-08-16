using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Screwdriver lock break:
/// Aim — tip stays on the keyhole; move the butt in a limited cone.
/// Hold LMB — shove in; mouse travel fills the break bar.
/// Release — pulls out; a full in→out pump also fills the bar.
/// Bar decays slowly; fill it completely to break the lock.
/// </summary>
[DisallowMultipleComponent]
public class ScrewdriverLockBreakMinigame : MonoBehaviour
{
    public static ScrewdriverLockBreakMinigame Active { get; private set; }

    public enum Stage
    {
        Idle,
        Playing,
        Exiting
    }

    [Header("Scene refs")]
    [Tooltip("Keyhole point. Forward should point INTO the lock.")]
    [SerializeField] Transform keyhole;
    [SerializeField] CinemachineCamera lockpickCamera;
    [SerializeField] int activeCameraPriority = 20;
    [SerializeField] float cameraBlendOutSeconds = 0.85f;

    [Header("Screwdriver visual")]
    [Tooltip("Optional. If empty, uses the used item's worldPrefab (Screwdriver.asset).")]
    [SerializeField] GameObject screwdriverPrefabOverride;
    [Tooltip("Local tip on the spawned mesh. Ignored when Auto Tip From Bounds is on.")]
    [SerializeField] Vector3 tipLocalOffset = Vector3.zero;
    [Tooltip("Extra local euler so the mesh tip faces +Z if the FBX axis differs.")]
    [SerializeField] Vector3 tipAxisFixEuler = Vector3.zero;
    [Tooltip("If on, scales the prefab so its longest mesh axis matches Target Length (pickup mesh is tiny).")]
    [SerializeField] bool autoFitScale = true;
    [Tooltip("Desired screwdriver length in meters after auto-fit (close-up prop size).")]
    [SerializeField] float targetLength = 0.22f;
    [Tooltip("Used only when Auto Fit Scale is off.")]
    [SerializeField] float spawnScale = 1f;
    [Tooltip("Estimate tip from mesh bounds (+Z end). Keep on for the stock Screwdriver prefab.")]
    [SerializeField] bool autoTipFromBounds = true;

    [Header("Aim")]
    [SerializeField] float aimSensitivity = 0.0045f;
    [Tooltip("How far the butt can wander from the ideal insert axis (degrees).")]
    [SerializeField] float aimConeDegrees = 18f;
    [SerializeField] bool invertAimX = true;
    [SerializeField] bool invertAimY = true;

    [Header("Insert")]
    [Tooltip("How far the tip pushes into the keyhole while LMB is held.")]
    [SerializeField] float insertDepth = 0.045f;
    [Tooltip("Seconds to fully shove in / pull out.")]
    [SerializeField] float insertPushSeconds = 0.25f;

    [Header("Break bar")]
    [Tooltip("How fast the bar drains (0-1 per second). 0 = no drain.")]
    [SerializeField] float barDecayPerSecond = 0f;
    [Tooltip("Bar fill from mouse travel while shoved in (per mouse-pixel).")]
    [SerializeField] float mouseFillPerPixel = 0.00055f;
    [Tooltip("Max bar fill you can earn in one shove before you must release and push again.")]
    [SerializeField] float maxFillPerPush = 0.28f;
    [Tooltip("Aim sensitivity multiplier when this shove's budget is used up (stuck).")]
    [SerializeField, Range(0f, 1f)] float stuckAimScale = 0.12f;

    [Header("Progress UI (mic-style). Leave empty to auto-create.")]
    [SerializeField] GameObject progressPanel;
    [SerializeField] Slider progressSlider;
    [SerializeField] Image progressFillImage;
    [SerializeField] Color barEmptyColor = new Color(0.95f, 0.28f, 0.22f, 1f);
    [SerializeField] Color barFullColor = new Color(0.45f, 0.95f, 0.5f, 1f);

    [Header("Audio (optional)")]
    [Tooltip("Loops while LMB is held (not stuck). Volume rises with mouse movement.")]
    [SerializeField] AudioClip holdLoopSfx;
    [Tooltip("Loops while LMB is held and the shove is stuck. Own volume controls.")]
    [SerializeField] AudioClip stuckLoopSfx;
    [SerializeField] AudioClip breakSfx;
    [SerializeField] AudioClip cancelSfx;
    [SerializeField] float sfxVolume = 1f;
    [SerializeField, Range(0f, 1f)] float holdLoopMinVolume = 0.08f;
    [SerializeField, Range(0f, 1f)] float holdLoopMaxVolume = 1f;
    [Tooltip("Mouse travel (pixels/frame, smoothed) that reaches max hold-loop volume.")]
    [SerializeField] float holdLoopMouseForMaxVolume = 25f;
    [SerializeField] float holdLoopVolumeSmooth = 12f;
    [SerializeField, Range(0f, 1f)] float stuckLoopMinVolume = 0.05f;
    [SerializeField, Range(0f, 1f)] float stuckLoopMaxVolume = 0.45f;
    [Tooltip("Mouse travel that reaches max stuck-loop volume.")]
    [SerializeField] float stuckLoopMouseForMaxVolume = 25f;

    [Header("Hints")]
    [SerializeField] string aimHintKey = "gameplay.screwdriver_aim";
    [SerializeField] string holdHintKey = "gameplay.screwdriver_shake";
    [SerializeField] string stuckHintKey = "gameplay.screwdriver_stuck";

    Stage _stage = Stage.Idle;
    InventoryItem _tool;
    Action<InventoryItem> _onSuccess;
    Action _onCancel;

    Transform _screwdriver;
    GameObject _spawnedVisual;
    Vector3 _resolvedTipLocal;
    Quaternion _tipAxisFix;

    Vector2 _aimAngles;
    float _insertProgress;
    float _breakFill;
    float _sessionFillLeft;
    bool _sessionArmed;
    bool _wasHolding;
    bool _ownsProgressUi;
    bool _wasStuck;
    AudioSource _loopSource;
    bool _loopIsStuckClip;
    float _holdLoopMoveSmoothed;
    float _holdLoopVolumeSmoothed;

    public Stage CurrentStage => _stage;
    public bool IsRunning => _stage == Stage.Playing;
    public bool IsHoldingBreak => IsRunning && Mouse.current != null && Mouse.current.leftButton.isPressed;
    public float BreakNormalized => Mathf.Clamp01(_breakFill);

    void Awake()
    {
        if (lockpickCamera != null)
            lockpickCamera.Priority = 0;

        if (progressSlider != null)
            progressSlider.interactable = false;

        SetProgressVisible(false);
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;
        StopHoldLoop();
        if (_stage != Stage.Idle)
            ForceAbortCleanup();
        else
        {
            DestroySpawnedVisual();
            TeardownOwnedProgressUi();
        }
    }

    /// <summary>
    /// Starts the minigame. Caller should already have validated the tool / inventory.
    /// </summary>
    public bool TryBegin(InventoryItem tool, Action<InventoryItem> onSuccess, Action onCancel = null)
    {
        if (tool == null || keyhole == null || lockpickCamera == null)
        {
            Debug.LogWarning($"[ScrewdriverLockBreak] Missing refs on {name}.", this);
            return false;
        }

        if (_stage != Stage.Idle)
            return false;

        if (Active != null && Active != this && Active.IsRunning)
            return false;

        if (!SpawnScrewdriverVisual(tool))
        {
            Debug.LogWarning($"[ScrewdriverLockBreak] No screwdriver prefab for '{tool.itemName}'.", this);
            return false;
        }

        EnsureProgressUi();

        _tool = tool;
        _onSuccess = onSuccess;
        _onCancel = onCancel;
        _aimAngles = Vector2.zero;
        _insertProgress = 0f;
        _breakFill = 0f;
        _sessionFillLeft = 0f;
        _sessionArmed = false;
        _wasHolding = false;
        _wasStuck = false;

        Active = this;
        PlayerControlGate.Acquire(PlayerControlGate.ReasonLockpick);

        ApplyAimPose();
        RefreshProgressUi();
        SetProgressVisible(true);

        lockpickCamera.Priority = activeCameraPriority;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        _stage = Stage.Playing;
        RefreshMinigameHint(holding: false, seated: false, stuck: false);
        return true;
    }

    bool SpawnScrewdriverVisual(InventoryItem tool)
    {
        DestroySpawnedVisual();

        GameObject prefab = screwdriverPrefabOverride != null
            ? screwdriverPrefabOverride
            : tool != null ? tool.GetDropPrefab() : null;

        if (prefab == null)
            return false;

        _spawnedVisual = Instantiate(prefab, keyhole.position, Quaternion.identity);
        _spawnedVisual.name = "LockBreakScrewdriver";
        _screwdriver = _spawnedVisual.transform;
        _screwdriver.localScale = Vector3.one;

        StripPickupBehaviour(_spawnedVisual);
        SetLayerRecursive(_spawnedVisual, 0); // Default — avoid Interactable_Item culling surprises

        float scale = ResolveSpawnScale(_spawnedVisual);
        _screwdriver.localScale = Vector3.one * scale;

        _tipAxisFix = Quaternion.Euler(tipAxisFixEuler);
        _resolvedTipLocal = autoTipFromBounds
            ? EstimateTipLocalOffset(_spawnedVisual)
            : tipLocalOffset;

        return true;
    }

    float ResolveSpawnScale(GameObject go)
    {
        if (!autoFitScale)
            return Mathf.Max(0.0001f, spawnScale);

        float longest = MeasureLongestLocalAxis(go);
        if (longest < 1e-8f)
            return Mathf.Max(0.0001f, spawnScale > 0.01f ? spawnScale : 80f);

        return Mathf.Max(0.0001f, targetLength / longest);
    }

    static float MeasureLongestLocalAxis(GameObject go)
    {
        Bounds local = GetCombinedLocalMeshBounds(go);
        if (local.size.sqrMagnitude < 1e-12f)
            return 0f;
        return Mathf.Max(local.size.x, local.size.y, local.size.z);
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
    }

    static void StripPickupBehaviour(GameObject go)
    {
        foreach (var item in go.GetComponentsInChildren<InteractableItem>(true))
            Destroy(item);

        foreach (var col in go.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }
    }

    static Bounds GetCombinedLocalMeshBounds(GameObject go)
    {
        var filters = go.GetComponentsInChildren<MeshFilter>(true);
        Transform root = go.transform;
        Vector3 savedScale = root.localScale;
        root.localScale = Vector3.one;

        Bounds local = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;

        for (int i = 0; i < filters.Length; i++)
        {
            var mf = filters[i];
            if (mf.sharedMesh == null)
                continue;

            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            Vector3 e = mb.extents;
            // Transform the 8 corners so rotation/scale of children is handled.
            for (int cx = -1; cx <= 1; cx += 2)
            for (int cy = -1; cy <= 1; cy += 2)
            for (int cz = -1; cz <= 1; cz += 2)
            {
                Vector3 corner = toRoot.MultiplyPoint3x4(mb.center + Vector3.Scale(e, new Vector3(cx, cy, cz)));
                if (!any)
                {
                    local = new Bounds(corner, Vector3.zero);
                    any = true;
                }
                else
                {
                    local.Encapsulate(corner);
                }
            }
        }

        root.localScale = savedScale;
        return any ? local : new Bounds(Vector3.zero, Vector3.zero);
    }

    static Vector3 EstimateTipLocalOffset(GameObject go)
    {
        Bounds local = GetCombinedLocalMeshBounds(go);
        if (local.size.sqrMagnitude < 1e-12f)
            return new Vector3(0f, 0f, 0.12f);

        // Tip = farthest extent along the longest axis (prefer +Z for shaft tools).
        float absX = Mathf.Abs(local.extents.x);
        float absY = Mathf.Abs(local.extents.y);
        float absZ = Mathf.Abs(local.extents.z);
        if (absZ >= absX && absZ >= absY)
            return new Vector3(local.center.x, local.center.y, local.max.z);
        if (absY >= absX)
            return new Vector3(local.center.x, local.max.y, local.center.z);
        return new Vector3(local.max.x, local.center.y, local.center.z);
    }

    void DestroySpawnedVisual()
    {
        if (_spawnedVisual != null)
        {
            Destroy(_spawnedVisual);
            _spawnedVisual = null;
        }

        _screwdriver = null;
    }

    void Update()
    {
        if (_stage != Stage.Playing)
            return;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // Pause / capture still blocks; lockpick reason itself should not freeze our Update.
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonPause)
            || PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            return;

        if (Keyboard.current != null
            && (Keyboard.current.escapeKey.wasPressedThisFrame
                || Keyboard.current.rKey.wasPressedThisFrame))
        {
            CancelAndExit();
            return;
        }

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelAndExit();
            return;
        }

        Vector2 mouseDelta = Mouse.current != null
            ? Mouse.current.delta.ReadValue()
            : Vector2.zero;

        TickPlaying(mouseDelta);
    }

    void TickPlaying(Vector2 mouseDelta)
    {
        bool holding = Mouse.current != null && Mouse.current.leftButton.isPressed;
        float pushSpeed = 1f / Mathf.Max(0.05f, insertPushSeconds);
        if (holding)
            _insertProgress = Mathf.Min(1f, _insertProgress + Time.deltaTime * pushSpeed);
        else
            _insertProgress = Mathf.Max(0f, _insertProgress - Time.deltaTime * pushSpeed);

        bool seated = holding && _insertProgress >= 0.99f;

        // New shove: grant a fresh movement budget once fully in.
        if (seated && !_sessionArmed)
        {
            _sessionArmed = true;
            _sessionFillLeft = Mathf.Max(0.01f, maxFillPerPush);
        }

        if (!holding)
        {
            _sessionArmed = false;
            _sessionFillLeft = 0f;
        }

        bool canWiggle = seated && _sessionFillLeft > 0.0001f;
        bool stuck = seated && !canWiggle;

        // Aim freely when not seated; full wiggle while budget remains; tiny motion when stuck.
        float aimScale = 1f;
        if (seated)
            aimScale = canWiggle ? 1f : stuckAimScale;

        if (aimScale > 0f)
        {
            float aimX = mouseDelta.x * (invertAimX ? -1f : 1f);
            float aimY = mouseDelta.y * (invertAimY ? -1f : 1f);
            _aimAngles.x = Mathf.Clamp(
                _aimAngles.x + aimX * aimSensitivity * aimScale,
                -aimConeDegrees,
                aimConeDegrees);
            _aimAngles.y = Mathf.Clamp(
                _aimAngles.y + aimY * aimSensitivity * aimScale,
                -aimConeDegrees,
                aimConeDegrees);
        }

        ApplyAimPose(_insertProgress * insertDepth);

        if (holding && !_wasHolding)
        {
            StartLoop(stuck: false);
        }
        else if (!holding && _wasHolding)
        {
            StopHoldLoop();
        }
        else if (holding && stuck && !_wasStuck)
        {
            StartLoop(stuck: true);
        }
        else if (holding && !stuck && _wasStuck)
        {
            StartLoop(stuck: false);
        }

        if (holding)
            UpdateHoldLoopVolume(mouseDelta, stuck);

        RefreshMinigameHint(holding, seated, stuck);

        _wasHolding = holding;
        _wasStuck = stuck;

        // Mouse fill only while seated and this shove still has budget.
        if (canWiggle && mouseDelta.sqrMagnitude > 0f)
        {
            float want = mouseDelta.magnitude * mouseFillPerPixel;
            float add = Mathf.Min(want, _sessionFillLeft);
            if (add > 0f)
            {
                _sessionFillLeft -= add;
                AddBreakFill(add);
            }
        }

        // Check success BEFORE decay so a full bar actually finishes.
        if (_breakFill >= 0.999f && _stage == Stage.Playing)
        {
            _breakFill = 1f;
            RefreshProgressUi();
            StopHoldLoop();
            if (breakSfx != null)
                SoundManager.Instance?.PlaySFXAt(breakSfx, keyhole.position, sfxVolume);
            StartCoroutine(FinishSuccessRoutine());
            return;
        }

        if (barDecayPerSecond > 0f && _breakFill > 0f)
            _breakFill = Mathf.Max(0f, _breakFill - barDecayPerSecond * Time.deltaTime);

        RefreshProgressUi();
    }

    void AddBreakFill(float amount)
    {
        if (amount <= 0f)
            return;
        _breakFill = Mathf.Clamp01(_breakFill + amount);
    }

    void EnsureProgressUi()
    {
        if (progressSlider != null)
        {
            progressSlider.interactable = false;
            progressSlider.minValue = 0f;
            progressSlider.maxValue = 1f;
            return;
        }

        Canvas canvas = null;
        if (HudManager.Instance != null)
            canvas = HudManager.Instance.GetComponentInParent<Canvas>();
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;

        _ownsProgressUi = true;

        progressPanel = new GameObject("LockBreakProgressPanel", typeof(RectTransform));
        progressPanel.transform.SetParent(canvas.transform, false);
        var panelRt = progressPanel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0f);
        panelRt.anchorMax = new Vector2(0.5f, 0f);
        panelRt.pivot = new Vector2(0.5f, 0f);
        panelRt.anchoredPosition = new Vector2(0f, 72f);
        panelRt.sizeDelta = new Vector2(360f, 28f);

        var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(progressPanel.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);
        bgImg.raycastTarget = false;

        var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGo.transform.SetParent(progressPanel.transform, false);
        var sliderRt = sliderGo.GetComponent<RectTransform>();
        sliderRt.anchorMin = Vector2.zero;
        sliderRt.anchorMax = Vector2.one;
        sliderRt.offsetMin = new Vector2(6f, 6f);
        sliderRt.offsetMax = new Vector2(-6f, -6f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderGo.transform, false);
        var fillAreaRt = fillArea.GetComponent<RectTransform>();
        fillAreaRt.anchorMin = Vector2.zero;
        fillAreaRt.anchorMax = Vector2.one;
        fillAreaRt.offsetMin = Vector2.zero;
        fillAreaRt.offsetMax = Vector2.zero;

        var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGo.transform.SetParent(fillArea.transform, false);
        var fillRt = fillGo.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = Vector2.zero;
        fillRt.offsetMax = Vector2.zero;
        progressFillImage = fillGo.GetComponent<Image>();
        progressFillImage.color = barEmptyColor;
        progressFillImage.raycastTarget = false;

        progressSlider = sliderGo.GetComponent<Slider>();
        progressSlider.interactable = false;
        progressSlider.minValue = 0f;
        progressSlider.maxValue = 1f;
        progressSlider.wholeNumbers = false;
        progressSlider.transition = Selectable.Transition.None;
        progressSlider.fillRect = fillRt;
        progressSlider.targetGraphic = progressFillImage;
        progressSlider.direction = Slider.Direction.LeftToRight;
        progressSlider.value = 0f;
    }

    void RefreshProgressUi()
    {
        float t = Mathf.Clamp01(_breakFill);
        if (progressSlider != null)
            progressSlider.value = t;
        if (progressFillImage != null)
            progressFillImage.color = Color.Lerp(barEmptyColor, barFullColor, t);
    }

    void SetProgressVisible(bool visible)
    {
        if (progressPanel != null)
            progressPanel.SetActive(visible);
    }

    void TeardownOwnedProgressUi()
    {
        SetProgressVisible(false);
        if (!_ownsProgressUi)
            return;

        if (progressPanel != null)
            Destroy(progressPanel);

        progressPanel = null;
        progressSlider = null;
        progressFillImage = null;
        _ownsProgressUi = false;
    }

    void ApplyAimPose(float depthAlongAxis = 0f)
    {
        if (_screwdriver == null)
            return;

        Vector3 insertDir = GetInsertDirection();
        Vector3 tip = keyhole.position + insertDir * depthAlongAxis;
        Quaternion rot = Quaternion.LookRotation(insertDir, keyhole.up) * _tipAxisFix;
        _screwdriver.rotation = rot;
        // TransformVector includes localScale (auto-fit), unlike rot * localOffset.
        _screwdriver.position = tip - _screwdriver.TransformVector(_resolvedTipLocal);
    }

    Vector3 GetInsertDirection()
    {
        Quaternion yaw = Quaternion.AngleAxis(_aimAngles.x, keyhole.up);
        Quaternion pitch = Quaternion.AngleAxis(-_aimAngles.y, keyhole.right);
        return (pitch * yaw * keyhole.forward).normalized;
    }

    void StartLoop(bool stuck)
    {
        AudioClip clip = stuck ? stuckLoopSfx : holdLoopSfx;
        // Fall back to hold loop if stuck clip not assigned.
        if (clip == null && stuck)
            clip = holdLoopSfx;
        if (clip == null)
        {
            StopHoldLoop();
            return;
        }

        // Reuse source if same mode/clip already playing.
        if (_loopSource != null && _loopIsStuckClip == stuck && _loopSource.clip == clip)
            return;

        StopHoldLoop();

        var go = new GameObject(stuck ? "ScrewdriverStuckLoop" : "ScrewdriverHoldLoop");
        go.transform.position = keyhole != null ? keyhole.position : transform.position;
        _loopSource = go.AddComponent<AudioSource>();
        _loopSource.clip = clip;
        _loopSource.loop = true;
        _loopSource.spatialBlend = 1f;
        _loopSource.playOnAwake = false;
        _loopIsStuckClip = stuck;
        _holdLoopMoveSmoothed = 0f;

        float minVol = stuck ? stuckLoopMinVolume : holdLoopMinVolume;
        _holdLoopVolumeSmoothed = minVol * sfxVolume;
        _loopSource.volume = _holdLoopVolumeSmoothed;
        _loopSource.Play();
    }

    void UpdateHoldLoopVolume(Vector2 mouseDelta, bool stuck)
    {
        if (_loopSource == null)
            return;

        if (keyhole != null)
            _loopSource.transform.position = keyhole.position;

        float move = mouseDelta.magnitude;
        float smooth = Mathf.Max(1f, holdLoopVolumeSmooth);
        _holdLoopMoveSmoothed = Mathf.Lerp(_holdLoopMoveSmoothed, move, 1f - Mathf.Exp(-smooth * Time.deltaTime));

        float minVol = stuck ? stuckLoopMinVolume : holdLoopMinVolume;
        float maxVol = stuck ? stuckLoopMaxVolume : holdLoopMaxVolume;
        float mouseForMax = stuck ? stuckLoopMouseForMaxVolume : holdLoopMouseForMaxVolume;

        float t = mouseForMax > 0.01f
            ? Mathf.Clamp01(_holdLoopMoveSmoothed / mouseForMax)
            : 1f;
        float target = Mathf.Lerp(minVol, maxVol, t) * sfxVolume;

        _holdLoopVolumeSmoothed = Mathf.Lerp(_holdLoopVolumeSmoothed, target, 1f - Mathf.Exp(-smooth * Time.deltaTime));
        _loopSource.volume = _holdLoopVolumeSmoothed;
    }

    void StopHoldLoop()
    {
        if (_loopSource != null)
        {
            _loopSource.Stop();
            Destroy(_loopSource.gameObject);
            _loopSource = null;
        }

        _loopIsStuckClip = false;
        _holdLoopMoveSmoothed = 0f;
        _holdLoopVolumeSmoothed = 0f;
    }

    void CancelAndExit()
    {
        if (_stage == Stage.Idle || _stage == Stage.Exiting)
            return;

        StopHoldLoop();
        if (cancelSfx != null)
            SoundManager.Instance?.PlaySFXAt(cancelSfx, keyhole.position, sfxVolume);

        var cancel = _onCancel;
        StartCoroutine(FinishExitRoutine(success: false, cancel));
    }

    IEnumerator FinishSuccessRoutine()
    {
        if (_stage != Stage.Playing)
            yield break;

        _stage = Stage.Exiting;

        var tool = _tool;
        var success = _onSuccess;
        _onSuccess = null;
        _onCancel = null;

        // Unlock SFX / subtitle / consume happen immediately — don't wait for cam blend.
        success?.Invoke(tool);

        yield return FinishExitRoutine(success: true, null);
    }

    IEnumerator FinishExitRoutine(bool success, Action cancelCallback)
    {
        _stage = Stage.Exiting;
        HudManager.Instance?.ClearSubtitle();

        SetProgressVisible(false);
        DestroySpawnedVisual();

        var playerCam = FindPlayerVirtualCamera();
        var brain = FindFirstObjectByType<CinemachineBrain>();

        // Player keeps the look pitch they entered with — no rotation clear on exit.
        CinemachineVirtualCameraBase.StandbyUpdateMode savedStandby = default;
        if (playerCam != null)
        {
            savedStandby = playerCam.StandbyUpdate;
            playerCam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
        }

        // Give CM a couple frames to bake the standby PVC state before blending back.
        yield return null;
        yield return null;

        if (lockpickCamera != null)
            lockpickCamera.Priority = 0;

        float wait = Mathf.Max(0.5f, cameraBlendOutSeconds);
        float t = 0f;
        while (t < wait)
        {
            if (brain != null && !brain.IsBlending && t > 0.1f)
                break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (playerCam != null)
            playerCam.StandbyUpdate = savedStandby;

        ForceAbortCleanup();
        if (!success)
            cancelCallback?.Invoke();
    }

    static CinemachineCamera FindPlayerVirtualCamera()
    {
        var all = FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var cam in all)
        {
            if (cam == null)
                continue;
            string n = cam.name;
            if (n.IndexOf("Player Virutal", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Player Virtual", StringComparison.OrdinalIgnoreCase) >= 0)
                return cam;
        }

        foreach (var cam in all)
        {
            if (cam != null && cam.name.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0)
                return cam;
        }

        return null;
    }

    void ForceAbortCleanup()
    {
        StopHoldLoop();
        DestroySpawnedVisual();
        TeardownOwnedProgressUi();
        SetProgressVisible(false);

        if (lockpickCamera != null)
            lockpickCamera.Priority = 0;

        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonLockpick))
            PlayerControlGate.Release(PlayerControlGate.ReasonLockpick);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        HudManager.Instance?.ClearSubtitle();

        _tool = null;
        _onSuccess = null;
        _onCancel = null;
        _breakFill = 0f;
        _stage = Stage.Idle;

        if (Active == this)
            Active = null;
    }

    void RefreshMinigameHint(bool holding, bool seated, bool stuck)
    {
        string key;
        string fallback;

        if (stuck)
        {
            key = stuckHintKey;
            fallback = "It's stuck, try again.";
        }
        else if (seated && holding)
        {
            key = holdHintKey;
            fallback = "Shake violently to break it.";
        }
        else
        {
            key = aimHintKey;
            fallback = "Shove the screwdriver in.";
        }

        string msg = !string.IsNullOrEmpty(key) ? Loc.Get(key) : fallback;
        if (string.IsNullOrEmpty(msg) || msg == key)
            msg = fallback;
        if (string.IsNullOrEmpty(msg))
            return;

        Color? color = HudManager.Instance != null ? HudManager.Instance.WarningColor : null;
        HudManager.Instance?.ShowPersistentSubtitle(msg, color);
    }
}
