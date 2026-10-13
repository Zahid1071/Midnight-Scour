using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Central thunder / storm cue: telegraph flicker, strike flash, lamp swing,
/// thunder SFX, and camera shake. Assign lights + <see cref="LampSwing"/>s here
/// instead of putting flicker logic on every lamp.
/// </summary>
public class ThunderManager : MonoBehaviour
{
    public static ThunderManager Instance { get; private set; }

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Targets")]
    [SerializeField] Light[] lights;
    [Tooltip("Optional. If empty, MeshRenderers under each light's parent are used.")]
    [SerializeField] Renderer[] emissionRenderers;
    [SerializeField] LampSwing[] swings;

    [Header("Audio")]
    [SerializeField] AudioClip thunderSfx;
    [SerializeField] float thunderVolume = 1f;
    [SerializeField] Vector2 thunderPitchRange = new Vector2(0.95f, 1.05f);

    [Header("Camera Shake")]
    [SerializeField] CinemachineCamera shakeCamera;
    [SerializeField] float shakeDegrees = 1.6f;
    [SerializeField] float shakeDuration = 0.55f;

    [Header("Timing")]
    [SerializeField] float telegraphDuration = 2.5f;
    [SerializeField] float strikeSwingStrength = 1f;
    [Tooltip("Seconds after thunder SFX starts during which the butcher ignores sounds (perfect hammer timing).")]
    [SerializeField] float strikeMaskWindow = 1.5f;
    [Tooltip("Hammer may strike this many seconds before the thunder clap.")]
    [SerializeField] float viseStrikeWindowPre = 1f;
    [Tooltip("Hammer may still strike this many seconds after the perfect (mask) window ends.")]
    [SerializeField] float viseStrikeWindowPost = 1f;

    [Header("Idle Flicker")]
    [SerializeField] bool idleFlicker = true;
    [Range(0f, 0.5f)]
    [SerializeField] float idleAmount = 0.06f;
    [SerializeField] float idleSpeed = 1.2f;

    [Header("Telegraph Flicker")]
    [Range(0f, 1f)]
    [SerializeField] float telegraphMinMultiplier = 0.05f;
    [SerializeField] float telegraphMaxMultiplier = 1.15f;
    [SerializeField] Vector2 telegraphFlashInterval = new Vector2(0.04f, 0.18f);

    [Header("Strike Flicker")]
    [SerializeField] int strikeFlashCount = 3;
    [SerializeField] float strikePeakMultiplier = 2.2f;
    [SerializeField] Vector2 strikeFlashOnTime = new Vector2(0.03f, 0.08f);
    [SerializeField] Vector2 strikeFlashOffTime = new Vector2(0.04f, 0.12f);

    [Header("Emission")]
    [SerializeField] bool driveEmission = true;

    [Header("Auto Schedule")]
    [SerializeField] bool autoPlay = true;
    [Tooltip("Off = no ambient or cage-door thunder; it only strikes to help at the vise and the toilet.")]
    [SerializeField] bool ambientThunder = true;
    [Tooltip("Seconds between ambient thunders (min/max).")]
    [SerializeField] Vector2 calmInterval = new Vector2(40f, 60f);
    [Tooltip("Seconds between thunders while the player is set up at the vise (see Vise Assist conditions).")]
    [FormerlySerializedAs("hammerInterval")]
    [SerializeField] Vector2 viseAssistInterval = new Vector2(8f, 15f);
    [Tooltip("Delay before the first ambient thunder, first cage-door thunder, and first vise-assist thunder.")]
    [SerializeField] Vector2 initialDelay = new Vector2(8f, 20f);

    [Header("Story Cues")]
    [Tooltip("Cage door / any SimpleOpenClose. First open schedules one thunder after Initial Delay (not instantly).")]
    [SerializeField] FS_Atmo.SimpleOpenClose[] firstOpenThunderDoors;
    [Tooltip("If First Open doors are empty, auto-use Cage.cageDoor from the scene.")]
    [SerializeField] bool autoUseCageDoor = true;
    [Tooltip("Required in inventory for vise-assist thunder cadence.")]
    [SerializeField] InventoryItem hammerItem;
    [Tooltip("Required in inventory for the first-time toilet proximity thunder cue.")]
    [SerializeField] InventoryItem plungerItem;
    [Tooltip("Toilet (or Dirty Water). Auto-finds ToiletWaterFlush if unset.")]
    [SerializeField] Transform plungerToilet;
    [Tooltip("Player must be within this distance of the toilet (with plunger) to fire the cue once.")]
    [SerializeField] float plungerToiletRange = 2f;
    [Tooltip("Seconds after first toilet approach before the first story thunder.")]
    [SerializeField] float plungerToiletFirstDelay = 10f;
    [Tooltip("Seconds after the first story thunder before the second.")]
    [SerializeField] float plungerToiletSecondDelay = 30f;

    [Header("Vise Assist Cadence")]
    [Tooltip("Player must be within this distance of the bench vise for the faster thunder interval.")]
    [SerializeField] float viseAssistRange = 2f;
    [Tooltip("Optional. Auto-finds BenchVicePuzzle.Instance if unset.")]
    [SerializeField] BenchVicePuzzle benchVice;
    [Tooltip("Optional. Auto-finds the first NPCController if unset.")]
    [SerializeField] NPCController butcher;

    float[] _baseIntensities;
    Color[] _baseEmissionColors;
    Renderer[] _resolvedEmissionRenderers;
    MaterialPropertyBlock _mpb;
    float _noiseSeed;
    float _stormMultiplier = 1f;
    bool _stormOverride;
    Coroutine _stormRoutine;
    Coroutine _shakeRoutine;
    Coroutine _scheduleRoutine;
    Coroutine _firstDoorThunderRoutine;
    Coroutine _plungerToiletThunderRoutine;
    float _strikeMaskUntil;
    float _plannedStrikeTime = -1f;
    bool _ready;
    bool _firstDoorThunderPlayed;
    bool _stormScheduleUnlocked;
    bool _hammerStrikeCompleted;
    bool _plungerToiletCuePlayed;
    bool _usingViseAssistCadence;
    bool _viseAssistIntroDone;
    bool _viseAssistEverMet;
    float _nextTimingHintAt = -1f;
    const int CaptiveLayer = 12;

    /// <summary>True during / briefly after the thunder strike SFX (butcher ignores player noises).</summary>
    public bool IsLoudNoiseMasked => Time.time <= _strikeMaskUntil;

    /// <summary>True while thunder is covering player-made noises for all NPCs.</summary>
    public static bool ArePlayerNoisesMasked =>
        Instance != null && Instance.IsLoudNoiseMasked;

    /// <summary>
    /// True from <see cref="viseStrikeWindowPre"/> before the clap through
    /// <see cref="viseStrikeWindowPost"/> after the perfect mask window ends.
    /// </summary>
    public bool IsInViseHammerWindow
    {
        get
        {
            if (_plannedStrikeTime < 0f)
                return false;

            float windowStart = _plannedStrikeTime - Mathf.Max(0f, viseStrikeWindowPre);

            // Mask end = real mask expiry once the clap has fired; otherwise planned mask end.
            float maskEnd = _strikeMaskUntil > _plannedStrikeTime - 0.01f
                ? _strikeMaskUntil
                : _plannedStrikeTime + Mathf.Max(0.05f, strikeMaskWindow);

            float windowEnd = maskEnd + Mathf.Max(0f, viseStrikeWindowPost);
            return Time.time >= windowStart && Time.time <= windowEnd;
        }
    }

    public static bool IsViseHammerWindowOpen =>
        Instance != null && Instance.IsInViseHammerWindow;

    /// <summary>In the strike window, before the clap / mask — hitting now is early.</summary>
    public static bool IsViseHammerEarly
    {
        get
        {
            if (Instance == null || !Instance.IsInViseHammerWindow)
                return false;
            if (Instance.IsLoudNoiseMasked)
                return false;
            return Time.time < Instance._plannedStrikeTime;
        }
    }

    /// <summary>In the strike window, after the perfect mask ended — hitting now is late.</summary>
    public static bool IsViseHammerLate
    {
        get
        {
            if (Instance == null || !Instance.IsInViseHammerWindow)
                return false;
            if (Instance.IsLoudNoiseMasked)
                return false;
            return Time.time >= Instance._plannedStrikeTime;
        }
    }

    public bool IsStormPlaying => _stormRoutine != null;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _noiseSeed = Random.Range(0f, 1000f);
        CacheTargets();
    }

    void Start()
    {
        EvaluateViseAssistCadence();
        // Ambient thunder stays off until the cage-door story cue unlocks the schedule.
    }

    void OnEnable()
    {
        InventoryManager.OnInventoryChanged += OnInventoryChanged;
        SubscribeStoryCues();
    }

    void OnDisable()
    {
        InventoryManager.OnInventoryChanged -= OnInventoryChanged;
        UnsubscribeStoryCues();
        StopSchedule();
        StopFirstDoorThunder();
        StopPlungerToiletThunder();
    }

    void OnDestroy()
    {
        InventoryManager.OnInventoryChanged -= OnInventoryChanged;
        UnsubscribeStoryCues();
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (!_ready)
            return;

        EvaluateViseAssistCadence();
        TryTriggerPlungerToiletCue();
        TryShowThunderTimingHint();

        float multiplier = _stormOverride ? _stormMultiplier : EvaluateIdleMultiplier();
        ApplyVisuals(multiplier);
    }

    /// <summary>Telegraph → strike (SFX, shake, swing, flash).</summary>
    public void PlayFullThunder(float telegraphSeconds = -1f)
    {
        if (telegraphSeconds <= 0f)
            telegraphSeconds = telegraphDuration;

        _plannedStrikeTime = Time.time + Mathf.Max(0.05f, telegraphSeconds);
        RestartStorm(FullThunderRoutine(telegraphSeconds));
    }

    /// <summary>
    /// Call when the player successfully frees the wrench with the hammer.
    /// Returns thunder cadence to the calm interval permanently.
    /// </summary>
    public void NotifyHammerUsedSuccessfully()
    {
        _hammerStrikeCompleted = true;
        SetViseAssistCadence(false);
        if (autoPlay)
            RestartSchedule(useInitialDelay: false);
    }

    /// <summary>Optional manual cue — plays once and resets the ambient timer.</summary>
    public void NotifyStoryBeatThunder()
    {
        PlayFullThunder();
        if (autoPlay)
            RestartSchedule(useInitialDelay: false);
    }

    public void PlayTelegraph(float duration = -1f)
    {
        if (duration <= 0f)
            duration = telegraphDuration;

        RestartStorm(TelegraphRoutine(duration));
    }

    public void PlayStrike()
    {
        _plannedStrikeTime = Time.time;
        RestartStorm(StrikeRoutine());
    }

    public void StopStorm()
    {
        if (_stormRoutine != null)
        {
            StopCoroutine(_stormRoutine);
            _stormRoutine = null;
        }

        _stormOverride = false;
        _stormMultiplier = 1f;
        ApplyVisuals(1f);
    }

#if UNITY_EDITOR
    [ContextMenu("Auto Collect Targets From Scene")]
    void ContextAutoCollect()
    {
        swings = FindObjectsByType<LampSwing>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        var lightList = new List<Light>();
        foreach (var swing in swings)
        {
            if (swing == null) continue;
            lightList.AddRange(swing.GetComponentsInChildren<Light>(true));
        }

        lights = lightList.ToArray();
        emissionRenderers = null;
        CacheTargets();
        Debug.Log($"[ThunderManager] Collected {swings.Length} swings, {lights.Length} lights.", this);
    }

    [ContextMenu("Test Full Thunder")]
    void ContextFullThunder() => PlayFullThunder();

    [ContextMenu("Test Telegraph")]
    void ContextTelegraph() => PlayTelegraph();

    [ContextMenu("Test Strike")]
    void ContextStrike() => PlayStrike();

    [ContextMenu("Restart Auto Schedule")]
    void ContextRestartSchedule() => RestartSchedule(useInitialDelay: true);
#endif

    void SubscribeStoryCues()
    {
        ResolveFirstOpenDoors();

        if (firstOpenThunderDoors == null)
            return;

        for (int i = 0; i < firstOpenThunderDoors.Length; i++)
        {
            var door = firstOpenThunderDoors[i];
            if (door == null) continue;
            door.OnOpenSoundPlayed -= OnTrackedDoorOpened;
            door.OnOpenSoundPlayed += OnTrackedDoorOpened;
        }
    }

    void UnsubscribeStoryCues()
    {
        if (firstOpenThunderDoors == null)
            return;

        for (int i = 0; i < firstOpenThunderDoors.Length; i++)
        {
            var door = firstOpenThunderDoors[i];
            if (door == null) continue;
            door.OnOpenSoundPlayed -= OnTrackedDoorOpened;
        }
    }

    void ResolveFirstOpenDoors()
    {
        if (firstOpenThunderDoors != null && firstOpenThunderDoors.Length > 0)
        {
            for (int i = 0; i < firstOpenThunderDoors.Length; i++)
            {
                if (firstOpenThunderDoors[i] != null)
                    return;
            }
        }

        if (!autoUseCageDoor)
            return;

        var cage = FindFirstObjectByType<Cage>();
        if (cage != null && cage.cageDoor != null)
            firstOpenThunderDoors = new[] { cage.cageDoor };
    }

    void OnTrackedDoorOpened()
    {
        if (_firstDoorThunderPlayed || !ambientThunder)
            return;

        _firstDoorThunderPlayed = true;
        _stormScheduleUnlocked = true;

        // Pause ambient so it doesn't strike during the door cue wait.
        StopSchedule();
        StopFirstDoorThunder();
        _firstDoorThunderRoutine = StartCoroutine(FirstDoorThunderRoutine());
    }

    IEnumerator FirstDoorThunderRoutine()
    {
        float wait = Random.Range(
            Mathf.Min(initialDelay.x, initialDelay.y),
            Mathf.Max(initialDelay.x, initialDelay.y));
        if (wait > 0f)
            yield return new WaitForSeconds(wait);

        _firstDoorThunderRoutine = null;
        PlayFullThunder();

        if (autoPlay)
            RestartSchedule(useInitialDelay: false);
    }

    void StopFirstDoorThunder()
    {
        if (_firstDoorThunderRoutine == null)
            return;

        StopCoroutine(_firstDoorThunderRoutine);
        _firstDoorThunderRoutine = null;
    }

    void TryTriggerPlungerToiletCue()
    {
        if (_plungerToiletCuePlayed || _plungerToiletThunderRoutine != null)
            return;
        if (plungerItem == null)
            return;
        if (!InventoryHasItem(plungerItem))
            return;

        var toilet = ResolvePlungerToilet();
        if (toilet == null)
            return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

        if (Vector3.Distance(player.transform.position, toilet.position) > plungerToiletRange)
            return;

        _plungerToiletCuePlayed = true;
        // Pause ambient so random strikes don't land during the story waits.
        StopSchedule();
        StopPlungerToiletThunder();
        _plungerToiletThunderRoutine = StartCoroutine(PlungerToiletThunderRoutine());
    }

    IEnumerator PlungerToiletThunderRoutine()
    {
        float firstWait = Mathf.Max(0f, plungerToiletFirstDelay);
        if (firstWait > 0f)
            yield return new WaitForSeconds(firstWait);

        PlayFullThunder();
        while (IsStormPlaying)
            yield return null;

        float secondWait = Mathf.Max(0f, plungerToiletSecondDelay);
        if (secondWait > 0f)
            yield return new WaitForSeconds(secondWait);

        PlayFullThunder();
        while (IsStormPlaying)
            yield return null;

        _plungerToiletThunderRoutine = null;

        if (autoPlay)
            RestartSchedule(useInitialDelay: false);
    }

    void StopPlungerToiletThunder()
    {
        if (_plungerToiletThunderRoutine == null)
            return;

        StopCoroutine(_plungerToiletThunderRoutine);
        _plungerToiletThunderRoutine = null;
    }

    Transform ResolvePlungerToilet()
    {
        if (plungerToilet != null)
            return plungerToilet;

        var flush = FindFirstObjectByType<ToiletWaterFlush>();
        if (flush != null)
            plungerToilet = flush.transform;
        return plungerToilet;
    }

    void OnInventoryChanged() => EvaluateViseAssistCadence();

    void EvaluateViseAssistCadence()
    {
        SetViseAssistCadence(ShouldUseViseAssistCadence());
    }

    bool ShouldUseViseAssistCadence()
    {
        if (_hammerStrikeCompleted || hammerItem == null)
            return false;

        if (!InventoryHasHammer())
            return false;

        var vise = ResolveBenchVice();
        if (vise == null)
            return false;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return false;

        if (Vector3.Distance(player.transform.position, vise.transform.position) > viseAssistRange)
            return false;

        if (player.layer == CaptiveLayer)
            return false;

        if (Cage.IsPlayerInsideAnyCage(player.transform))
            return false;

        var hider = player.GetComponent<PlayerHider>();
        if (hider != null && hider.isHidden)
            return false;

        var npc = ResolveButcher();
        if (npc == null || npc.currentState != NPCController.AIState.Natural)
            return false;

        return true;
    }

    bool InventoryHasHammer() => InventoryHasItem(hammerItem);

    bool InventoryHasItem(InventoryItem item)
    {
        if (item == null)
            return false;

        var inv = InventoryManager.Instance?.GetInventory();
        if (inv == null)
            return false;

        for (int i = 0; i < inv.Count; i++)
        {
            if (inv[i] == item)
                return true;
        }

        return false;
    }

    BenchVicePuzzle ResolveBenchVice()
    {
        if (benchVice == null)
            benchVice = BenchVicePuzzle.Instance != null
                ? BenchVicePuzzle.Instance
                : FindFirstObjectByType<BenchVicePuzzle>();
        return benchVice;
    }

    NPCController ResolveButcher()
    {
        if (butcher == null)
            butcher = FindFirstObjectByType<NPCController>();
        return butcher;
    }

    void SetViseAssistCadence(bool urgent)
    {
        if (_usingViseAssistCadence == urgent)
            return;

        _usingViseAssistCadence = urgent;
        if (!autoPlay || !isActiveAndEnabled)
            return;

        if (urgent)
        {
            if (!_viseAssistEverMet)
            {
                _viseAssistEverMet = true;
                _nextTimingHintAt = Time.time + 90f;
            }

            // First time at the vise with the hammer: wait Initial Delay, then strike.
            // After that intro, later assist cycles use Vise Assist Interval.
            RestartSchedule(useInitialDelay: !_viseAssistIntroDone);
        }
        else
        {
            RestartSchedule(useInitialDelay: false);
        }
    }

    void TryShowThunderTimingHint()
    {
        if (!_viseAssistEverMet || _hammerStrikeCompleted)
            return;
        if (_nextTimingHintAt < 0f || Time.time < _nextTimingHintAt)
            return;
        if (hammerItem == null)
            return;
        if (InventoryUI.Instance == null || InventoryUI.Instance.GetSelectedItem() != hammerItem)
            return;

        var hud = HudManager.Instance;
        if (hud != null)
            hud.ShowSubtitle(Loc.Get("gameplay.hammer_time_with_thunder"), 4f, hud.WarningColor);

        _nextTimingHintAt = Time.time + 30f;
    }

    void RestartSchedule(bool useInitialDelay)
    {
        StopSchedule();
        if (!autoPlay || !isActiveAndEnabled)
            return;
        // No ambient loop until the cage door (or an explicit unlock) has happened.
        if (ambientThunder ? !_stormScheduleUnlocked : !_usingViseAssistCadence)
            return;
        // Plunger-toilet story cue owns timing until both strikes finish.
        if (_plungerToiletThunderRoutine != null)
            return;

        _scheduleRoutine = StartCoroutine(ScheduleRoutine(useInitialDelay));
    }

    void StopSchedule()
    {
        if (_scheduleRoutine == null)
            return;

        StopCoroutine(_scheduleRoutine);
        _scheduleRoutine = null;
    }

    IEnumerator ScheduleRoutine(bool useInitialDelay)
    {
        // First wait is either Initial Delay (intro) or the normal cadence gap — not both.
        bool nextWaitIsIntro = useInitialDelay;

        while (autoPlay)
        {
            float wait;
            if (nextWaitIsIntro)
            {
                wait = Random.Range(
                    Mathf.Min(initialDelay.x, initialDelay.y),
                    Mathf.Max(initialDelay.x, initialDelay.y));
                nextWaitIsIntro = false;
            }
            else
            {
                Vector2 range = _usingViseAssistCadence ? viseAssistInterval : calmInterval;
                wait = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
            }

            if (wait > 0f)
                yield return new WaitForSeconds(wait);

            while (IsStormPlaying)
                yield return null;

            if (!ambientThunder && !_usingViseAssistCadence)
                break;

            bool assistStrike = _usingViseAssistCadence;
            PlayFullThunder();

            if (assistStrike)
                _viseAssistIntroDone = true;

            while (IsStormPlaying)
                yield return null;
        }

        _scheduleRoutine = null;
    }

    void CacheTargets()
    {
        _ready = false;

        if (lights == null)
            lights = System.Array.Empty<Light>();

        _baseIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            _baseIntensities[i] = lights[i] != null ? lights[i].intensity : 0f;

        ResolveEmissionRenderers();
        _mpb = new MaterialPropertyBlock();
        _ready = true;
    }

    void ResolveEmissionRenderers()
    {
        if (!driveEmission)
        {
            _resolvedEmissionRenderers = System.Array.Empty<Renderer>();
            _baseEmissionColors = System.Array.Empty<Color>();
            return;
        }

        if (emissionRenderers != null && emissionRenderers.Length > 0)
        {
            _resolvedEmissionRenderers = emissionRenderers;
        }
        else
        {
            var set = new List<Renderer>();
            foreach (var light in lights)
            {
                if (light == null || light.transform.parent == null)
                    continue;

                var renderers = light.transform.parent.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                {
                    if (r != null && !set.Contains(r))
                        set.Add(r);
                }
            }

            _resolvedEmissionRenderers = set.ToArray();
        }

        _baseEmissionColors = new Color[_resolvedEmissionRenderers.Length];
        for (int i = 0; i < _resolvedEmissionRenderers.Length; i++)
        {
            var r = _resolvedEmissionRenderers[i];
            if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty(EmissionColorId))
            {
                _baseEmissionColors[i] = Color.black;
                continue;
            }

            _baseEmissionColors[i] = r.sharedMaterial.GetColor(EmissionColorId);
        }
    }

    float EvaluateIdleMultiplier()
    {
        if (!idleFlicker || idleAmount <= 0.0001f)
            return 1f;

        float n = Mathf.PerlinNoise(_noiseSeed, Time.time * idleSpeed);
        return 1f + (n * 2f - 1f) * idleAmount;
    }

    void ApplyVisuals(float multiplier)
    {
        multiplier = Mathf.Max(0f, multiplier);

        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] == null) continue;
            lights[i].intensity = _baseIntensities[i] * multiplier;
        }

        if (!driveEmission || _resolvedEmissionRenderers == null)
            return;

        for (int i = 0; i < _resolvedEmissionRenderers.Length; i++)
        {
            var r = _resolvedEmissionRenderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(EmissionColorId, _baseEmissionColors[i] * multiplier);
            r.SetPropertyBlock(_mpb);
        }
    }

    void RestartStorm(IEnumerator routine)
    {
        StopStorm();
        _stormRoutine = StartCoroutine(StormWrapper(routine));
    }

    IEnumerator StormWrapper(IEnumerator routine)
    {
        yield return routine;
        _stormOverride = false;
        _stormMultiplier = 1f;
        _stormRoutine = null;
    }

    IEnumerator FullThunderRoutine(float telegraphSeconds)
    {
        yield return TelegraphRoutine(telegraphSeconds);
        yield return StrikeRoutine();
    }

    IEnumerator TelegraphRoutine(float duration)
    {
        _stormOverride = true;
        float end = Time.time + Mathf.Max(0.05f, duration);

        while (Time.time < end)
        {
            bool dip = Random.value < 0.65f;
            _stormMultiplier = dip
                ? Random.Range(telegraphMinMultiplier, Mathf.Lerp(telegraphMinMultiplier, 0.55f, 0.5f))
                : Random.Range(0.75f, telegraphMaxMultiplier);

            yield return new WaitForSeconds(
                Random.Range(telegraphFlashInterval.x, telegraphFlashInterval.y));
        }
    }

    IEnumerator StrikeRoutine()
    {
        _stormOverride = true;
        _plannedStrikeTime = Time.time;
        _strikeMaskUntil = Time.time + Mathf.Max(0.05f, strikeMaskWindow);

        PlayThunderSfx();
        ImpulseSwings();
        StartCameraShake();

        int flashes = Mathf.Max(1, strikeFlashCount);
        for (int i = 0; i < flashes; i++)
        {
            _stormMultiplier = strikePeakMultiplier * Random.Range(0.85f, 1.1f);
            yield return new WaitForSeconds(Random.Range(strikeFlashOnTime.x, strikeFlashOnTime.y));

            _stormMultiplier = Random.Range(0f, 0.2f);
            yield return new WaitForSeconds(Random.Range(strikeFlashOffTime.x, strikeFlashOffTime.y));
        }
    }

    void PlayThunderSfx()
    {
        if (thunderSfx == null || SoundManager.Instance == null)
            return;

        float pitch = Random.Range(thunderPitchRange.x, thunderPitchRange.y);
        SoundManager.Instance.PlaySFX(thunderSfx, thunderVolume, pitch);
    }

    void ImpulseSwings()
    {
        if (swings == null)
            return;

        for (int i = 0; i < swings.Length; i++)
        {
            if (swings[i] != null)
                swings[i].ApplyThunderImpulse(strikeSwingStrength);
        }
    }

    void StartCameraShake()
    {
        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);

        _shakeRoutine = StartCoroutine(CameraShakeRoutine());
    }

    IEnumerator CameraShakeRoutine()
    {
        var cams = CollectShakeCameras();
        if (cams.Count == 0)
        {
            _shakeRoutine = null;
            yield break;
        }

        var baseDutch = new float[cams.Count];
        for (int i = 0; i < cams.Count; i++)
            baseDutch[i] = cams[i].Lens.Dutch;

        float duration = Mathf.Max(0.05f, shakeDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float falloff = 1f - Mathf.Clamp01(elapsed / duration);
            falloff *= falloff;
            float offset = Random.Range(-shakeDegrees, shakeDegrees) * falloff;

            for (int i = 0; i < cams.Count; i++)
            {
                if (cams[i] == null)
                    continue;
                var lens = cams[i].Lens;
                lens.Dutch = baseDutch[i] + offset;
                cams[i].Lens = lens;
            }

            yield return null;
        }

        for (int i = 0; i < cams.Count; i++)
        {
            if (cams[i] == null)
                continue;
            var restore = cams[i].Lens;
            restore.Dutch = baseDutch[i];
            cams[i].Lens = restore;
        }

        _shakeRoutine = null;
    }

    List<CinemachineCamera> CollectShakeCameras()
    {
        var cams = new List<CinemachineCamera>(2);

        var playerCam = shakeCamera != null ? shakeCamera : FindPlayerCamera();
        if (playerCam != null)
            cams.Add(playerCam);

        var hiding = HidingCameraSystem.Instance != null ? HidingCameraSystem.Instance.cmCamera : null;
        if (hiding != null && !cams.Contains(hiding))
            cams.Add(hiding);

        return cams;
    }

    static CinemachineCamera FindPlayerCamera()
    {
        var all = FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var cam in all)
        {
            if (cam != null && cam.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return cam;
        }

        return all.Length > 0 ? all[0] : null;
    }
}
