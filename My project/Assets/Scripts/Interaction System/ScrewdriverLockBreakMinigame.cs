using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Screwdriver lock break:
/// Aim — tip stays on the keyhole; move the butt in a limited cone.
/// Hold LMB — shove in; mouse travel slowly fills the break bar until it jams.
/// Jammed — force it (hard mouse movement) to break through for a big chunk of progress.
/// Pulling fully out and shoving back returns to the same jam, but each re-seat loosens it.
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
    [Tooltip("Bar fill mouse movement can earn before the screwdriver jams again.")]
    [SerializeField] float maxFillPerPush = 0.28f;

    [Header("Jam (forcing through)")]
    [Tooltip("Seconds of steady pushing in the pry direction needed to break through a fresh jam.")]
    [SerializeField] float stuckForceRequired = 4f;
    [Tooltip("While not pushing, built-up force drains at this many seconds per second.")]
    [SerializeField] float stuckForceBleedPerSecond = 0.5f;
    [Tooltip("Slowest push (pixels/second along the pry direction) that still counts.")]
    [SerializeField] float steadyPushMinSpeed = 15f;
    [Tooltip("Pushing faster than this earns no extra progress; at twice this speed it earns none.")]
    [SerializeField] float steadyPushMaxSpeed = 1500f;
    [Tooltip("How quickly the measured push speed follows the mouse (per second).")]
    [SerializeField] float pushSpeedSmooth = 20f;
    [Tooltip("Bar fill granted when a jam breaks.")]
    [SerializeField, Range(0f, 1f)] float stuckBreakFill = 0.35f;
    [Tooltip("Each full pull-out and re-seat multiplies the remaining force requirement by this.")]
    [SerializeField, Range(0.1f, 1f)] float reseatForceMultiplier = 0.7f;
    [Tooltip("Re-seating can never lower the requirement below this fraction of Stuck Force Required.")]
    [SerializeField, Range(0f, 1f)] float reseatMinForceFraction = 0.35f;
    [Tooltip("Insert progress below which the screwdriver counts as pulled out.")]
    [SerializeField, Range(0f, 0.5f)] float pulledOutThreshold = 0.05f;
    [Tooltip("Sideways mouse travel after a jam that decides whether the player pries left or right.")]
    [SerializeField] float jamDirectionPickPixels = 25f;
    [Tooltip("How fast pushing back against the pry direction removes force (1 = as fast as pushing builds it).")]
    [SerializeField, Range(0f, 2f)] float pushBackPenalty = 1f;
    [Tooltip("While jammed the mouse can't steer the screwdriver; it only creeps this many degrees toward the pry direction as force builds, and stays there on breakthrough.")]
    [SerializeField] float stuckLeanDegrees = 8f;
    [Tooltip("Max screwdriver tremble (degrees) while pushing the right way during a jam. 0 = off.")]
    [SerializeField] float stuckTrembleDegrees = 0.5f;
    [Tooltip("Extra depth (m) the tip lurches into the lock when a jam breaks.")]
    [SerializeField] float releaseDepthPunch = 0.012f;

    [Header("Final jam (breaking it opens the lock)")]
    [Tooltip("Force multiplier for the last jam relative to Stuck Force Required.")]
    [SerializeField] float finalJamForceMultiplier = 2f;
    [Tooltip("Loops while forcing the final jam. Falls back to Stuck Loop Sfx.")]
    [SerializeField] AudioClip finalJamLoopSfx;
    [SerializeField, Range(0f, 1f)] float finalLoopMinVolume = 0f;
    [SerializeField, Range(0f, 1f)] float finalLoopMaxVolume = 0.9f;

    [Header("Jam camera feel (tension -> release)")]
    [Tooltip("FOV narrows by up to this many degrees as force builds.")]
    [SerializeField] float tensionFovSqueeze = 2f;
    [Tooltip("Extra noise amplitude at full force (on top of the camera's own).")]
    [SerializeField] float tensionNoiseAmplitude = 0.35f;
    [Tooltip("Extra noise frequency at full force, so tension reads as a tremor.")]
    [SerializeField] float tensionNoiseFrequency = 1.5f;
    [Tooltip("Share of the tension shake/tremble present the instant a correct push starts (rest builds with force).")]
    [SerializeField, Range(0f, 1f)] float pushShakeFloor = 0.12f;
    [Tooltip("Scales the tension shake during the final jam.")]
    [SerializeField, Range(0f, 1f)] float finalJamShakeScale = 0.7f;
    [Tooltip("Camera jolt when a push in the right direction starts. 0 = off.")]
    [SerializeField] float pushOnsetKickDegrees = 0f;
    [SerializeField] float pushOnsetKickFrequency = 10f;
    [SerializeField] float pushOnsetKickDuration = 0.2f;
    [Tooltip("Minimum seconds between push-start jolts.")]
    [SerializeField] float pushOnsetCooldown = 0.3f;
    [Tooltip("FOV pops out by this many degrees when the jam breaks, then settles.")]
    [SerializeField] float releaseFovKick = 2.5f;
    [Tooltip("Noise amplitude burst when the jam breaks.")]
    [SerializeField] float releaseNoiseAmplitude = 1f;
    [Tooltip("Noise frequency during the release burst (higher = sharper jolt).")]
    [SerializeField] float releaseNoiseFrequency = 3f;
    [Tooltip("How fast the release settles (per second).")]
    [SerializeField] float releaseDecay = 9f;
    [Tooltip("How fast the tension follows the applied force (per second).")]
    [SerializeField] float tensionSmooth = 8f;

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
    [Tooltip("Parts cracking when a shove gets stuck (random pick each time).")]
    [SerializeField] AudioClip[] stuckCrackSfx;
    [SerializeField, Range(0f, 1f)] float stuckCrackVolume = 0.8f;
    [SerializeField] Vector2 stuckCrackPitchRange = new Vector2(0.9f, 1.1f);
    [Tooltip("Played when a jam is forced through (random pick). Falls back to Stuck Crack Sfx.")]
    [SerializeField] AudioClip[] jamBreakSfx;
    [SerializeField, Range(0f, 1f)] float jamBreakVolume = 1f;
    [Tooltip("Played when the lock finally breaks.")]
    [SerializeField] AudioClip breakSfx;
    [SerializeField] AudioClip cancelSfx;
    [SerializeField] float sfxVolume = 1f;
    [SerializeField, Range(0f, 1f)] float holdLoopMinVolume = 0.08f;
    [SerializeField, Range(0f, 1f)] float holdLoopMaxVolume = 1f;
    [Tooltip("Mouse travel (pixels/frame, smoothed) that reaches max hold-loop volume.")]
    [SerializeField] float holdLoopMouseForMaxVolume = 25f;
    [SerializeField] float holdLoopVolumeSmooth = 12f;
    [Tooltip("Volume response while jammed; high so the loop cuts out as soon as the push stops.")]
    [SerializeField] float stuckLoopVolumeSmooth = 45f;
    [SerializeField, Range(0f, 1f)] float stuckLoopMinVolume = 0f;
    [SerializeField, Range(0f, 1f)] float stuckLoopMaxVolume = 0.45f;
    [Tooltip("Mouse travel that reaches max stuck-loop volume.")]
    [SerializeField] float stuckLoopMouseForMaxVolume = 25f;

    [Header("Hints")]
    [SerializeField] string aimHintKey = "gameplay.screwdriver_aim";
    [SerializeField] string holdHintKey = "gameplay.screwdriver_shake";
    [SerializeField] string stuckHintKey = "gameplay.screwdriver_force";

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
    bool _jammed;
    bool _finalJam;
    float _jamForce;
    float _jamForceRequired;
    float _jamBaseRequired;
    Vector2 _jamDirection;
    Vector2 _jamDirectionProbe;
    float _pushSpeedSmoothed;
    float _pushGain;
    float _pushActivity;
    bool _wasPushing;
    float _lastPushOnsetTime;
    CameraKick _cameraKick;
    bool _pulledOutSinceSeat;
    float _tension;
    float _release;
    CinemachineBasicMultiChannelPerlin _cameraNoise;
    float _baseNoiseAmplitude;
    float _baseNoiseFrequency;
    float _baseFov;
    bool _wasHolding;
    bool _ownsProgressUi;
    bool _wasStuck;
    UIFadeHelper _progressFade;
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
        _sessionFillLeft = Mathf.Max(0.01f, maxFillPerPush);
        _sessionArmed = false;
        _jammed = false;
        _finalJam = false;
        _jamForce = 0f;
        _jamForceRequired = 0f;
        _jamBaseRequired = 0f;
        _jamDirection = Vector2.zero;
        _jamDirectionProbe = Vector2.zero;
        _pushSpeedSmoothed = 0f;
        _pushGain = 0f;
        _pushActivity = 0f;
        _pulledOutSinceSeat = false;
        _tension = 0f;
        _release = 0f;
        _cameraNoise = lockpickCamera.GetComponent<CinemachineBasicMultiChannelPerlin>();
        if (_cameraNoise != null)
        {
            _baseNoiseAmplitude = _cameraNoise.AmplitudeGain;
            _baseNoiseFrequency = _cameraNoise.FrequencyGain;
        }
        _baseFov = lockpickCamera.Lens.FieldOfView;
        _wasPushing = false;
        _lastPushOnsetTime = -999f;
        if (_cameraKick == null && pushOnsetKickDegrees > 0f)
            _cameraKick = lockpickCamera.gameObject.AddComponent<CameraKick>();
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

        if (_insertProgress <= pulledOutThreshold)
            _pulledOutSinceSeat = true;

        if (seated && !_sessionArmed)
        {
            _sessionArmed = true;
            // Re-seating into the same jam after a full pull-out loosens it.
            if (_jammed && _pulledOutSinceSeat)
            {
                float floor = _jamBaseRequired * reseatMinForceFraction;
                _jamForceRequired = Mathf.Max(floor, _jamForceRequired * reseatForceMultiplier);
            }
            _pulledOutSinceSeat = false;
        }

        if (!holding)
            _sessionArmed = false;

        if (seated && !_jammed && _sessionFillLeft <= 0.0001f)
        {
            _jammed = true;
            _jamForce = 0f;
            _jamDirection = Vector2.zero;
            _jamDirectionProbe = Vector2.zero;
            _pushSpeedSmoothed = 0f;
            // If breaking this jam plus one more wiggle would fill the bar, it is the last one.
            _finalJam = _breakFill + stuckBreakFill + maxFillPerPush >= 0.999f;
            _jamBaseRequired = Mathf.Max(0.1f, stuckForceRequired * (_finalJam ? finalJamForceMultiplier : 1f));
            _jamForceRequired = _jamBaseRequired;
        }

        bool canWiggle = seated && !_jammed;
        bool stuck = seated && _jammed;

        float dt = Time.deltaTime;
        float follow = 1f - Mathf.Exp(-pushSpeedSmooth * dt);
        _pushGain = 0f;
        if (stuck)
        {
            // The first consistent left or right movement picks the pry direction. Force is time spent
            // pushing steadily along it: speed beyond the steady range earns nothing, pushing back eases it off.
            // Summed travel cancels out while shaking, so only a consistent push picks the direction.
            if (_jamDirection == Vector2.zero && _wasStuck)
            {
                _jamDirectionProbe = _jamDirectionProbe * Mathf.Exp(-1f * dt) + new Vector2(mouseDelta.x, 0f);
                if (Mathf.Abs(_jamDirectionProbe.x) >= jamDirectionPickPixels)
                    _jamDirection = new Vector2(Mathf.Sign(_jamDirectionProbe.x), 0f);
            }
            if (_jamDirection != Vector2.zero)
            {
                float pryPushSpeed = dt > 0f ? Vector2.Dot(mouseDelta, _jamDirection) / dt : 0f;
                _pushSpeedSmoothed = Mathf.Lerp(_pushSpeedSmoothed, pryPushSpeed, follow);
                _pushGain = SteadyPushGain(_pushSpeedSmoothed);
                if (_pushGain > 0f)
                    _jamForce += _pushGain * dt;
                else if (_pushSpeedSmoothed <= -steadyPushMinSpeed)
                    _jamForce -= pushBackPenalty * dt;
                else
                    _jamForce -= stuckForceBleedPerSecond * dt;
                _jamForce = Mathf.Max(0f, _jamForce);
            }
        }
        _pushActivity = Mathf.Lerp(_pushActivity, _pushGain, follow);
        bool pushing = _pushGain > 0f;
        if (pushing && !_wasPushing && Time.time - _lastPushOnsetTime >= pushOnsetCooldown)
        {
            _lastPushOnsetTime = Time.time;
            if (_cameraKick != null)
                _cameraKick.Kick(pushOnsetKickDegrees, pushOnsetKickFrequency, pushOnsetKickDuration);
        }
        _wasPushing = pushing;
        if (!stuck)
        {
            _pushSpeedSmoothed = 0f;
            if (_jamForce > 0f)
                _jamForce = Mathf.Max(0f, _jamForce - stuckForceBleedPerSecond * dt);
        }
        if (stuck)
        {
            if (_jamForce >= _jamForceRequired)
            {
                Vector2 leanBaked = _aimAngles + JamLeanDirection() * stuckLeanDegrees;
                _aimAngles = new Vector2(
                    Mathf.Clamp(leanBaked.x, -aimConeDegrees, aimConeDegrees),
                    Mathf.Clamp(leanBaked.y, -aimConeDegrees, aimConeDegrees));
                _jammed = false;
                _jamForce = 0f;
                _sessionFillLeft = Mathf.Max(0.01f, maxFillPerPush);
                if (_finalJam)
                    _breakFill = 1f;
                else
                {
                    AddBreakFill(stuckBreakFill);
                    PlayJamBreak();
                }
                _release = 1f;
                _tension = 0f;
                stuck = false;
                canWiggle = true;
            }
        }

        float strain = stuck ? Mathf.Clamp01(_jamForce / Mathf.Max(0.01f, _jamForceRequired)) : 0f;
        _tension = Mathf.Lerp(_tension, strain, 1f - Mathf.Exp(-tensionSmooth * Time.deltaTime));
        _release = Mathf.Lerp(_release, 0f, 1f - Mathf.Exp(-releaseDecay * Time.deltaTime));
        ApplyCameraFeel();

        // Aim freely when not seated or wiggling; jammed, the mouse can't steer it at all.
        float aimScale = seated && !canWiggle ? 0f : 1f;

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

        Vector2 savedAim = _aimAngles;
        if (stuck)
            _aimAngles += JamLeanDirection() * (stuckLeanDegrees * strain);
        if (stuck && stuckTrembleDegrees > 0f)
            _aimAngles += UnityEngine.Random.insideUnitCircle * (stuckTrembleDegrees * _pushActivity * (pushShakeFloor + (1f - pushShakeFloor) * strain));
        ApplyAimPose(_insertProgress * insertDepth + _release * releaseDepthPunch);
        _aimAngles = savedAim;

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
            PlayStuckCrack();
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

        if (canWiggle && mouseDelta.sqrMagnitude > 0f)
        {
            float want = mouseDelta.magnitude * mouseFillPerPixel;
            float add = Mathf.Min(want, _sessionFillLeft);
            // Wiggling alone never opens the lock; the final jam must be forced.
            float roomBeforeFull = 0.99f - _breakFill;
            if (add >= roomBeforeFull)
            {
                add = Mathf.Max(0f, roomBeforeFull);
                _sessionFillLeft = 0f;
            }
            if (add > 0f)
            {
                _sessionFillLeft = Mathf.Max(0f, _sessionFillLeft - add);
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

    void PlayStuckCrack()
    {
        if (stuckCrackSfx == null || stuckCrackSfx.Length == 0 || SoundManager.Instance == null)
            return;
        AudioClip clip = stuckCrackSfx[UnityEngine.Random.Range(0, stuckCrackSfx.Length)];
        if (clip == null)
            return;
        float pitch = UnityEngine.Random.Range(stuckCrackPitchRange.x, stuckCrackPitchRange.y);
        SoundManager.Instance.PlaySFXAt(clip, keyhole.position, stuckCrackVolume, pitch);
    }

    /// <summary>1 inside the steady push range, fading to 0 at twice the max speed; 0 below the min.</summary>
    float SteadyPushGain(float pushSpeed)
    {
        if (pushSpeed < steadyPushMinSpeed)
            return 0f;
        if (pushSpeed <= steadyPushMaxSpeed)
            return 1f;
        return Mathf.Clamp01(2f - pushSpeed / Mathf.Max(1f, steadyPushMaxSpeed));
    }

    /// <summary>Pry direction in aim space (zero until the player commits to one).</summary>
    Vector2 JamLeanDirection()
    {
        return new Vector2(
            _jamDirection.x * (invertAimX ? -1f : 1f),
            _jamDirection.y * (invertAimY ? -1f : 1f));
    }

    void ApplyCameraFeel()
    {
        if (lockpickCamera == null)
            return;

        var lens = lockpickCamera.Lens;
        lens.FieldOfView = _baseFov - _tension * tensionFovSqueeze + _release * releaseFovKick;
        lockpickCamera.Lens = lens;

        if (_cameraNoise != null)
        {
            // Tension shake only while pushing the right way; it grows with the built-up force.
            float shake = _pushActivity * (pushShakeFloor + (1f - pushShakeFloor) * _tension);
            if (_finalJam)
                shake *= finalJamShakeScale;
            _cameraNoise.AmplitudeGain = _baseNoiseAmplitude + shake * tensionNoiseAmplitude + _release * releaseNoiseAmplitude;
            _cameraNoise.FrequencyGain = _baseNoiseFrequency + shake * tensionNoiseFrequency + _release * releaseNoiseFrequency;
        }
    }

    void ResetCameraFeel()
    {
        _tension = 0f;
        _release = 0f;
        _pushActivity = 0f;
        if (lockpickCamera == null || _baseFov <= 0f)
            return;
        ApplyCameraFeel();
    }

    void PlayJamBreak()
    {
        AudioClip[] pool = jamBreakSfx != null && jamBreakSfx.Length > 0 ? jamBreakSfx : stuckCrackSfx;
        if (pool == null || pool.Length == 0 || SoundManager.Instance == null)
            return;
        AudioClip clip = pool[UnityEngine.Random.Range(0, pool.Length)];
        if (clip == null)
            return;
        float pitch = UnityEngine.Random.Range(stuckCrackPitchRange.x, stuckCrackPitchRange.y);
        SoundManager.Instance.PlaySFXAt(clip, keyhole.position, jamBreakVolume, pitch);
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
        if (progressPanel == null) return;
        if (_progressFade == null)
            _progressFade = new UIFadeHelper(progressPanel);
        _progressFade.SetVisible(visible);
    }

    void TeardownOwnedProgressUi()
    {
        if (progressPanel != null)
            progressPanel.SetActive(false);
        if (!_ownsProgressUi)
            return;

        if (progressPanel != null)
            Destroy(progressPanel);

        progressPanel = null;
        progressSlider = null;
        progressFillImage = null;
        _progressFade = null;
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
        if (stuck && _finalJam && finalJamLoopSfx != null)
            clip = finalJamLoopSfx;
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

        float minVol = LoopMinVolume(stuck);
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
        float smooth = Mathf.Max(1f, stuck ? stuckLoopVolumeSmooth : holdLoopVolumeSmooth);
        _holdLoopMoveSmoothed = Mathf.Lerp(_holdLoopMoveSmoothed, move, 1f - Mathf.Exp(-smooth * Time.deltaTime));

        float minVol = LoopMinVolume(stuck);
        float maxVol = !stuck ? holdLoopMaxVolume
            : _finalJam && finalJamLoopSfx != null ? finalLoopMaxVolume
            : stuckLoopMaxVolume;
        float mouseForMax = stuck ? stuckLoopMouseForMaxVolume : holdLoopMouseForMaxVolume;

        // Jammed: the loop only sounds while the push is actually making progress.
        float t = stuck ? _pushGain
            : mouseForMax > 0.01f ? Mathf.Clamp01(_holdLoopMoveSmoothed / mouseForMax)
            : 1f;
        float target = Mathf.Lerp(minVol, maxVol, t) * sfxVolume;

        _holdLoopVolumeSmoothed = Mathf.Lerp(_holdLoopVolumeSmoothed, target, 1f - Mathf.Exp(-smooth * Time.deltaTime));
        _loopSource.volume = _holdLoopVolumeSmoothed;
    }

    float LoopMinVolume(bool stuck)
    {
        if (!stuck)
            return holdLoopMinVolume;
        return _finalJam && finalJamLoopSfx != null ? finalLoopMinVolume : stuckLoopMinVolume;
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
        SetProgressVisible(false);
        yield return null;
        SetProgressVisible(false);
        yield return null;

        if (lockpickCamera != null)
            lockpickCamera.Priority = 0;

        float wait = Mathf.Max(0.5f, cameraBlendOutSeconds);
        float t = 0f;
        while (t < wait)
        {
            SetProgressVisible(false);
            float settle = 1f - Mathf.Exp(-releaseDecay * Time.unscaledDeltaTime);
            _release = Mathf.Lerp(_release, 0f, settle);
            _tension = Mathf.Lerp(_tension, 0f, settle);
            _pushActivity = Mathf.Lerp(_pushActivity, 0f, settle);
            ApplyCameraFeel();
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
        ResetCameraFeel();
        if (_cameraKick != null)
        {
            Destroy(_cameraKick);
            _cameraKick = null;
        }
        DestroySpawnedVisual();
        if (_progressFade != null)
            _progressFade.SetVisible(false, instant: true);
        TeardownOwnedProgressUi();

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
            fallback = "It's jammed. Force it, or pull out and shove again to loosen it.";
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
