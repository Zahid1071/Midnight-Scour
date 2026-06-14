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
    public const string BoneDrop = "bone_drop";
    public const string BoneGumCombine = "bone_gum_combine";
    public const string HidingSpot = "hiding_spot";
    public const string Sound = "sound";

    static readonly Dictionary<string, int> HintWeights = new()
    {
        { HidingSpot, 10 },
        { Sound, 20 },
        { BoneDrop, 30 },
        { BoneGumCombine, 40 },
    };

    static readonly HashSet<string> TimedHints = new()
    {
        HidingSpot,
        Sound,
    };

    public static GameplayHintManager Instance { get; private set; }

    [Serializable]
    struct HintEntry
    {
        public string id;
        public CanvasGroup canvasGroup;
    }

    [Header("Panels — match id to each hint box's Canvas Group")]
    [SerializeField] HintEntry[] panels;
    [SerializeField] float fadeDuration = 0.25f;

    [Header("Transient hints")]
    [SerializeField] float transientHintDurationSeconds = 60f;

    [Header("Show spring")]
    [SerializeField] float springStartScale = 0.6f;
    [SerializeField] float springStiffness = 300f;
    [SerializeField] float springDamping = 8f;
    [SerializeField] float springInitialVelocity = 7f;

    [Header("Background flash")]
    [SerializeField] float flashDuration = 0.45f;
    [SerializeField] float flashIntensity = 0.55f;
    [SerializeField] float flashBrightnessBoost = 0.12f;

    [Header("Items (optional — matched by asset or name if unset)")]
    [SerializeField] InventoryItem boneItem;
    [SerializeField] InventoryItem gumItem;
    [SerializeField] InventoryItem gummyBoneItem;

    [Header("Combine hint")]
    [SerializeField] float combineHintDelaySeconds = 5f;

    readonly Dictionary<string, CanvasGroup> _groups = new();
    readonly Dictionary<string, Image> _backgrounds = new();
    readonly Dictionary<string, Color> _baseBackgroundColors = new();
    readonly Dictionary<string, Coroutine> _springRoutines = new();
    readonly Dictionary<string, Coroutine> _flashRoutines = new();
    readonly Dictionary<string, Coroutine> _autoHideRoutines = new();
    readonly List<string> _pendingHints = new();

    string _activeHintId;
    Coroutine _transitionRoutine;

    Coroutine _combineTimerRoutine;
    bool _combineTimerStarted;
    bool _combineHintShown;
    bool _combineFinished;
    float _combineElapsed;
    bool _boneDropHintFinished;
    bool _boneDropHintVisible;
    bool _hidingSpotHintShown;
    bool _soundHintShown;

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
        StopAllFlashRoutines();
        StopAllAutoHideRoutines();
    }

    void Update()
    {
        EvaluateFirstShiftHint();
        EvaluateCombineTimer();
    }

    public void NotifyCageDoorOpenedFirstTime()
    {
        if (_hidingSpotHintShown)
            return;

        if (Show(HidingSpot))
            _hidingSpotHintShown = true;
    }

    void EvaluateFirstShiftHint()
    {
        if (_soundHintShown || !WasShiftPressedThisFrame())
            return;

        if (Show(Sound))
            _soundHintShown = true;
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
        _backgrounds.Clear();
        _baseBackgroundColors.Clear();

        if (panels == null)
            return;

        foreach (var entry in panels)
        {
            if (string.IsNullOrEmpty(entry.id) || entry.canvasGroup == null)
                continue;

            _groups[entry.id] = entry.canvasGroup;

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
        Hide(BoneGumCombine);
    }

    void EvaluateHints()
    {
        EvaluateBoneDropHint();
        EvaluateCombineHint();
    }

    void EvaluateBoneDropHint()
    {
        if (_boneDropHintFinished)
            return;

        if (HasRegularBone())
        {
            if (Show(BoneDrop))
                _boneDropHintVisible = true;
            return;
        }

        if (_boneDropHintVisible)
        {
            Hide(BoneDrop);
            _boneDropHintVisible = false;
            _boneDropHintFinished = true;
        }
    }

    void EvaluateCombineHint()
    {
        if (_combineFinished || HasCombinedStickyBone())
        {
            _combineElapsed = 0f;
            StopCombineTimerCoroutine();
            Hide(BoneGumCombine);
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

        if (Show(BoneGumCombine))
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
        if (string.IsNullOrEmpty(hintId) || !_groups.TryGetValue(hintId, out var group) || group == null)
        {
            Debug.LogWarning($"[GameplayHintManager] No panel registered for id '{hintId}'.");
            return false;
        }

        if (hintId == _activeHintId || IsPending(hintId))
            return true;

        int newWeight = GetWeight(hintId);

        if (_transitionRoutine != null)
        {
            if (_activeHintId != null && newWeight > GetWeight(_activeHintId))
            {
                RemovePending(hintId);
                StopTransitionRoutine();
                BeginTransition(_activeHintId, hintId);
            }
            else
            {
                InsertPending(hintId);
            }

            return true;
        }

        if (_activeHintId != null)
        {
            if (newWeight > GetWeight(_activeHintId))
            {
                RemovePending(hintId);
                BeginTransition(_activeHintId, hintId);
            }
            else
            {
                InsertPending(hintId);
            }

            return true;
        }

        BeginTransition(null, hintId);
        return true;
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
        StopAllFlashRoutines();
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

    IEnumerator TransitionRoutine(string fromHintId, string toHintId)
    {
        if (!string.IsNullOrEmpty(fromHintId))
        {
            CancelAutoHide(fromHintId);
            StopSpring(fromHintId);
            StopBackgroundFlash(fromHintId);

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
            StartBackgroundFlash(toHintId);
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
        if (!TimedHints.Contains(hintId))
            return;

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

        float scale = springStartScale;
        float velocity = springInitialVelocity;
        target.localScale = Vector3.one * scale;

        while (Mathf.Abs(scale - 1f) > 0.001f || Mathf.Abs(velocity) > 0.001f)
        {
            float dt = Time.unscaledDeltaTime;
            float acceleration = -springStiffness * (scale - 1f) - springDamping * velocity;
            velocity += acceleration * dt;
            scale += velocity * dt;
            target.localScale = Vector3.one * scale;
            yield return null;
        }

        target.localScale = Vector3.one;
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

    void StartBackgroundFlash(string hintId)
    {
        StopBackgroundFlash(hintId);

        if (!_backgrounds.TryGetValue(hintId, out var background) || background == null)
            return;

        _flashRoutines[hintId] = StartCoroutine(BackgroundFlashRoutine(hintId, background));
    }

    IEnumerator BackgroundFlashRoutine(string hintId, Image background)
    {
        Color baseColor = GetBaseBackgroundColor(hintId, background);
        Color peakColor = new(
            Mathf.Min(baseColor.r + flashBrightnessBoost, 1f),
            Mathf.Min(baseColor.g + flashBrightnessBoost, 1f),
            Mathf.Min(baseColor.b + flashBrightnessBoost, 1f),
            Mathf.Min(baseColor.a + 0.18f, 1f));

        float elapsed = 0f;

        while (elapsed < flashDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float wave = Mathf.Sin((elapsed / flashDuration) * Mathf.PI);
            background.color = Color.Lerp(baseColor, peakColor, wave * flashIntensity);
            yield return null;
        }

        background.color = baseColor;
        _flashRoutines.Remove(hintId);
    }

    void StopBackgroundFlash(string hintId)
    {
        if (string.IsNullOrEmpty(hintId) || !_flashRoutines.TryGetValue(hintId, out var routine))
            return;

        if (routine != null)
            StopCoroutine(routine);

        _flashRoutines.Remove(hintId);
        ResetBackgroundColor(hintId);
    }

    void StopAllFlashRoutines()
    {
        foreach (var kv in _flashRoutines)
        {
            if (kv.Value != null)
                StopCoroutine(kv.Value);

            ResetBackgroundColor(kv.Key);
        }

        _flashRoutines.Clear();
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
        return HintWeights.TryGetValue(hintId, out int weight) ? weight : 0;
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

    public static void ResetAllHints()
    {
        if (Instance == null)
            return;

        Instance._combineHintShown = false;
        Instance._combineFinished = false;
        Instance._combineTimerStarted = false;
        Instance._combineElapsed = 0f;
        Instance._boneDropHintFinished = false;
        Instance._boneDropHintVisible = false;
        Instance._hidingSpotHintShown = false;
        Instance._soundHintShown = false;
        Instance.StopCombineTimerCoroutine();
        Instance.HideAll();
    }
}
