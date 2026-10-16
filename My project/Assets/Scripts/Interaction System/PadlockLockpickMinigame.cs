using System;
using System.Collections;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Padlock lockpick — binding-pin style:
/// RMB — tension (cylinder binds; full turn only when all pins are set).
/// Mouse — travel 0–1; pick snaps to 4 stages (0–25% / 25–50% / 50–75% / 75–100%).
/// LMB — lift to set the binding pin; while held, the pick stays at its current stage.
/// Release LMB to move freely again. Binding order is shuffled each attempt.
/// Releasing RMB drops set pins but keeps the same binding order.
/// </summary>
[DisallowMultipleComponent]
public class PadlockLockpickMinigame : MonoBehaviour, IInteractable, IItemReceiver
{
    public static PadlockLockpickMinigame Active { get; private set; }

    /// <summary>Fired each time a pin sets (any padlock).</summary>
    public static event Action<PadlockLockpickMinigame> PinSet;

    public enum Stage
    {
        Idle,
        Presenting,
        Playing,
        Exiting
    }

    [Serializable]
    public struct Pin
    {
        [Range(0f, 1f)] public float depth01;
        [Range(0.01f, 0.25f)] public float depthWindow;
    }

    [Header("Scene refs")]
    [SerializeField] CinemachineCamera lockpickCamera;
    [SerializeField] int activeCameraPriority = 20;
    [SerializeField] float cameraBlendOutSeconds = 0.85f;
    [Tooltip("The padlock keyhole / plug mesh to twist (e.g. child named Cylinder).")]
    [SerializeField] Transform keyholeCylinder;
    [Tooltip("Scene-placed Lockpick_Pick under the keyhole. Driven in/out by mouse.")]
    [SerializeField] Transform pickVisual;
    [Tooltip("Scene-placed tension wrench. Leads the cylinder by a small deadzone on RMB.")]
    [SerializeField] Transform wrenchVisual;

    [Header("Lock presentation")]
    [SerializeField] float presentDuration = 1.8f;
    [SerializeField] float restoreDuration = 1.2f;
    [SerializeField] Transform presentPoseTarget;
    [SerializeField] Vector3 presentLocalPositionOffset;
    [SerializeField] Vector3 presentLocalEulerOffset;

    [Header("Cylinder twist (RMB)")]
    [SerializeField] Vector3 twistAxisLocal = new Vector3(0f, 0f, 1f);
    [SerializeField] bool invertTwist = true;
    [SerializeField] float bindAngleDegrees = 14f;
    [SerializeField] float unlockAngleDegrees = 90f;
    [Tooltip("Degrees/sec while binding (pins not all set).")]
    [SerializeField] float twistEngageSpeed = 90f;
    [Tooltip("Degrees/sec for the final unlock turn after all pins are set.")]
    [SerializeField] float unlockTwistEngageSpeed = 45f;
    [SerializeField] float twistReturnSpeed = 140f;

    [Header("Wrench deadzone (RMB lead)")]
    [Tooltip("Wrench rotates this many degrees before the cylinder starts (and returns this much before the cylinder springs back).")]
    [SerializeField] float wrenchDeadzoneDegrees = 4f;
    [SerializeField] float wrenchLeadEngageSpeed = 120f;
    [SerializeField] float wrenchLeadReturnSpeed = 160f;
    [SerializeField] bool hideWrenchWhenIdle = true;

    [Header("Pick depth (mouse) — 4 stage zones")]
    [SerializeField] float insertSensitivity = 0.0025f;
    [SerializeField] bool invertInsert = true;
    [Tooltip("Parent-local axis the pick slides along (default = local forward +Z).")]
    [SerializeField] Vector3 pickInsertAxisLocal = new Vector3(0f, 0f, 1f);
    [Tooltip("How far the pick travels from its placed pose at stage 4 / insert = 1 (parent local units).")]
    [SerializeField] float pickInsertDistance = 0.0025f;
    [Tooltip("How fast the pick eases between stage snaps (depth 0-1 per second). Higher = snappier.")]
    [SerializeField] float stageVisualSmoothSpeed = 6f;
    [SerializeField] bool hidePickWhenIdle = true;

    [Header("Pick lift (LMB)")]
    [Tooltip("Local euler offset applied while LMB is held (tip pushing up on a pin).")]
    [SerializeField] Vector3 pickLiftEuler = new Vector3(-12f, 0f, 0f);
    [SerializeField] float pickLiftEngageSpeed = 8f;
    [SerializeField] float pickLiftReturnSpeed = 10f;
    [Tooltip("Lift amount (0-1) required to set the binding pin.")]
    [SerializeField, Range(0.1f, 1f)] float minLiftToSetPin = 0.85f;
    [Tooltip("After setting a pin, lift must drop below this before the next pin can set.")]
    [SerializeField, Range(0f, 0.5f)] float liftReleaseBeforeNextPin = 0.15f;

    [Header("Pins (one stage each; binding order is shuffled each attempt)")]
    [SerializeField] Pin[] pins =
    {
        new Pin { depth01 = 0.25f, depthWindow = 0.06f },
        new Pin { depth01 = 0.5f, depthWindow = 0.06f },
        new Pin { depth01 = 0.75f, depthWindow = 0.06f },
        new Pin { depth01 = 1f, depthWindow = 0.06f },
    };
    [Tooltip("Randomize which pin binds first / next each time the minigame starts.")]
    [SerializeField] bool shuffleBindingOrder = true;
    [Tooltip("Cylinder must be this far into the bind (0-1) to set a pin — like real tension.")]
    [SerializeField, Range(0f, 1f)] float minTensionToSetPin = 0.55f;

    [Header("Level digit (3D, above the padlock)")]
    [Tooltip("3D text that shows the stage the pick is at (1-4) while picking. Hidden outside the minigame.")]
    [SerializeField] TMP_Text levelText;
    [SerializeField] bool showLevelText = true;
    [SerializeField] Color levelTextColor = new Color(1f, 0.85f, 0.2f, 1f);
    [Tooltip("Colour of a stage whose pin is already set.")]
    [SerializeField] Color levelFoundColor = new Color(0.35f, 1f, 0.4f, 1f);
    [Tooltip("Extra scale the digit pops to when the pick moves to another stage.")]
    [SerializeField] float levelTextPunch = 0.3f;
    [Tooltip("Peak extra scale of the elastic bounce when a pin is set.")]
    [SerializeField] float levelTextSuccessPunch = 0.6f;
    [SerializeField] float levelTextSuccessDuration = 0.5f;
    [Tooltip("Metres the digit shakes side to side when the wrong pin is pushed.")]
    [SerializeField] float levelTextShakeDistance = 0.006f;
    [SerializeField] float levelTextShakeDuration = 0.35f;
    [SerializeField] float levelTextShakeFrequency = 55f;

    [Header("Progress UI (screwdriver-style). Leave empty to auto-create.")]
    [SerializeField] GameObject progressPanel;
    [SerializeField] Slider progressSlider;
    [SerializeField] Image progressFillImage;
    [SerializeField] Color barEmptyColor = new Color(0.95f, 0.28f, 0.22f, 1f);
    [SerializeField] Color barFullColor = new Color(0.45f, 0.95f, 0.5f, 1f);

    [Header("Lock")]
    [SerializeField] bool isLocked = true;
    [SerializeField] InventoryItem requiredLockpick;
    [SerializeField] string lockedMessage = "It's locked.";
    [SerializeField] string wrongItemMessage = "That won't work here.";
    [SerializeField] float lockedFeedbackCooldown = 1f;

    [Header("Unlock hooks")]
    [SerializeField] UnityEvent onUnlocked;
    [Tooltip("Padlock shackle to raise when unlocked.")]
    [SerializeField] Transform shackle;
    [SerializeField] Vector3 shackleRaisedLocalPosition;
    [SerializeField] Vector3 shackleRaisedLocalEuler;
    [Tooltip("Chain link rigidbodies to drop on unlock (set non-kinematic). Leave empty to skip.")]
    [SerializeField] Rigidbody[] chainRigidbodies;
    [Tooltip("Loop-closing chain joint — connectedBody is cleared so the loop opens and chains can fall.")]
    [SerializeField] Joint chainDetachJoint;
    [Tooltip("After this many seconds, chains go kinematic again and their colliders are disabled.")]
    [SerializeField] float chainSettleSeconds = 5f;
    [Tooltip("Cage / door unlocked silently when the padlock opens (no door subtitle or unlock SFX).")]
    [SerializeField] FS_Atmo.SimpleOpenClose doorToUnlockSilently;

    [Header("Audio (optional)")]
    [SerializeField] AudioClip unlockSound;
    [SerializeField] AudioClip lockedSound;
    [SerializeField] AudioClip cancelSfx;
    [SerializeField] AudioClip pinSetSfx;
    [Tooltip("Plays while the cylinder/key is rotating (assign key-twist clip).")]
    [SerializeField] AudioClip cylinderRotateSfx;
    [Tooltip("Plays when chains are released (gravity / non-kinematic).")]
    [SerializeField] AudioClip chainFallSfx;
    [SerializeField] float sfxVolume = 1f;
    [SerializeField, Range(0f, 1f)] float cylinderRotateSfxVolume = 0.85f;
    [Tooltip("Short metallic tick played whenever the pick moves into a new depth stage, pitched per stage.")]
    [SerializeField] AudioClip stageTickSfx;
    [Tooltip("Pitch offset of each stage's tick in semitones, shallow to deep (0,2,4,5 = do re mi fa).")]
    [SerializeField] float[] stageTickSemitones = { 0f, 2f, 4f, 5f };
    [SerializeField, Range(0f, 1f)] float stageTickVolume = 0.6f;
    [Tooltip("Peak camera wobble (degrees) when the pick moves into a new stage. 0 = off.")]
    [SerializeField] float stageShakeDegrees = 0.25f;
    [SerializeField] float stageShakeFrequency = 12f;
    [SerializeField] float stageShakeDuration = 0.15f;
    [Tooltip("Springy bounce-back played when the player lifts a pin that isn't the binding one (while tension is held).")]
    [SerializeField] AudioClip wrongPinSfx;
    [SerializeField, Range(0f, 1f)] float wrongPinVolume = 0.7f;

    Stage _stage = Stage.Idle;
    CameraKick _cameraKick;
    InventoryItem _tool;
    Action<InventoryItem> _onSuccess;
    Action _onCancel;
    Vector3 _savedPosition;
    Quaternion _savedRotation;
    bool _poseStored;
    Coroutine _poseRoutine;
    Coroutine _unwindRoutine;
    int _openedOnFrame = -1;
    float _nextInteractTime;

    Quaternion _cylinderBaseLocalRot;
    bool _cylinderPoseStored;
    float _twistAngle;
    float _wrenchLeadAngle;

    /// <summary>Continuous mouse travel 0–1 (input only). Visual snaps to stage.</summary>
    float _travel01;
    float _pickLift01;
    float _prevPickLift01;
    bool _liftArmedForNextPin = true;
    bool[] _pinSet;
    int[] _bindingOrder;
    int _bindingOrderIndex;
    /// <summary>Pin indices sorted shallow → deep (stage 0..n-1).</summary>
    int[] _depthOrder;
    /// <summary>0..n-1 = which quartile stage the pick is snapped to.</summary>
    int _pinSlot;
    /// <summary>Smoothed visual depth 0-1 toward the current stage snap.</summary>
    float _visualStageDepth01;
    bool _pinsComplete;
    bool _tensionWasHeld;

    Vector3 _pickBaseLocalPos;
    Quaternion _pickBaseLocalRot;
    bool _pickPoseStored;

    Quaternion _wrenchBaseLocalRot;
    bool _wrenchPoseStored;

    AudioSource _cylinderRotateSource;
    Coroutine _chainSettleRoutine;

    bool _ownsProgressUi;
    UIFadeHelper _progressFade;
    enum Guide { None, Pressure, FindPins }
    Guide _guide;
    /// <summary>Guide stays off until this (unscaled) time so pin feedback lines can be read.</summary>
    float _guideResumeTime;

    float _levelTextPop;
    float _levelTextBounceLeft;
    float _levelTextShakeLeft;
    Vector3 _levelTextBaseScale = Vector3.one;
    Vector3 _levelTextBasePosition;

    public Stage CurrentStage => _stage;
    public bool IsRunning => _stage == Stage.Playing || _stage == Stage.Presenting;
    public bool IsLocked => isLocked;
    public float ProgressNormalized =>
        _pinSet == null || _pinSet.Length == 0 ? 0f : (float)CountSolvedPins() / _pinSet.Length;

    void Awake()
    {
        if (lockpickCamera != null)
            lockpickCamera.Priority = 0;

        if (keyholeCylinder == null)
            keyholeCylinder = FindNamedChild(transform, "Cylinder");

        if (pickVisual == null)
            pickVisual = FindNamedChild(transform, "Lockpick_Pick");

        if (wrenchVisual == null)
            wrenchVisual = FindNamedChild(transform, "Lockpick_TensionWrench");

        StorePickPose();
        SetPickVisible(!hidePickWhenIdle);
        StoreWrenchPose();
        SetWrenchVisible(!hideWrenchWhenIdle);
        ApplyPickVisual();
        ApplyWrenchVisual();

        if (progressSlider != null)
            progressSlider.interactable = false;

        SetProgressVisible(false);

        if (levelText != null)
        {
            _levelTextBaseScale = levelText.transform.localScale;
            _levelTextBasePosition = levelText.transform.localPosition;
            levelText.gameObject.SetActive(false);
        }
    }

    /// <summary>Called by ContinuePadlockSpawner after Instantiate.</summary>
    public void ConfigureForContinue(FS_Atmo.SimpleOpenClose door, CinemachineCamera camera)
    {
        if (door != null)
            doorToUnlockSilently = door;
        if (camera != null)
        {
            lockpickCamera = camera;
            lockpickCamera.Priority = 0;
        }
    }

    void OnDestroy()
    {
        if (_chainSettleRoutine != null)
        {
            StopCoroutine(_chainSettleRoutine);
            _chainSettleRoutine = null;
        }

        if (Active == this)
            Active = null;

        if (_stage != Stage.Idle)
            ForceAbortCleanup(snapPose: true);
        else
            TeardownOwnedProgressUi();
    }

    void LateUpdate()
    {
        UpdateLevelText();
    }

    void Update()
    {
        if (_stage != Stage.Playing && _stage != Stage.Presenting)
            return;

        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonPause)
            || PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            return;

        if (Time.frameCount <= _openedOnFrame)
            return;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (WasCancelPressedThisFrame())
        {
            BeginCancelExit();
            return;
        }

        if (_stage != Stage.Playing)
            return;

        TickGameplay();
    }

    public void NotifyPickSucceeded()
    {
        if (_stage != Stage.Playing)
            return;

        BeginSuccessExit();
    }

    public bool Interact()
    {
        if (!isLocked)
            return false;

        if (Time.time < _nextInteractTime)
        {
            InteractionFeedback.Fail();
            return false;
        }

        InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
        if (selected != null && requiredLockpick != null && selected == requiredLockpick)
            return false;

        InteractionFeedback.Fail(Loc.Get("gameplay.locked"), 2.5f);
        PlayLockedFeedback();
        return false;
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (!isLocked)
            return false;

        if (item == null || requiredLockpick == null || item != requiredLockpick)
        {
            if (Time.time < _nextInteractTime)
                return false;

            if (item != null && item.TryBreakOnWrongUse(transform.position))
            {
                InteractionFeedback.Fail();
                PlayLockedFeedback();
                return false;
            }

            InteractionFeedback.Fail(Loc.Get("gameplay.wrong_item"), 2.5f);
            PlayLockedFeedback();
            return false;
        }

        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            InteractionFeedback.Fail(Loc.Format("gameplay.dont_have_item", item.GetLocalizedName()), 2.5f);
            return false;
        }

        return TryBegin(item, CompleteUnlockFromMinigame);
    }

    public bool TryBegin(InventoryItem tool, Action<InventoryItem> onSuccess, Action onCancel = null)
    {
        if (tool == null || lockpickCamera == null)
        {
            Debug.LogWarning($"[PadlockLockpick] Missing lockpick camera on {name}.", this);
            return false;
        }

        if (_stage != Stage.Idle || !isLocked)
            return false;

        if (Active != null && Active != this && Active.IsRunning)
            return false;

        if (keyholeCylinder == null)
            keyholeCylinder = FindNamedChild(transform, "Cylinder");

        if (keyholeCylinder == null)
        {
            Debug.LogWarning($"[PadlockLockpick] Assign Keyhole Cylinder (child 'Cylinder') on {name}.", this);
            return false;
        }

        if (pickVisual == null)
            pickVisual = FindNamedChild(transform, "Lockpick_Pick");

        if (wrenchVisual == null)
            wrenchVisual = FindNamedChild(transform, "Lockpick_TensionWrench");

        _tool = tool;
        _onSuccess = onSuccess;
        _onCancel = onCancel;
        _openedOnFrame = Time.frameCount;
        _twistAngle = 0f;
        _wrenchLeadAngle = 0f;
        _travel01 = 0f;
        _pickLift01 = 0f;
        _prevPickLift01 = 0f;
        _liftArmedForNextPin = true;
        _pinSlot = 0;
        _visualStageDepth01 = GetSnappedStageDepth01();
        _pinsComplete = false;
        _tensionWasHeld = false;
        _guide = Guide.None;
        _guideResumeTime = 0f;
        ResetPins();
        RefreshPinSlotFromTravel();

        EnsureProgressUi();
        RefreshProgressUi();
        SetProgressVisible(true);

        StoreCylinderPose();
        ApplyCylinderTwist();
        StorePickPose();
        SetPickVisible(true);
        ApplyPickVisual();
        StoreWrenchPose();
        SetWrenchVisible(true);
        ApplyWrenchVisual();

        Active = this;
        PlayerControlGate.Acquire(PlayerControlGate.ReasonLockpick);

        StorePose();
        lockpickCamera.Priority = activeCameraPriority;
        if (_cameraKick == null && stageShakeDegrees > 0f)
            _cameraKick = lockpickCamera.gameObject.AddComponent<CameraKick>();
        _stage = Stage.Presenting;
        StartPoseRoutine(PresentThenStartPlaying());

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        ShowGuide(Guide.Pressure);
        return true;
    }

    IEnumerator PresentThenStartPlaying()
    {
        yield return AnimateToPresentedPose();

        if (_stage != Stage.Presenting)
            yield break;

        ApplyCylinderTwist();
        _stage = Stage.Playing;
    }

    void TickGameplay()
    {
        var mouse = Mouse.current;
        Vector2 delta = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
        bool tensionHeld = mouse != null && mouse.rightButton.isPressed;
        bool lifting = mouse != null && mouse.leftButton.isPressed;

        // Continuous travel input, then snap into one of 4 stage zones. While lifting (LMB) the pick is
        // pressed against a pin and holds its depth; mouse movement is ignored.
        if (!lifting)
        {
            float insertDelta = delta.y * insertSensitivity * (invertInsert ? -1f : 1f);
            _travel01 = Mathf.Clamp01(_travel01 + insertDelta);
        }

        int previousSlot = _pinSlot;
        RefreshPinSlotFromTravel();
        if (_pinSlot != previousSlot)
        {
            PlayStageTick(_pinSlot);
            if (_cameraKick != null)
                _cameraKick.Kick(stageShakeDegrees, stageShakeFrequency, stageShakeDuration);
        }

        TickPickLift(lifting);
        TickPickStageVisual();
        TickCylinderTwist(tensionHeld);
        ApplyPickVisual();

        // Lose tension → set pins drop; binding order stays (no reshuffle).
        if (_tensionWasHeld && !tensionHeld)
            ResetPinsAfterTensionLost();
        _tensionWasHeld = tensionHeld;

        TickGuide(tensionHeld);

        if (_pickLift01 <= liftReleaseBeforeNextPin)
            _liftArmedForNextPin = true;

        bool liftJustEngaged = _liftArmedForNextPin
            && _pickLift01 >= minLiftToSetPin
            && _prevPickLift01 < minLiftToSetPin;

        if (liftJustEngaged)
            TrySetBindingPin();

        _prevPickLift01 = _pickLift01;

        RefreshProgressUi();

        if (_pinsComplete && tensionHeld && _twistAngle >= unlockAngleDegrees - 0.5f)
            BeginSuccessExit();
    }

    /// <summary>
    /// 0–25% → stage 0, 25–50% → 1, 50–75% → 2, 75–100% → 3.
    /// Pick visual always sits on that stage (not in-between).
    /// </summary>
    void RefreshPinSlotFromTravel()
    {
        int count = pins != null ? pins.Length : 0;
        if (count <= 0)
        {
            _pinSlot = 0;
            return;
        }

        int slot = Mathf.FloorToInt(_travel01 * count);
        if (slot >= count)
            slot = count - 1;
        if (slot < 0)
            slot = 0;
        _pinSlot = slot;
    }

    bool IsSlotFound(int slot)
    {
        if (_pinsComplete)
            return true;
        if (_depthOrder == null || _pinSet == null || slot < 0 || slot >= _depthOrder.Length)
            return false;
        int pin = _depthOrder[slot];
        return pin >= 0 && pin < _pinSet.Length && _pinSet[pin];
    }

    void UpdateLevelText()
    {
        bool visible = showLevelText && _stage == Stage.Playing && pins != null && pins.Length > 0;
        if (levelText == null)
            return;

        if (!visible)
        {
            if (levelText.gameObject.activeSelf)
            {
                levelText.gameObject.SetActive(false);
                levelText.transform.localScale = _levelTextBaseScale;
                levelText.transform.localPosition = _levelTextBasePosition;
            }
            return;
        }

        if (!levelText.gameObject.activeSelf)
        {
            levelText.gameObject.SetActive(true);
            _levelTextPop = 0f;
            _levelTextBounceLeft = 0f;
            _levelTextShakeLeft = 0f;
        }

        string digit = (_pinSlot + 1).ToString();
        if (levelText.text != digit)
        {
            levelText.text = digit;
            _levelTextPop = 1f;
        }
        levelText.color = IsSlotFound(_pinSlot) ? levelFoundColor : levelTextColor;

        float dt = Time.unscaledDeltaTime;
        _levelTextPop = Mathf.MoveTowards(_levelTextPop, 0f, dt * 6f);
        float pop = 1f + levelTextPunch * _levelTextPop * _levelTextPop;

        if (_levelTextBounceLeft > 0f)
        {
            _levelTextBounceLeft = Mathf.Max(0f, _levelTextBounceLeft - dt);
            float p = 1f - _levelTextBounceLeft / Mathf.Max(0.01f, levelTextSuccessDuration);
            pop += levelTextSuccessPunch * Mathf.Sin(p * Mathf.PI * 2.5f) * (1f - p);
        }
        levelText.transform.localScale = _levelTextBaseScale * pop;

        Vector3 position = _levelTextBasePosition;
        if (_levelTextShakeLeft > 0f)
        {
            _levelTextShakeLeft = Mathf.Max(0f, _levelTextShakeLeft - dt);
            float fade = _levelTextShakeLeft / Mathf.Max(0.01f, levelTextShakeDuration);
            float elapsed = levelTextShakeDuration - _levelTextShakeLeft;
            float offset = Mathf.Sin(elapsed * levelTextShakeFrequency) * levelTextShakeDistance * fade;
            Transform t = levelText.transform;
            Vector3 side = t.parent != null ? t.parent.InverseTransformVector(t.right * offset) : t.right * offset;
            position += side;
        }
        levelText.transform.localPosition = position;
    }

    void PlayWrongPinSfx()
    {
        if (wrongPinSfx == null || SoundManager.Instance == null)
            return;
        Vector3 position = keyholeCylinder != null ? keyholeCylinder.position : transform.position;
        SoundManager.Instance.PlaySFXAt(wrongPinSfx, position, wrongPinVolume);
    }

    void PlayStageTick(int slot)
    {
        if (stageTickSfx == null || SoundManager.Instance == null)
            return;

        float semitones = stageTickSemitones != null && stageTickSemitones.Length > 0
            ? stageTickSemitones[Mathf.Clamp(slot, 0, stageTickSemitones.Length - 1)]
            : 0f;
        Vector3 position = keyholeCylinder != null ? keyholeCylinder.position : transform.position;
        SoundManager.Instance.PlaySFXAt(stageTickSfx, position, stageTickVolume, Mathf.Pow(2f, semitones / 12f));
    }

    /// <summary>Snapped stage depth along local forward (stage 1→0.25 … stage 4→1).</summary>
    float GetSnappedStageDepth01()
    {
        int count = pins != null ? Mathf.Max(1, pins.Length) : 1;
        return (_pinSlot + 1f) / count;
    }

    void TickPickStageVisual()
    {
        float target = GetSnappedStageDepth01();
        float speed = Mathf.Max(0.01f, stageVisualSmoothSpeed);
        _visualStageDepth01 = Mathf.MoveTowards(_visualStageDepth01, target, speed * Time.deltaTime);
    }

    void TickPickLift(bool held)
    {
        float target = held ? 1f : 0f;
        float speed = held ? pickLiftEngageSpeed : pickLiftReturnSpeed;
        _pickLift01 = Mathf.MoveTowards(_pickLift01, target, speed * Time.deltaTime);
    }

    void TickCylinderTwist(bool held)
    {
        float maxAngle = GetCurrentMaxTwistAngle();
        float deadzone = Mathf.Max(0f, wrenchDeadzoneDegrees);
        float prevTwist = _twistAngle;

        if (held)
        {
            // Wrench fills the deadzone first, then the cylinder follows.
            _wrenchLeadAngle = Mathf.MoveTowards(
                _wrenchLeadAngle,
                deadzone,
                wrenchLeadEngageSpeed * Time.deltaTime);

            if (_wrenchLeadAngle >= deadzone - 0.01f)
            {
                float engageSpeed = _pinsComplete ? unlockTwistEngageSpeed : twistEngageSpeed;
                _twistAngle = Mathf.MoveTowards(
                    _twistAngle,
                    maxAngle,
                    engageSpeed * Time.deltaTime);
            }
        }
        else
        {
            // On release, wrench returns through the deadzone before the cylinder springs back.
            _wrenchLeadAngle = Mathf.MoveTowards(
                _wrenchLeadAngle,
                0f,
                wrenchLeadReturnSpeed * Time.deltaTime);

            if (_wrenchLeadAngle <= 0.01f)
            {
                _twistAngle = Mathf.MoveTowards(
                    _twistAngle,
                    0f,
                    twistReturnSpeed * Time.deltaTime);
            }
        }

        ApplyCylinderTwist();
        ApplyWrenchVisual();
        UpdateCylinderRotateSfx(Mathf.Abs(_twistAngle - prevTwist) > 0.0001f);
    }

    void UpdateCylinderRotateSfx(bool cylinderMoving)
    {
        if (cylinderRotateSfx == null)
        {
            StopCylinderRotateSfx();
            return;
        }

        if (!cylinderMoving)
        {
            StopCylinderRotateSfx();
            return;
        }

        if (_cylinderRotateSource == null)
        {
            var go = new GameObject("PadlockCylinderRotateSfx");
            go.transform.SetParent(transform, false);
            _cylinderRotateSource = go.AddComponent<AudioSource>();
            _cylinderRotateSource.playOnAwake = false;
            _cylinderRotateSource.loop = true;
            _cylinderRotateSource.spatialBlend = 1f;
            _cylinderRotateSource.minDistance = 0.5f;
            _cylinderRotateSource.maxDistance = 12f;
        }

        _cylinderRotateSource.transform.position = keyholeCylinder != null
            ? keyholeCylinder.position
            : transform.position;
        _cylinderRotateSource.volume = cylinderRotateSfxVolume * sfxVolume;

        if (_cylinderRotateSource.clip != cylinderRotateSfx)
            _cylinderRotateSource.clip = cylinderRotateSfx;

        if (!_cylinderRotateSource.isPlaying)
            _cylinderRotateSource.Play();
    }

    void StopCylinderRotateSfx()
    {
        if (_cylinderRotateSource == null)
            return;

        if (_cylinderRotateSource.isPlaying)
            _cylinderRotateSource.Stop();
    }

    float GetCurrentMaxTwistAngle()
    {
        if (_pinsComplete)
            return unlockAngleDegrees;

        int total = pins != null ? Mathf.Max(1, pins.Length) : 1;
        int solved = CountSolvedPins();
        float t = (solved + 1f) / total;
        return Mathf.Lerp(bindAngleDegrees * 0.45f, bindAngleDegrees, t);
    }

    int GetBindingPinIndex()
    {
        if (_bindingOrder == null || _pinSet == null)
            return -1;

        if (_bindingOrderIndex < 0 || _bindingOrderIndex >= _bindingOrder.Length)
            return -1;

        return _bindingOrder[_bindingOrderIndex];
    }

    void TrySetBindingPin()
    {
        if (_pinSet == null || pins == null || _depthOrder == null)
            return;

        float tension01 = GetBindTension01();
        if (tension01 < minTensionToSetPin)
            return;

        int binder = GetBindingPinIndex();
        if (binder < 0 || binder >= pins.Length)
            return;

        if (binder < _pinSet.Length && _pinSet[binder])
            return;

        if (_pinSlot < 0 || _pinSlot >= _depthOrder.Length)
            return;

        if (_depthOrder[_pinSlot] != binder)
        {
            PlayWrongPinSfx();
            _levelTextShakeLeft = levelTextShakeDuration;
            return;
        }

        _pinSet[binder] = true;
        _levelTextBounceLeft = levelTextSuccessDuration;
        _bindingOrderIndex++;
        _liftArmedForNextPin = false;

        if (pinSetSfx != null)
            SoundManager.Instance?.PlaySFXAt(pinSetSfx, transform.position, sfxVolume);

        RefreshProgressUi();
        PinSet?.Invoke(this);

        float feedbackSeconds;
        if (AllPinsSet())
        {
            _pinsComplete = true;
            feedbackSeconds = 2.5f;
            HudManager.Instance?.ShowSubtitle(
                LocOrFallback("gameplay.lockpick_pins_set", "Unlocked key"),
                feedbackSeconds,
                HudManager.Instance != null ? HudManager.Instance.SuccessColor : Color.white);
        }
        else
        {
            feedbackSeconds = 1.8f;
            HudManager.Instance?.ShowSubtitle(
                LocOrFallback("gameplay.lockpick_pin_set", "Pin set"),
                feedbackSeconds,
                HudManager.Instance != null ? HudManager.Instance.SuccessColor : Color.white);
        }
        _guide = Guide.None;
        _guideResumeTime = Time.unscaledTime + feedbackSeconds;
    }

    // Keeps the hint for the current phase on screen: no tension → apply pressure, tension built → find pins.
    // While tension is still building, or all pins are set and held, the current hint is left as is.
    void TickGuide(bool tensionHeld)
    {
        if (Time.unscaledTime < _guideResumeTime)
            return;

        Guide want = _guide;
        if (!tensionHeld)
            want = Guide.Pressure;
        else if (!_pinsComplete && GetBindTension01() >= minTensionToSetPin)
            want = Guide.FindPins;

        ShowGuide(want);
    }

    void ShowGuide(Guide guide)
    {
        _guide = guide;
        if (guide == Guide.None)
            return;

        string message = guide == Guide.Pressure
            ? LocOrFallback("gameplay.lockpick_guide_pressure", "Hold right mouse click to apply pressure")
            : LocOrFallback(
                "gameplay.lockpick_guide_find_pins",
                "Use your mouse to move the lockpick and press left mouse button to pick pins");
        Color yellow = HudManager.Instance != null
            ? HudManager.Instance.WarningColor
            : new Color(1f, 0.92f, 0.35f, 1f);
        HudManager.Instance?.ShowPersistentSubtitle(message, yellow);
    }

    float GetBindTension01()
    {
        float maxBind = Mathf.Max(0.01f, GetCurrentMaxTwistAngle());
        if (_pinsComplete)
            return 1f;
        return Mathf.Clamp01(_twistAngle / maxBind);
    }

    void ResetPins()
    {
        int count = pins != null ? pins.Length : 0;
        _pinSet = new bool[count];
        _pinsComplete = false;
        _liftArmedForNextPin = true;
        _prevPickLift01 = 0f;
        _bindingOrderIndex = 0;
        _pinSlot = 0;
        _travel01 = 0f;
        _visualStageDepth01 = GetSnappedStageDepth01();
        BuildDepthOrder(count);
        BuildBindingOrder(count);
    }

    void ResetPinsAfterTensionLost()
    {
        if (_stage != Stage.Playing)
            return;

        // Nothing to lose — avoid resetting while just tapping tension.
        if (!HasAnyPinSet())
            return;

        if (_pinSet != null)
            Array.Clear(_pinSet, 0, _pinSet.Length);

        _pinsComplete = false;
        _liftArmedForNextPin = true;
        _prevPickLift01 = 0f;
        _bindingOrderIndex = 0;
        // Keep _bindingOrder / travel so releasing tension is a setback, not a new combo.
        RefreshProgressUi();
    }

    bool HasAnyPinSet()
    {
        if (_pinSet == null)
            return false;

        for (int i = 0; i < _pinSet.Length; i++)
        {
            if (_pinSet[i])
                return true;
        }

        return false;
    }

    void BuildDepthOrder(int count)
    {
        _depthOrder = new int[count];
        for (int i = 0; i < count; i++)
            _depthOrder[i] = i;

        Array.Sort(_depthOrder, (a, b) =>
        {
            float da = pins != null && a < pins.Length ? pins[a].depth01 : 0f;
            float db = pins != null && b < pins.Length ? pins[b].depth01 : 0f;
            return da.CompareTo(db);
        });
    }

    void BuildBindingOrder(int count)
    {
        _bindingOrder = new int[count];
        for (int i = 0; i < count; i++)
            _bindingOrder[i] = i;

        if (!shuffleBindingOrder || count <= 1)
            return;

        // Reject fully sequential runs like 0-1-2-3 or 3-2-1-0.
        const int maxAttempts = 48;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            for (int i = count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int tmp = _bindingOrder[i];
                _bindingOrder[i] = _bindingOrder[j];
                _bindingOrder[j] = tmp;
            }

            if (!IsFullySequentialBindingOrder(_bindingOrder))
                return;
        }

        // Deterministic non-sequential fallback (swap first two when needed).
        if (IsFullySequentialBindingOrder(_bindingOrder) && count >= 2)
        {
            int tmp = _bindingOrder[0];
            _bindingOrder[0] = _bindingOrder[1];
            _bindingOrder[1] = tmp;
        }
    }

    /// <summary>True for 0,1,2,3… or n-1,…,0 (the boring consecutive combos).</summary>
    static bool IsFullySequentialBindingOrder(int[] order)
    {
        if (order == null || order.Length < 3)
            return false;

        bool allAsc = true;
        bool allDesc = true;
        for (int i = 1; i < order.Length; i++)
        {
            int step = order[i] - order[i - 1];
            if (step != 1)
                allAsc = false;
            if (step != -1)
                allDesc = false;
        }

        return allAsc || allDesc;
    }

    int CountSolvedPins()
    {
        if (_pinSet == null)
            return 0;

        int n = 0;
        for (int i = 0; i < _pinSet.Length; i++)
        {
            if (_pinSet[i])
                n++;
        }

        return n;
    }

    bool AllPinsSet()
    {
        if (_pinSet == null || _pinSet.Length == 0)
            return false;

        for (int i = 0; i < _pinSet.Length; i++)
        {
            if (!_pinSet[i])
                return false;
        }

        return true;
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

        progressPanel = new GameObject("LockpickProgressPanel", typeof(RectTransform));
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
        float t = ProgressNormalized;
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

    void StoreCylinderPose()
    {
        if (keyholeCylinder == null)
            return;

        _cylinderBaseLocalRot = keyholeCylinder.localRotation;
        _cylinderPoseStored = true;
    }

    void ApplyCylinderTwist()
    {
        if (keyholeCylinder == null || !_cylinderPoseStored)
            return;

        Vector3 axis = twistAxisLocal.sqrMagnitude > 0.0001f ? twistAxisLocal.normalized : Vector3.forward;
        if (invertTwist)
            axis = -axis;
        keyholeCylinder.localRotation = _cylinderBaseLocalRot * Quaternion.AngleAxis(_twistAngle, axis);
    }

    /// <summary>
    /// Turns the cylinder back to rest with the normal release feel instead of snapping.
    /// Runs alongside the exit so it never delays the camera blend.
    /// </summary>
    void StartCylinderUnwind()
    {
        if (_unwindRoutine != null)
            StopCoroutine(_unwindRoutine);

        _unwindRoutine = StartCoroutine(UnwindCylinderRoutine());
    }

    IEnumerator UnwindCylinderRoutine()
    {
        // Guard against a zero return speed in the inspector spinning here forever.
        float elapsed = 0f;
        while (_cylinderPoseStored
            && keyholeCylinder != null
            && (_twistAngle > 0.01f || _wrenchLeadAngle > 0.01f)
            && elapsed < 3f)
        {
            TickCylinderTwist(held: false);
            elapsed += Time.deltaTime;
            yield return null;
        }

        _unwindRoutine = null;
        RestoreCylinderPose();
    }

    void StopCylinderUnwind()
    {
        if (_unwindRoutine == null)
            return;

        StopCoroutine(_unwindRoutine);
        _unwindRoutine = null;
    }

    void RestoreCylinderPose()
    {
        if (!_cylinderPoseStored || keyholeCylinder == null)
            return;

        keyholeCylinder.localRotation = _cylinderBaseLocalRot;
        _cylinderPoseStored = false;
        _twistAngle = 0f;
        _wrenchLeadAngle = 0f;
        ApplyWrenchVisual();
        StopCylinderRotateSfx();
    }

    void StoreWrenchPose()
    {
        if (wrenchVisual == null || _wrenchPoseStored)
            return;

        _wrenchBaseLocalRot = wrenchVisual.localRotation;
        _wrenchPoseStored = true;
    }

    void ApplyWrenchVisual()
    {
        if (wrenchVisual == null || !_wrenchPoseStored)
            return;

        // Extra local lead on top of cylinder parent rotation (wrench is typically under Cylinder).
        Vector3 axis = twistAxisLocal.sqrMagnitude > 0.0001f ? twistAxisLocal.normalized : Vector3.forward;
        if (invertTwist)
            axis = -axis;
        wrenchVisual.localRotation = _wrenchBaseLocalRot * Quaternion.AngleAxis(_wrenchLeadAngle, axis);
    }

    void RestoreWrenchPose()
    {
        if (!_wrenchPoseStored || wrenchVisual == null)
            return;

        _wrenchLeadAngle = 0f;
        wrenchVisual.localRotation = _wrenchBaseLocalRot;
    }

    void SetWrenchVisible(bool visible)
    {
        if (wrenchVisual == null)
            return;

        if (wrenchVisual.gameObject.activeSelf != visible)
            wrenchVisual.gameObject.SetActive(visible);
    }

    void StorePickPose()
    {
        if (pickVisual == null || _pickPoseStored)
            return;

        _pickBaseLocalPos = pickVisual.localPosition;
        _pickBaseLocalRot = pickVisual.localRotation;
        _pickPoseStored = true;
    }

    void ApplyPickVisual()
    {
        if (pickVisual == null || !_pickPoseStored)
            return;

        // Always slide in the pick parent's local forward (or configured axis).
        Vector3 axis = pickInsertAxisLocal.sqrMagnitude > 0.0001f
            ? pickInsertAxisLocal.normalized
            : Vector3.forward;
        float depth = _visualStageDepth01 * Mathf.Max(0f, pickInsertDistance);
        pickVisual.localPosition = _pickBaseLocalPos + axis * depth;

        Quaternion lift = Quaternion.Euler(pickLiftEuler * Mathf.Clamp01(_pickLift01));
        pickVisual.localRotation = _pickBaseLocalRot * lift;
    }

    void RestorePickPose()
    {
        if (!_pickPoseStored || pickVisual == null)
            return;

        _travel01 = 0f;
        _pinSlot = 0;
        _visualStageDepth01 = GetSnappedStageDepth01();
        _pickLift01 = 0f;
        _prevPickLift01 = 0f;
        _liftArmedForNextPin = true;
        pickVisual.localPosition = _pickBaseLocalPos;
        pickVisual.localRotation = _pickBaseLocalRot;
    }

    void SetPickVisible(bool visible)
    {
        if (pickVisual == null)
            return;

        if (pickVisual.gameObject.activeSelf != visible)
            pickVisual.gameObject.SetActive(visible);
    }

    void StorePose()
    {
        _savedPosition = transform.position;
        _savedRotation = transform.rotation;
        _poseStored = true;
    }

    void ComputePresentedPose(out Vector3 targetPos, out Quaternion targetRot)
    {
        if (presentPoseTarget != null)
        {
            targetPos = presentPoseTarget.position;
            targetRot = presentPoseTarget.rotation;
            return;
        }

        targetPos = _savedPosition + (_savedRotation * presentLocalPositionOffset);
        targetRot = _savedRotation * Quaternion.Euler(presentLocalEulerOffset);
    }

    IEnumerator AnimateToPresentedPose()
    {
        if (!_poseStored)
            yield break;

        ComputePresentedPose(out Vector3 targetPos, out Quaternion targetRot);
        yield return AnimatePose(transform.position, transform.rotation, targetPos, targetRot, presentDuration);
    }

    IEnumerator AnimateBackToSavedPose()
    {
        if (!_poseStored)
            yield break;

        yield return AnimatePose(
            transform.position,
            transform.rotation,
            _savedPosition,
            _savedRotation,
            restoreDuration);

        SnapToSavedPose();
    }

    IEnumerator AnimatePose(Vector3 fromPos, Quaternion fromRot, Vector3 toPos, Quaternion toRot, float duration)
    {
        float dur = Mathf.Max(0.05f, duration);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            transform.SetPositionAndRotation(
                Vector3.Lerp(fromPos, toPos, u),
                Quaternion.Slerp(fromRot, toRot, u));
            yield return null;
        }

        transform.SetPositionAndRotation(toPos, toRot);
    }

    void SnapToSavedPose()
    {
        if (!_poseStored)
            return;

        transform.SetPositionAndRotation(_savedPosition, _savedRotation);
        _poseStored = false;
    }

    void StartPoseRoutine(IEnumerator routine)
    {
        if (_poseRoutine != null)
            StopCoroutine(_poseRoutine);
        _poseRoutine = StartCoroutine(WrapPoseRoutine(routine));
    }

    IEnumerator WrapPoseRoutine(IEnumerator routine)
    {
        yield return routine;
        _poseRoutine = null;
    }

    void StopPoseRoutine()
    {
        if (_poseRoutine == null)
            return;

        StopCoroutine(_poseRoutine);
        _poseRoutine = null;
    }

    void BeginSuccessExit()
    {
        if (_stage != Stage.Playing && _stage != Stage.Presenting)
            return;

        StartCoroutine(FinishSuccessRoutine());
    }

    void BeginCancelExit()
    {
        if (_stage != Stage.Playing && _stage != Stage.Presenting)
            return;

        if (cancelSfx != null)
            SoundManager.Instance?.PlaySFXAt(cancelSfx, transform.position, sfxVolume);

        var cancel = _onCancel;
        _onSuccess = null;
        _onCancel = null;
        StartCoroutine(FinishExitRoutine(success: false, cancel));
    }

    IEnumerator FinishSuccessRoutine()
    {
        if (_stage != Stage.Playing && _stage != Stage.Presenting)
            yield break;

        _stage = Stage.Exiting;

        var tool = _tool;
        var success = _onSuccess;
        _onSuccess = null;
        _onCancel = null;

        success?.Invoke(tool);
        yield return FinishExitRoutine(success: true, null);
    }

    IEnumerator FinishExitRoutine(bool success, Action cancelCallback)
    {
        _stage = Stage.Exiting;
        HudManager.Instance?.ClearSubtitle();

        StopPoseRoutine();
        StartCylinderUnwind();
        RestoreWrenchPose();
        RestorePickPose();
        if (hidePickWhenIdle)
            SetPickVisible(false);
        if (hideWrenchWhenIdle)
            SetWrenchVisible(false);

        var playerCam = FindPlayerVirtualCamera();
        var brain = FindFirstObjectByType<CinemachineBrain>();

        // Player keeps the look pitch they entered with — no rotation clear on exit.
        CinemachineVirtualCameraBase.StandbyUpdateMode savedStandby = default;
        if (playerCam != null)
        {
            savedStandby = playerCam.StandbyUpdate;
            playerCam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
        }

        if (lockpickCamera != null)
            lockpickCamera.Priority = 0;

        StartPoseRoutine(AnimateBackToSavedPose());

        float wait = Mathf.Max(0.5f, cameraBlendOutSeconds);
        float t = 0f;
        while (t < wait)
        {
            SetProgressVisible(false);
            if (brain != null && !brain.IsBlending && t > 0.1f)
                break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (playerCam != null)
            playerCam.StandbyUpdate = savedStandby;

        ForceAbortCleanup(snapPose: false);
        if (!success)
            cancelCallback?.Invoke();
    }

    void CompleteUnlockFromMinigame(InventoryItem item)
    {
        if (item == null || !isLocked)
            return;

        isLocked = false;
        HudManager.Instance?.ShowSubtitle(
            Loc.Format("gameplay.item_unlocked", item.GetLocalizedName()), 2.5f, HudManager.Instance.SuccessColor);

        // Padlock always spends the lockpick.
        InventoryManager.Instance?.RemoveItem(item);
        InventoryUI.Instance?.RefreshUI();

        AudioClip sfx = unlockSound != null ? unlockSound : item.useSuccessSound;
        InteractionFeedback.PlaySound(sfx, transform.position);

        ApplyUnlockRelease();
        onUnlocked?.Invoke();
    }

    /// <summary>Checkpoint load: already picked. Opens silently and frees the door, no lockpick spent.</summary>
    public void RestoreUnlocked()
    {
        if (!isLocked)
            return;
        isLocked = false;
        ApplyUnlockRelease(silent: true);
    }

    void ApplyUnlockRelease(bool silent = false)
    {
        if (shackle != null)
        {
            shackle.localPosition = shackleRaisedLocalPosition;
            shackle.localRotation = Quaternion.Euler(shackleRaisedLocalEuler);
        }

        if (chainDetachJoint != null)
            chainDetachJoint.connectedBody = null;

        if (chainRigidbodies != null)
        {
            for (int i = 0; i < chainRigidbodies.Length; i++)
            {
                Rigidbody rb = chainRigidbodies[i];
                if (rb == null)
                    continue;

                rb.isKinematic = false;
                rb.useGravity = true;
            }
        }

        if (chainFallSfx != null && !silent)
            SoundManager.Instance?.PlaySFXAt(chainFallSfx, transform.position, sfxVolume);

        // Silent — padlock already showed unlock feedback.
        if (doorToUnlockSilently == null)
        {
            var cage = UnityEngine.Object.FindFirstObjectByType<Cage>();
            if (cage != null)
                doorToUnlockSilently = cage.cageDoor;
        }

        if (doorToUnlockSilently != null)
        {
            doorToUnlockSilently.UnlockSilently();
            var doorController = doorToUnlockSilently.GetComponentInParent<FS_Atmo.DoorController>();
            if (doorController != null)
                doorController.SetLocked(false);
        }

        if (_chainSettleRoutine != null)
            StopCoroutine(_chainSettleRoutine);
        _chainSettleRoutine = StartCoroutine(SettleChainsAfterDelay());
    }

    IEnumerator SettleChainsAfterDelay()
    {
        float wait = Mathf.Max(0f, chainSettleSeconds);
        if (wait > 0f)
            yield return new WaitForSeconds(wait);

        if (chainRigidbodies != null)
        {
            for (int i = 0; i < chainRigidbodies.Length; i++)
            {
                Rigidbody rb = chainRigidbodies[i];
                if (rb == null)
                    continue;

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;

                var cols = rb.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null)
                        cols[c].enabled = false;
                }
            }
        }

        _chainSettleRoutine = null;
    }

    void ForceAbortCleanup(bool snapPose = true)
    {
        if (snapPose)
        {
            StopPoseRoutine();
            SnapToSavedPose();
            StopCylinderUnwind();
        }

        // A still-running unwind owns the cylinder and restores it when it lands.
        if (_unwindRoutine == null)
            RestoreCylinderPose();

        RestoreWrenchPose();
        RestorePickPose();
        if (hidePickWhenIdle)
            SetPickVisible(false);
        if (hideWrenchWhenIdle)
            SetWrenchVisible(false);
        StopCylinderRotateSfx();
        if (_progressFade != null)
            _progressFade.SetVisible(false, instant: true);
        TeardownOwnedProgressUi();

        if (lockpickCamera != null)
            lockpickCamera.Priority = 0;
        if (_cameraKick != null)
        {
            Destroy(_cameraKick);
            _cameraKick = null;
        }

        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonLockpick))
            PlayerControlGate.Release(PlayerControlGate.ReasonLockpick);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        HudManager.Instance?.ClearSubtitle();

        if (Active == this)
            Active = null;

        _tool = null;
        _onSuccess = null;
        _onCancel = null;
        _stage = Stage.Idle;
    }

    void PlayLockedFeedback()
    {
        _nextInteractTime = Time.time + lockedFeedbackCooldown;
        if (lockedSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(lockedSound, transform.position);
    }

    static Transform FindNamedChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamedChild(root.GetChild(i), childName);
            if (found != null)
                return found;
        }

        return null;
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

    static bool WasCancelPressedThisFrame()
    {
        var kb = Keyboard.current;
        if (kb == null)
            return false;

        if (kb.leftWindowsKey.wasPressedThisFrame || kb.rightWindowsKey.wasPressedThisFrame)
            return false;

        return kb.anyKey.wasPressedThisFrame;
    }

    static string LocOrFallback(string key, string fallback)
    {
        string s = Loc.Get(key);
        if (string.IsNullOrEmpty(s)
            || s == key
            || s.StartsWith("gameplay.")
            || s.IndexOf("No translation", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return fallback;
        return s;
    }
}
