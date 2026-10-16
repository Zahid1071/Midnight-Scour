using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Shows pre-authored hint panels and runs inventory hint rules.
/// Only one hint is visible at a time.
/// Higher priority replaces the active hint; equal priority → latest wins; lower priority fails while a higher one is showing.
/// </summary>
public class GameplayHintManager : MonoBehaviour
{
    public static GameplayHintManager Instance { get; private set; }

    [Serializable]
    struct HintEntry
    {
        [Tooltip("Name used by Show() and the trigger fields below. Can be anything you like.")]
        public string id;
        public CanvasGroup canvasGroup;
        [Tooltip("Higher priority replaces lower. Equal priority → latest wins. Lower while higher is active → Show fails.")]
        public int priority;
    }

    [Header("Panels")]
    [SerializeField] HintEntry[] panels;
    [Tooltip("How long hint panels take to fade in or out.")]
    [SerializeField] float fadeDuration = 0.25f;

    [Header("Transient hints")]
    [Tooltip("Shown hints automatically hide after this many seconds.")]
    [SerializeField] float transientHintDurationSeconds = 30f;

    [Header("Show spring")]
    [Tooltip("Scale when the hint first pops in. 1 = normal size; lower = starts smaller.")]
    [SerializeField] float springStartScale = 0.6f;
    [Tooltip("Spring pull toward normal size. Higher = faster, snappier bounce.")]
    [SerializeField] float springStiffness = 300f;
    [Tooltip("How fast the bounce settles. Lower = more wobbles; higher = dies out quicker.")]
    [SerializeField] float springDamping = 8f;
    [Tooltip("Initial shove into the bounce. Higher = bigger first overshoot past normal size.")]
    [SerializeField] float springInitialVelocity = 7f;

    [Header("Background flash")]
    [Tooltip("Flash strength at each scale peak. 0 = no flash, 1 = full blend to peak color.")]
    [SerializeField] float flashIntensity = 0.55f;
    [Tooltip("How much brighter/whiter the hint background gets at each flash peak.")]
    [SerializeField] float flashBrightnessBoost = 0.12f;
    [Tooltip("Extra multiplier on flash strength if peaks feel too subtle.")]
    [SerializeField] float springFlashStrength = 1.25f;
    [Tooltip("Bright flashes on scale peaks, then stops. 0 = never flash. Spring bounce still runs.")]
    [Min(0)]
    [SerializeField] int maxFlashCount = 3;

    [Header("Items (optional — matched by asset or name if unset)")]
    [SerializeField] InventoryItem boneItem;
    [SerializeField] InventoryItem gumItem;
    [SerializeField] InventoryItem gummyBoneItem;
    [SerializeField] InventoryItem vaseItem;
    [Tooltip("Granted to inventory at run start if the player earned it last run.")]
    [SerializeField] InventoryItem lockpickItem;

    public InventoryItem LockpickItem => lockpickItem;

    [Header("Combine hint")]
    [Tooltip("Seconds after picking up bone + gum before the combine hint can appear.")]
    [SerializeField] float combineHintDelaySeconds = 5f;

    [Header("Vase drop hint")]
    [Tooltip("Seconds after first vase pickup before the drop hint appears.")]
    [Min(1f)]
    [SerializeField] float vaseDropHintInitialDelaySeconds = 60f;
    [Tooltip("Seconds between drop hint reminders while the vase is still held.")]
    [Min(1f)]
    [SerializeField] float vaseDropHintRepeatIntervalSeconds = 60f;

    [Header("Triggered hints — Id must match a panel Id above")]
    [SerializeField] string hidingSpotHintId = "Hiding Spot";
    [SerializeField] string soundHintId = "Sound";
    [SerializeField] string boneDropHintId = "Drop Item";
    [SerializeField] string boneGumCombineHintId = "Bone Gum Combine";
    [SerializeField] string noteHintId = "Note";
    [Tooltip("Shown once when the butcher searches the same hide spot a second time while the player is still in it.")]
    [SerializeField] string killerPatternHintId = "Killer Pattern Recognition";
    [Tooltip("Shown on lockpick pickup, or the first time the player dies.")]
    [SerializeField] string lockpickHintId = "Lockpick";
    [SerializeField] string lockpickWatchSubtitleKey = "gameplay.lockpick_watch_for_more";
    [SerializeField] float lockpickWatchSubtitleSeconds = 6f;

    readonly Dictionary<string, CanvasGroup> _groups = new();
    readonly Dictionary<string, int> _priorities = new();
    readonly Dictionary<string, Image> _backgrounds = new();
    readonly Dictionary<string, Color> _baseBackgroundColors = new();
    readonly Dictionary<string, Coroutine> _springRoutines = new();
    readonly Dictionary<string, Coroutine> _autoHideRoutines = new();
    readonly List<string> _pendingHints = new();

    string _activeHintId;
    Coroutine _transitionRoutine;

    Coroutine _combineTimerRoutine;
    bool _combineTimerStarted;
    bool _combineHintShown;
    bool _combineFinished;
    float _combineElapsed;
    bool _vaseDropHintFinished;
    bool _vasePickupTracked;
    bool _vaseDropHintShownOnce;
    float _vaseDropHintElapsed;
    bool _hidingSpotHintShown;
    bool _soundHintShown;
    bool _killerPatternHintShown;
    bool _lockpickHintShown;
    bool _lockpickHintRequested;
    bool _lockpickLetterFollowupDone;
    bool _lockpickDeathHintQueued;

    /// <summary>Survives scene reload so first-death hint can show after Restart.</summary>
    static bool s_pendingLockpickHintAfterReload;

    [Header("Note / lockpick rotating hints")]
    [Tooltip("If enabled, alternates note and lockpick hint panels on an interval.")]
    [SerializeField] bool rotatingHintsEnabled = true;
    [Tooltip("Seconds between rotating hint reminders.")]
    [Min(1f)]
    [SerializeField] float rotatingHintIntervalSeconds = 75f;
    float _rotatingHintElapsed;
    bool _nextRotatingHintIsLockpick;

    [Header("Note hint (legacy — prefer rotating hints above)")]
    [Tooltip("If enabled and rotating hints are off, shows the note hint every N seconds.")]
    [SerializeField] bool noteHintEnabled = true;
    [Tooltip("Seconds between note hint reminders.")]
    [Min(1f)]
    [SerializeField] float noteHintIntervalSeconds = 20f;
    float _noteHintElapsed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        RegisterPanels();

        foreach (var group in _groups.Values)
        {
            if (group != null)
                group.alpha = 0f;
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        InventoryManager.OnInventoryChanged += EvaluateHints;
        ItemCombineReceiver.OnCombineSuccess += OnCombineSuccess;
        EvaluateHints();
    }

    void OnDisable()
    {
        InventoryManager.OnInventoryChanged -= EvaluateHints;
        ItemCombineReceiver.OnCombineSuccess -= OnCombineSuccess;
        StopCombineTimerCoroutine();
        StopTransitionRoutine();
        StopAllSpringRoutines();
        StopAllAutoHideRoutines();
    }

    void Update()
    {
        if (PlayerControlGate.IsInputBlocked)
        {
            if (_activeHintId != null || _pendingHints.Count > 0)
                HideAll();
            return;
        }

        TryShowRequestedLockpickHint();
        EvaluateFirstShiftHint();
        EvaluateCombineTimer();
        TickRotatingHints();
        TickNoteHint();
        TickVaseDropHint();
    }

    public void NotifyCageDoorOpenedFirstTime()
    {
        if (_hidingSpotHintShown)
            return;

        if (Show(hidingSpotHintId))
            _hidingSpotHintShown = true;
    }

    /// <summary>Closed the padlock/hairpin letter — subtitle only (hint is on lockpick pickup / first death).</summary>
    public void NotifyPadlockHairpinLetterClosed()
    {
        if (_lockpickLetterFollowupDone)
            return;

        _lockpickLetterFollowupDone = true;
        string msg = Loc.Get(lockpickWatchSubtitleKey);
        if (string.IsNullOrEmpty(msg) || msg == lockpickWatchSubtitleKey)
            msg = "I must keep an eye out to find more lockpicks";
        HudManager.Instance?.ShowSubtitle(msg, lockpickWatchSubtitleSeconds);
    }

    /// <summary>First capture/game-over — teach lockpicks.</summary>
    public void NotifyFirstPlayerDeath()
    {
        if (_lockpickHintShown || _lockpickDeathHintQueued)
            return;

        _lockpickDeathHintQueued = true;
        // Game-over blocks input / hides HUD — show when gameplay is free (Continue)
        // or after Restart reload via static flag.
        s_pendingLockpickHintAfterReload = true;
        RequestLockpickHint();
    }

    void RequestLockpickHint()
    {
        if (_lockpickHintShown)
            return;
        _lockpickHintRequested = true;
    }

    void TryShowRequestedLockpickHint()
    {
        if (s_pendingLockpickHintAfterReload)
        {
            s_pendingLockpickHintAfterReload = false;
            _lockpickHintRequested = true;
        }

        if (!_lockpickHintRequested || _lockpickHintShown)
            return;

        if (Show(lockpickHintId))
        {
            _lockpickHintShown = true;
            _lockpickHintRequested = false;
            s_pendingLockpickHintAfterReload = false;
        }
    }

    /// <summary>World lockpick collected — show the lockpick hint panel.</summary>
    public void NotifyLockpickPickedUp()
    {
        RequestLockpickHint();
        // Pickup can happen while free; try immediately as well as next Update.
        TryShowRequestedLockpickHint();
    }

    public void NotifyItemPickedUp(InventoryItem item)
    {
        if (item == null)
            return;

        if (lockpickItem != null && item == lockpickItem)
            NotifyLockpickPickedUp();
    }

    /// <summary>
    /// Call when search exposure for the player's current hide spot increments.
    /// Shows the pattern hint on the second search of that same spot (when capture odds start rising).
    /// </summary>
    public void NotifyHideSpotSearchExposure(HidingSpot spot, int exposureCount)
    {
        if (_killerPatternHintShown || spot == null || spot.searchCheckpointOnly)
            return;

        // Exposure 1+ = room pressure / spot checks building; hint when capture odds are about to rise.
        if (exposureCount < 2)
            return;

        if (Show(killerPatternHintId))
            _killerPatternHintShown = true;
    }

    void EvaluateFirstShiftHint()
    {
        if (_soundHintShown || !WasShiftPressedThisFrame())
            return;

        if (!IsPlayerOnPlayerLayer())
            return;

        if (Show(soundHintId))
            _soundHintShown = true;
    }

    static bool IsPlayerOnPlayerLayer()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null)
            return false;

        return player.layer == LayerMask.NameToLayer("Player");
    }

    static bool WasShiftPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftShiftKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.LeftShift);
#endif
    }

    void RegisterPanels()
    {
        _groups.Clear();
        _priorities.Clear();
        _backgrounds.Clear();
        _baseBackgroundColors.Clear();

        if (panels == null)
            return;

        foreach (var entry in panels)
        {
            if (string.IsNullOrEmpty(entry.id) || entry.canvasGroup == null)
                continue;

            _groups[entry.id] = entry.canvasGroup;
            _priorities[entry.id] = entry.priority;

            var background = entry.canvasGroup.GetComponent<Image>();
            if (background == null)
                continue;

            _backgrounds[entry.id] = background;
            _baseBackgroundColors[entry.id] = background.color;
        }
    }

    void OnCombineSuccess()
    {
        _combineFinished = true;
        _combineElapsed = 0f;
        StopCombineTimerCoroutine();
        Hide(boneGumCombineHintId);
    }

    void EvaluateHints()
    {
        EvaluateVaseDropHintState();
        EvaluateCombineHint();
    }

    void EvaluateVaseDropHintState()
    {
        if (_vaseDropHintFinished)
            return;

        if (HasVase())
        {
            if (!_vasePickupTracked)
            {
                _vasePickupTracked = true;
                _vaseDropHintElapsed = 0f;
            }

            return;
        }

        if (!_vasePickupTracked)
            return;

        _vaseDropHintFinished = true;
        _vasePickupTracked = false;
        Hide(boneDropHintId);
    }

    void TickVaseDropHint()
    {
        if (_vaseDropHintFinished || !_vasePickupTracked || !HasVase())
            return;

        _vaseDropHintElapsed += Time.unscaledDeltaTime;

        float threshold = _vaseDropHintShownOnce
            ? vaseDropHintRepeatIntervalSeconds
            : vaseDropHintInitialDelaySeconds;

        if (_vaseDropHintElapsed < threshold)
            return;

        _vaseDropHintElapsed = 0f;
        ShowPeriodicVaseDropHint();
        _vaseDropHintShownOnce = true;
    }

    void ShowPeriodicVaseDropHint()
    {
        if (string.IsNullOrEmpty(boneDropHintId) || !_groups.TryGetValue(boneDropHintId, out var group) || group == null)
            return;

        RemovePending(boneDropHintId);

        if (_activeHintId == boneDropHintId)
        {
            StopTransitionRoutine();
            CancelAutoHide(boneDropHintId);
            StopSpring(boneDropHintId);
            group.gameObject.SetActive(true);
            group.alpha = 1f;
            StartSpring(boneDropHintId, group.transform);
            ScheduleAutoHide(boneDropHintId);
            return;
        }

        Show(boneDropHintId);
    }

    void EvaluateCombineHint()
    {
        if (_combineFinished || HasCombinedStickyBone())
        {
            _combineElapsed = 0f;
            StopCombineTimerCoroutine();
            Hide(boneGumCombineHintId);
            return;
        }

        if (_combineHintShown)
            return;

        EvaluateCombineTimer();
    }

    void EvaluateCombineTimer()
    {
        if (_combineFinished || _combineHintShown || HasCombinedStickyBone())
            return;

        if (HasBoneAndGum())
            _combineTimerStarted = true;

        if (!_combineTimerStarted || _combineTimerRoutine != null)
            return;

        _combineTimerRoutine = StartCoroutine(ShowCombineHintAfterDelay());
    }

    IEnumerator ShowCombineHintAfterDelay()
    {
        while (_combineElapsed < combineHintDelaySeconds)
        {
            if (_combineFinished || HasCombinedStickyBone())
            {
                _combineTimerRoutine = null;
                yield break;
            }

            _combineElapsed += Time.deltaTime;
            yield return null;
        }

        _combineTimerRoutine = null;

        if (_combineFinished || HasCombinedStickyBone())
            yield break;

        if (Show(boneGumCombineHintId))
            _combineHintShown = true;
    }

    void StopCombineTimerCoroutine()
    {
        if (_combineTimerRoutine != null)
        {
            StopCoroutine(_combineTimerRoutine);
            _combineTimerRoutine = null;
        }
    }

    public bool Show(string hintId)
    {
        if (PlayerControlGate.IsInputBlocked)
            return false;

        if (string.IsNullOrEmpty(hintId) || !_groups.TryGetValue(hintId, out var group) || group == null)
        {
            Debug.LogWarning($"[GameplayHintManager] No panel registered for id '{hintId}'.");
            return false;
        }

        RefreshLocalizedHintLabel(hintId);

        if (hintId == _activeHintId)
            return true;

        if (_transitionRoutine != null)
            StopTransitionRoutine();

        // Nothing showing — claim the slot immediately so a second Show this frame can't stack.
        if (_activeHintId == null)
        {
            EnsureOnlyHintVisible(hintId);
            _activeHintId = hintId;
            BeginTransition(null, hintId);
            return true;
        }

        int newPriority = GetWeight(hintId);
        int activePriority = GetWeight(_activeHintId);

        // Higher priority replaces. Equal → latest wins. Lower → fail (do not queue).
        if (newPriority < activePriority)
            return false;

        BeginInstantSwap(_activeHintId, hintId);
        return true;
    }

    /// <summary>Force every other hint panel off so two CanvasGroups never both appear.</summary>
    void EnsureOnlyHintVisible(string keepHintId)
    {
        foreach (var kv in _groups)
        {
            if (kv.Value == null || kv.Key == keepHintId)
                continue;

            CancelAutoHide(kv.Key);
            StopSpring(kv.Key);
            kv.Value.alpha = 0f;
            kv.Value.transform.localScale = Vector3.one;
            ResetBackgroundColor(kv.Key);
        }
    }

    void RefreshLocalizedHintLabel(string hintId)
    {
        if (hintId != boneDropHintId || !_groups.TryGetValue(hintId, out var group) || group == null)
            return;

        group.GetComponent<DropItemHintLabel>()?.Refresh();
    }

    public void Hide(string hintId)
    {
        if (string.IsNullOrEmpty(hintId) || !_groups.TryGetValue(hintId, out _))
            return;

        RemovePending(hintId);

        if (_activeHintId != hintId)
            return;

        StopTransitionRoutine();
        BeginTransition(hintId, null);
    }

    public void HideAll()
    {
        StopTransitionRoutine();
        StopAllSpringRoutines();
        StopAllAutoHideRoutines();
        _pendingHints.Clear();
        _activeHintId = null;

        foreach (var kv in _groups)
        {
            if (kv.Value == null)
                continue;

            kv.Value.alpha = 0f;
            kv.Value.transform.localScale = Vector3.one;
            ResetBackgroundColor(kv.Key);
        }
    }

    void BeginTransition(string fromHintId, string toHintId)
    {
        StopTransitionRoutine();
        _transitionRoutine = StartCoroutine(TransitionRoutine(fromHintId, toHintId));
    }

    // Equal or higher priority: cut the current hint immediately, then show the new one.
    void BeginInstantSwap(string fromHintId, string toHintId)
    {
        StopTransitionRoutine();
        EnsureOnlyHintVisible(toHintId);

        if (!string.IsNullOrEmpty(fromHintId) && fromHintId != toHintId)
        {
            CancelAutoHide(fromHintId);
            StopSpring(fromHintId);

            if (_groups.TryGetValue(fromHintId, out var fromGroup) && fromGroup != null)
            {
                fromGroup.alpha = 0f;
                fromGroup.transform.localScale = Vector3.one;
                ResetBackgroundColor(fromHintId);
            }
        }

        // Claim before visuals so concurrent Show() sees an active hint.
        _activeHintId = toHintId;

        if (!string.IsNullOrEmpty(toHintId) && _groups.TryGetValue(toHintId, out var toGroup) && toGroup != null)
        {
            toGroup.gameObject.SetActive(true);
            toGroup.alpha = 1f;
            StartSpring(toHintId, toGroup.transform);
            ScheduleAutoHide(toHintId);
        }
        else
        {
            _activeHintId = null;
        }
    }

    IEnumerator TransitionRoutine(string fromHintId, string toHintId)
    {
        // Claim the destination up front so another Show can't start a second fade-in.
        if (!string.IsNullOrEmpty(toHintId))
            _activeHintId = toHintId;

        if (!string.IsNullOrEmpty(fromHintId) && fromHintId != toHintId)
        {
            CancelAutoHide(fromHintId);
            StopSpring(fromHintId);

            if (_groups.TryGetValue(fromHintId, out var fromGroup) && fromGroup != null)
            {
                yield return FadeGroup(fromGroup, 0f);
                fromGroup.alpha = 0f;
                fromGroup.transform.localScale = Vector3.one;
                ResetBackgroundColor(fromHintId);
            }
        }

        if (!string.IsNullOrEmpty(toHintId) && _groups.TryGetValue(toHintId, out var toGroup) && toGroup != null)
        {
            EnsureOnlyHintVisible(toHintId);
            toGroup.gameObject.SetActive(true);
            StartSpring(toHintId, toGroup.transform);
            yield return FadeGroup(toGroup, 1f);
            toGroup.alpha = 1f;
            _activeHintId = toHintId;
            ScheduleAutoHide(toHintId);
        }
        else if (string.IsNullOrEmpty(toHintId))
        {
            _activeHintId = null;
        }

        _transitionRoutine = null;
    }

    void ScheduleAutoHide(string hintId)
    {
        CancelAutoHide(hintId);
        _autoHideRoutines[hintId] = StartCoroutine(AutoHideAfterDelay(hintId));
    }

    IEnumerator AutoHideAfterDelay(string hintId)
    {
        float elapsed = 0f;

        while (elapsed < transientHintDurationSeconds)
        {
            if (_activeHintId != hintId)
            {
                _autoHideRoutines.Remove(hintId);
                yield break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        _autoHideRoutines.Remove(hintId);

        if (_activeHintId == hintId)
            Hide(hintId);
    }

    void CancelAutoHide(string hintId)
    {
        if (!_autoHideRoutines.TryGetValue(hintId, out var routine))
            return;

        if (routine != null)
            StopCoroutine(routine);

        _autoHideRoutines.Remove(hintId);
    }

    void StopAllAutoHideRoutines()
    {
        foreach (var routine in _autoHideRoutines.Values)
        {
            if (routine != null)
                StopCoroutine(routine);
        }

        _autoHideRoutines.Clear();
    }

    void StartSpring(string hintId, Transform target)
    {
        StopSpring(hintId);
        _springRoutines[hintId] = StartCoroutine(SpringPopRoutine(hintId, target));
    }

    IEnumerator SpringPopRoutine(string hintId, Transform target)
    {
        if (target == null)
            yield break;

        _backgrounds.TryGetValue(hintId, out var background);
        Color baseColor = background != null ? GetBaseBackgroundColor(hintId, background) : default;
        Color peakColor = background != null
            ? new Color(
                Mathf.Min(baseColor.r + flashBrightnessBoost, 1f),
                Mathf.Min(baseColor.g + flashBrightnessBoost, 1f),
                Mathf.Min(baseColor.b + flashBrightnessBoost, 1f),
                Mathf.Min(baseColor.a + 0.18f, 1f))
            : baseColor;

        float scale = springStartScale;
        float velocity = springInitialVelocity;
        target.localScale = Vector3.one * scale;

        bool flashActive = false;
        float lastPeakDelta = 0.001f;
        int flashPeaksSeen = 0;

        while (Mathf.Abs(scale - 1f) > 0.001f || Mathf.Abs(velocity) > 0.001f)
        {
            // Alt-tab / focus regain can deliver a multi-second unscaledDeltaTime that
            // explodes this spring and stretches the hint across the screen.
            if (!Application.isFocused)
            {
                yield return null;
                continue;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float acceleration = -springStiffness * (scale - 1f) - springDamping * velocity;
            float prevVelocity = velocity;
            velocity += acceleration * dt;
            scale += velocity * dt;
            velocity = Mathf.Clamp(velocity, -40f, 40f);
            scale = Mathf.Clamp(scale, 0.25f, 1.75f);
            target.localScale = Vector3.one * scale;

            if (background != null)
            {
                // Peak detected (moving up, then starts moving down)
                if (prevVelocity > 0f && velocity <= 0f && scale > 1f)
                {
                    flashPeaksSeen++;
                    if (flashPeaksSeen <= maxFlashCount)
                    {
                        flashActive = true;
                        lastPeakDelta = Mathf.Max(0.001f, scale - 1f);
                    }
                    else
                    {
                        flashActive = false;
                    }
                }

                // Once we returned to/below normal size, stop the current flash window.
                if (scale <= 1f)
                    flashActive = false;

                float t = 0f;
                if (flashActive && scale > 1f)
                {
                    float normalized = (scale - 1f) / lastPeakDelta;
                    t = Mathf.Clamp01(normalized) * flashIntensity * springFlashStrength;
                }

                background.color = Color.Lerp(baseColor, peakColor, t);
            }

            yield return null;
        }

        target.localScale = Vector3.one;
        if (background != null)
            background.color = baseColor;
        _springRoutines.Remove(hintId);
    }

    void StopSpring(string hintId)
    {
        if (string.IsNullOrEmpty(hintId) || !_springRoutines.TryGetValue(hintId, out var routine))
            return;

        if (routine != null)
            StopCoroutine(routine);

        _springRoutines.Remove(hintId);

        if (_groups.TryGetValue(hintId, out var group) && group != null)
            group.transform.localScale = Vector3.one;

        ResetBackgroundColor(hintId);
    }

    void StopAllSpringRoutines()
    {
        foreach (var kv in _springRoutines)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);

            if (_groups.TryGetValue(kv.Key, out var group) && group != null)
                group.transform.localScale = Vector3.one;
        }

        _springRoutines.Clear();
    }

    void ResetBackgroundColor(string hintId)
    {
        if (!_backgrounds.TryGetValue(hintId, out var background) || background == null)
            return;

        background.color = GetBaseBackgroundColor(hintId, background);
    }

    Color GetBaseBackgroundColor(string hintId, Image background)
    {
        return _baseBackgroundColors.TryGetValue(hintId, out var baseColor) ? baseColor : background.color;
    }

    void TryShowNextPending()
    {
        if (_transitionRoutine != null || _pendingHints.Count == 0)
            return;

        string nextHintId = _pendingHints[0];
        int nextWeight = GetWeight(nextHintId);
        int activeWeight = _activeHintId != null ? GetWeight(_activeHintId) : int.MinValue;

        if (nextWeight <= activeWeight)
            return;

        _pendingHints.RemoveAt(0);
        BeginTransition(_activeHintId, nextHintId);
    }

    IEnumerator FadeGroup(CanvasGroup group, float target)
    {
        float start = group.alpha;
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = fadeDuration > 0f ? elapsed / fadeDuration : 1f;
            group.alpha = Mathf.Lerp(start, target, t);
            yield return null;
        }

        group.alpha = target;
    }

    void InsertPending(string hintId)
    {
        RemovePending(hintId);

        int weight = GetWeight(hintId);
        int insertAt = _pendingHints.Count;

        for (int i = 0; i < _pendingHints.Count; i++)
        {
            if (GetWeight(_pendingHints[i]) < weight)
            {
                insertAt = i;
                break;
            }
        }

        _pendingHints.Insert(insertAt, hintId);
    }

    void RemovePending(string hintId)
    {
        for (int i = _pendingHints.Count - 1; i >= 0; i--)
        {
            if (_pendingHints[i] == hintId)
                _pendingHints.RemoveAt(i);
        }
    }

    bool IsPending(string hintId)
    {
        for (int i = 0; i < _pendingHints.Count; i++)
        {
            if (_pendingHints[i] == hintId)
                return true;
        }

        return false;
    }

    static int GetWeight(string hintId)
    {
        if (Instance != null && !string.IsNullOrEmpty(hintId) && Instance._priorities.TryGetValue(hintId, out int p))
            return p;
        return 0;
    }

    void TickRotatingHints()
    {
        if (!rotatingHintsEnabled)
            return;

        bool hasNote = !string.IsNullOrEmpty(noteHintId) && _groups.ContainsKey(noteHintId);
        bool hasLockpick = !string.IsNullOrEmpty(lockpickHintId) && _groups.ContainsKey(lockpickHintId);
        if (!hasNote && !hasLockpick)
            return;

        _rotatingHintElapsed += Time.unscaledDeltaTime;
        if (_rotatingHintElapsed < rotatingHintIntervalSeconds)
            return;

        _rotatingHintElapsed = 0f;

        if (PlayerControlGate.IsInputBlocked)
            return;

        // Note first, then lockpick, then repeat — always, so players don't miss the lockpick.
        string id;
        if (hasNote && hasLockpick)
        {
            id = _nextRotatingHintIsLockpick ? lockpickHintId : noteHintId;
            _nextRotatingHintIsLockpick = !_nextRotatingHintIsLockpick;
        }
        else
        {
            id = hasNote ? noteHintId : lockpickHintId;
        }

        ShowPeriodicHint(id);
    }

    void TickNoteHint()
    {
        // Rotating loop owns both note + lockpick when enabled.
        if (rotatingHintsEnabled || !noteHintEnabled || string.IsNullOrEmpty(noteHintId))
            return;

        _noteHintElapsed += Time.unscaledDeltaTime;
        if (_noteHintElapsed < noteHintIntervalSeconds)
            return;

        _noteHintElapsed = 0f;

        if (PlayerControlGate.IsInputBlocked)
            return;

        ShowPeriodicHint(noteHintId);
    }

    void ShowPeriodicHint(string hintId)
    {
        if (string.IsNullOrEmpty(hintId) || !_groups.TryGetValue(hintId, out var group) || group == null)
        {
            Debug.LogWarning($"[GameplayHintManager] No panel registered for periodic hint id '{hintId}'.");
            return;
        }

        RemovePending(hintId);

        if (_activeHintId == hintId)
        {
            StopTransitionRoutine();
            CancelAutoHide(hintId);
            StopSpring(hintId);
            group.gameObject.SetActive(true);
            group.alpha = 1f;
            StartSpring(hintId, group.transform);
            ScheduleAutoHide(hintId);
            return;
        }

        Show(hintId);
    }

    void ShowPeriodicNote() => ShowPeriodicHint(noteHintId);

    void StopTransitionRoutine()
    {
        if (_transitionRoutine != null)
        {
            StopCoroutine(_transitionRoutine);
            _transitionRoutine = null;
        }
    }

    bool HasBoneAndGum()
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        return inventory != null && ContainsRegularBone(inventory) && ContainsGum(inventory);
    }

    bool HasRegularBone()
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        return inventory != null && ContainsRegularBone(inventory);
    }

    bool HasCombinedStickyBone()
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null)
            return false;

        if (gummyBoneItem != null && inventory.Contains(gummyBoneItem))
            return true;

        foreach (var item in inventory)
        {
            if (item != null && item.interactionReachBonus > 0f && item.itemName == "Bone")
                return true;
        }

        return false;
    }

    bool ContainsRegularBone(List<InventoryItem> inventory)
    {
        if (boneItem != null)
            return inventory.Contains(boneItem);

        foreach (var item in inventory)
        {
            if (item != null && item.itemName == "Bone" && item.interactionReachBonus <= 0f)
                return true;
        }

        return false;
    }

    bool ContainsGum(List<InventoryItem> inventory)
    {
        if (gumItem != null)
            return inventory.Contains(gumItem);

        foreach (var item in inventory)
        {
            if (item != null && item.itemName == "Chewing Gum")
                return true;
        }

        return false;
    }

    bool HasVase()
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null)
            return false;

        if (vaseItem != null)
            return inventory.Contains(vaseItem);

        foreach (var item in inventory)
        {
            if (item != null && item.itemName == "Vase")
                return true;
        }

        return false;
    }

    /// <summary>One-time hints already done this run, by name (checkpoint save).</summary>
    public List<string> GetShownHints()
    {
        var shown = new List<string>();
        if (_hidingSpotHintShown) shown.Add("hiding_spot");
        if (_soundHintShown) shown.Add("sound");
        if (_killerPatternHintShown) shown.Add("killer_pattern");
        if (_lockpickHintShown) shown.Add("lockpick");
        if (_lockpickLetterFollowupDone) shown.Add("lockpick_letter");
        if (_combineFinished) shown.Add("combine");
        if (_vaseDropHintFinished) shown.Add("vase_drop");
        return shown;
    }

    /// <summary>Checkpoint load: hints from <see cref="GetShownHints"/> don't show again.</summary>
    public void RestoreShownHints(IEnumerable<string> shown)
    {
        if (shown == null)
            return;
        foreach (string id in shown)
        {
            switch (id)
            {
                case "hiding_spot": _hidingSpotHintShown = true; break;
                case "sound": _soundHintShown = true; break;
                case "killer_pattern": _killerPatternHintShown = true; break;
                case "lockpick": _lockpickHintShown = true; _lockpickHintRequested = false; break;
                case "lockpick_letter": _lockpickLetterFollowupDone = true; break;
                case "combine": _combineFinished = true; _combineHintShown = true; break;
                case "vase_drop": _vaseDropHintFinished = true; break;
            }
        }
    }

    public static void ResetAllHints()
    {
        TutorialPopupUI.ResetShown();
        if (Instance == null)
        {
            // New game from menu before instance exists — drop death-hint carryover.
            s_pendingLockpickHintAfterReload = false;
            return;
        }

        Instance._combineHintShown = false;
        Instance._combineFinished = false;
        Instance._combineTimerStarted = false;
        Instance._combineElapsed = 0f;
        Instance._vaseDropHintFinished = false;
        Instance._vasePickupTracked = false;
        Instance._vaseDropHintShownOnce = false;
        Instance._vaseDropHintElapsed = 0f;
        Instance._hidingSpotHintShown = false;
        Instance._soundHintShown = false;
        Instance._killerPatternHintShown = false;
        Instance._lockpickHintShown = false;
        Instance._lockpickHintRequested = false;
        Instance._lockpickLetterFollowupDone = false;
        Instance._lockpickDeathHintQueued = false;
        // Keep s_pendingLockpickHintAfterReload — Restart sets it so the hint can show in the new load.
        Instance._noteHintElapsed = 0f;
        Instance._rotatingHintElapsed = 0f;
        Instance._nextRotatingHintIsLockpick = false;
        Instance.StopCombineTimerCoroutine();
        Instance.HideAll();
    }
}
