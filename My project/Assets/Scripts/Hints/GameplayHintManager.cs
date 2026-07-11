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
/// Only one hint is visible at a time; higher-weight hints replace lower ones after a full fade-out.
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
        [Tooltip("Higher priority replaces lower. Equal or higher cuts the current hint immediately.")]
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

    [Header("Note hint (optional)")]
    [Tooltip("If enabled, shows the note hint every N seconds.")]
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

        EvaluateFirstShiftHint();
        EvaluateCombineTimer();
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

        if (hintId == _activeHintId || IsPending(hintId))
            return true;

        if (_transitionRoutine != null)
            StopTransitionRoutine();

        if (_activeHintId == null)
        {
            RemovePending(hintId);
            BeginTransition(null, hintId);
            return true;
        }

        int newPriority = GetWeight(hintId);
        int activePriority = GetWeight(_activeHintId);

        if (newPriority >= activePriority)
        {
            RemovePending(hintId);
            BeginInstantSwap(_activeHintId, hintId);
        }
        else
        {
            InsertPending(hintId);
        }

        return true;
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

        if (!string.IsNullOrEmpty(fromHintId))
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

        _activeHintId = null;

        if (!string.IsNullOrEmpty(toHintId) && _groups.TryGetValue(toHintId, out var toGroup) && toGroup != null)
        {
            toGroup.gameObject.SetActive(true);
            toGroup.alpha = 1f;
            StartSpring(toHintId, toGroup.transform);
            _activeHintId = toHintId;
            ScheduleAutoHide(toHintId);
        }

        TryShowNextPending();
    }

    IEnumerator TransitionRoutine(string fromHintId, string toHintId)
    {
        if (!string.IsNullOrEmpty(fromHintId))
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

        _activeHintId = null;

        if (!string.IsNullOrEmpty(toHintId) && _groups.TryGetValue(toHintId, out var toGroup) && toGroup != null)
        {
            toGroup.gameObject.SetActive(true);
            StartSpring(toHintId, toGroup.transform);
            yield return FadeGroup(toGroup, 1f);
            toGroup.alpha = 1f;
            _activeHintId = toHintId;
            ScheduleAutoHide(toHintId);
        }

        _transitionRoutine = null;
        TryShowNextPending();
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
            float dt = Time.unscaledDeltaTime;
            float acceleration = -springStiffness * (scale - 1f) - springDamping * velocity;
            float prevVelocity = velocity;
            velocity += acceleration * dt;
            scale += velocity * dt;
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

    void TickNoteHint()
    {
        if (!noteHintEnabled || string.IsNullOrEmpty(noteHintId))
            return;

        _noteHintElapsed += Time.unscaledDeltaTime;
        if (_noteHintElapsed < noteHintIntervalSeconds)
            return;

        _noteHintElapsed = 0f;

        if (PlayerControlGate.IsInputBlocked)
            return;

        ShowPeriodicNote();
    }

    void ShowPeriodicNote()
    {
        if (string.IsNullOrEmpty(noteHintId) || !_groups.TryGetValue(noteHintId, out var group) || group == null)
        {
            Debug.LogWarning($"[GameplayHintManager] No panel registered for note hint id '{noteHintId}'.");
            return;
        }

        RemovePending(noteHintId);

        if (_activeHintId == noteHintId)
        {
            StopTransitionRoutine();
            CancelAutoHide(noteHintId);
            StopSpring(noteHintId);
            group.gameObject.SetActive(true);
            group.alpha = 1f;
            StartSpring(noteHintId, group.transform);
            ScheduleAutoHide(noteHintId);
            return;
        }

        Show(noteHintId);
    }

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

    public static void ResetAllHints()
    {
        if (Instance == null)
            return;

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
        Instance.StopCombineTimerCoroutine();
        Instance.HideAll();
    }
}
