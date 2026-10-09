using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Zippo controls on the lighter's HeldItemView. The draw ends with the lid open.
/// Left-click opens the lid if it is closed, otherwise strikes; right-click closes the lid, which puts
/// the flame out. Lid open uses the draw clip, lid close and the put-down its reversed copy, and the closed
/// lid loops its own idle (starting on draw frame 20). Timings below are draw clip times.
/// FollowHeldItemSocket keeps the flame lights off until IsLit. A lighter picked up burning (DrawLitNextTime) draws
/// with the flame on and the lid held open by the lid layer.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(HeldItemView))]
public sealed class HeldLighter : MonoBehaviour
{
    [Header("Animator states")]
    [SerializeField] private Animator animator;
    [SerializeField] private string drawState = "Draw";
    [SerializeField] private string idleState = "Idle";
    [SerializeField] private string strikeState = "Strike";
    [SerializeField] private string lidOpenState = "LidOpen";
    [SerializeField] private string lidCloseState = "LidClose";
    [SerializeField] private string lidClosedState = "LidClosed";
    [SerializeField] private string putDownState = "PutDown";

    [Tooltip("Masked layer (thumb + lid) that closes the lid during the put-down while the arm lowers. " +
             "Must match the HeldItemView's put-down overlay layer.")]
    [SerializeField] private string lidLayer = "Lid";

    [Header("Draw speed")]
    [Tooltip("Float parameter used as the Draw state's speed multiplier.")]
    [SerializeField] private string drawSpeedParameter = "DrawSpeed";

    [Tooltip("Draw speed while the hand comes up (draw clip up to the closed-lid pose).")]
    [Min(0.01f)]
    [SerializeField] private float drawRaiseSpeed = 1.65f;

    [Tooltip("Draw speed from the closed-lid pose on, i.e. the lid opening. Keep in line with the LidOpen state's speed.")]
    [Min(0.01f)]
    [SerializeField] private float drawLidSpeed = 4f;

    [Tooltip("Seconds of blending into the strike and the lid close / open (the idle loops drift from their start pose).")]
    [Min(0f)]
    [SerializeField] private float blend = 0.08f;

    [Header("Draw clip timing (0-1)")]
    [Tooltip("Hand up, lid closed, thumb resting on the lid (frame 20 of 68); first frame of the closed-lid idle.")]
    [Range(0f, 1f)]
    [SerializeField] private float lidClosedPose = 20f / 68f;

    [Tooltip("Lid touches the case (frame 33 of 68). Closing past this puts the flame out.")]
    [Range(0f, 1f)]
    [SerializeField] private float lidShut = 33f / 68f;

    [Tooltip("Thumb back at rest after opening (frame 58 of 68); the rest of the clip is still. Closing starts here.")]
    [Range(0f, 1f)]
    [SerializeField] private float lidOpenRest = 58f / 68f;

    [Tooltip("Lid fully open with the thumb still on it (frame 45 of 68). The put-down's lid closing starts here at " +
             "the latest, skipping the thumb settling back, so the lid shuts before the arm is out of view.")]
    [Range(0f, 1f)]
    [SerializeField] private float lidCloseStart = 45f / 68f;

    [Header("Strike")]
    [Tooltip("Point in the strike clip (0-1) where the flame catches: frame 10 of 30. Follows the state's speed. " +
             "Ignored if the clip calls Ignite() from an animation event.")]
    [Range(0f, 1f)]
    [SerializeField] private float igniteNormalizedTime = 10f / 30f;

    [SerializeField] private bool useAnimationEvent;

    [Header("Lit draw (picked up burning)")]
    [Tooltip("Lid layer state holding the open lid (the idle clip) while the hand comes up with the flame on.")]
    [SerializeField] private string lidOpenHoldState = "LidOpenHold";

    [Tooltip("Seconds of blending from the raised hand into the idle, and of fading the lid layer out.")]
    [Min(0f)]
    [SerializeField] private float litDrawBlend = 0.2f;

    [Header("Flame")]
    [Tooltip("Optional extra object switched on at ignition.")]
    [SerializeField] private GameObject flame;

    [Header("Audio")]
    [Tooltip("Played as the lid starts opening (left-click or the end of the draw). Trimmed so the clink lands as the lid flips.")]
    [SerializeField] private AudioClip lidOpenSfx;
    [SerializeField, Range(0f, 1f)] private float lidOpenVolume = 0.7f;
    [Tooltip("Played when the strike starts; its flick peaks where the flame catches.")]
    [SerializeField] private AudioClip strikeSfx;
    [SerializeField, Range(0f, 1f)] private float strikeVolume = 1f;

    public bool IsLit { get; private set; }
    public bool IsLidOpen { get; private set; } = true;

    private HeldItemView _view;
    private int _lidLayer = -1;
    private int _drawSpeedId;
    private bool _hasDrawSpeed;
    private bool _striking;
    private bool _closing;
    private bool _drawLitNext;
    private bool _litDraw;
    private bool _litDrawHandedOff;
    private float _lastDrawTime = -1f;

    private void Awake()
    {
        _view = GetComponent<HeldItemView>();
        if (animator == null)
            animator = _view.Animator != null ? _view.Animator : GetComponentInChildren<Animator>();
        if (animator != null && !string.IsNullOrEmpty(lidLayer))
            _lidLayer = animator.GetLayerIndex(lidLayer);
        if (animator != null && !string.IsNullOrEmpty(drawSpeedParameter))
        {
            _drawSpeedId = Animator.StringToHash(drawSpeedParameter);
            foreach (AnimatorControllerParameter p in animator.parameters)
                _hasDrawSpeed |= p.nameHash == _drawSpeedId && p.type == AnimatorControllerParameterType.Float;
        }
        if (flame != null)
            flame.SetActive(false);
        _view.Drawn += OnDrawn;
    }

    private void OnDestroy()
    {
        if (_view != null)
            _view.Drawn -= OnDrawn;
    }

    /// <summary>
    /// The next draw keeps the flame on (a burning lighter was picked up): the lid stays open while the hand
    /// comes up, then it goes straight to the lit idle without opening the lid or striking.
    /// </summary>
    public void DrawLitNextTime() => _drawLitNext = true;

    private void OnEnable()
    {
        IsLidOpen = true;
        _closing = false;
        Extinguish();

        _litDraw = _drawLitNext;
        _litDrawHandedOff = false;
        _drawLitNext = false;
        _lastDrawTime = -1f;
        if (_litDraw)
            Ignite();
    }

    private void OnDisable()
    {
        _closing = false;
        _litDraw = false;
        Extinguish();
    }

    private void OnDrawn()
    {
        if (!_litDraw || animator == null || _lidLayer <= 0)
            return;
        animator.Play(lidOpenHoldState, _lidLayer, 0f);
        animator.SetLayerWeight(_lidLayer, 1f);
        animator.Update(0f);
    }

    private void Update()
    {
        if (animator == null)
            return;

        UpdateDrawSpeed();
        TrackDrawLidOpen();
        TrackReversedLid();
        if (_view.IsHolstering)
        {
            _litDraw = false;
            return;
        }

        TickLitDraw();
        if (_litDraw || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
            return;
        // Left-click while G is held places the lighter (InventoryUI); don't strike or open the lid too.
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsDropKeyHeld)
            return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!IsLidOpen && InState(lidClosedState))
                OpenLid();
            else if (IsLidOpen && !IsLit && !_striking && InState(idleState))
                Strike();
        }
        else if (Mouse.current.rightButton.wasPressedThisFrame && IsLidOpen && !_striking && InState(idleState))
            CloseLid();
    }

    private void LateUpdate()
    {
        if (animator == null || _view.IsHolstering)
            return;

        // Put-down reverses the draw from wherever the lid is, skipping the still tail. With the lid open the
        // arm lowers from the closed-lid pose while the lid layer closes the lid at the same time.
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        float drawTime = lidOpenRest;
        if (state.IsName(drawState) || state.IsName(lidOpenState))
            drawTime = Mathf.Clamp01(state.normalizedTime);
        else if (state.IsName(lidCloseState))
            drawTime = 1f - Mathf.Clamp01(state.normalizedTime);
        else if (state.IsName(lidClosedState))
            drawTime = lidClosedPose;
        drawTime = Mathf.Min(drawTime, lidOpenRest);

        // During a lit draw the lid layer holds the lid open even while the hand is still coming up.
        bool lidOpen = (drawTime > lidClosedPose || _litDraw) && _lidLayer > 0;
        _view.PutDownStartTime = lidOpen ? Mathf.Min(drawTime, lidClosedPose) : drawTime;
        _view.PutDownOverlayStartTime = !lidOpen ? -1f : _litDraw ? lidCloseStart : Mathf.Min(drawTime, lidCloseStart);
    }

    // Lit draw: once the hand is up (closed-lid pose of the draw clip), cross-fade into the idle while the
    // lid layer, which held the lid open, fades out. Both show the same open-lid pose, so the hand-off is clean.
    private void TickLitDraw()
    {
        if (!_litDraw)
            return;

        if (!_litDrawHandedOff)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName(drawState) && state.normalizedTime < lidClosedPose)
                return;
            _litDrawHandedOff = true;
            animator.CrossFadeInFixedTime(idleState, litDrawBlend, 0, 0f);
        }

        if (_lidLayer <= 0)
        {
            _litDraw = false;
            return;
        }

        float weight = litDrawBlend > 0f ? animator.GetLayerWeight(_lidLayer) - Time.deltaTime / litDrawBlend : 0f;
        animator.SetLayerWeight(_lidLayer, Mathf.Max(0f, weight));
        if (weight <= 0f)
            _litDraw = false;
    }

    public void OpenLid()
    {
        IsLidOpen = true;
        CrossFade(lidOpenState, lidClosedPose);
        PlaySfx(lidOpenSfx, lidOpenVolume);
    }

    public void CloseLid()
    {
        IsLidOpen = false;
        _closing = true;
        CrossFade(lidCloseState, 1f - lidOpenRest);
    }

    public void Strike()
    {
        if (IsLit || _striking)
            return;
        _striking = true;
        CrossFade(strikeState, 0f);
        PlaySfx(strikeSfx, strikeVolume);
        if (!useAnimationEvent)
            StartCoroutine(IgniteAtStrikeFrame());
    }

    // Animation event hook on the strike clip's ignition frame.
    public void Ignite()
    {
        _striking = false;
        IsLit = true;
        if (flame != null)
            flame.SetActive(true);
    }

    public void Extinguish()
    {
        StopAllCoroutines();
        _striking = false;
        IsLit = false;
        if (flame != null)
            flame.SetActive(false);
    }

    private void UpdateDrawSpeed()
    {
        if (!_hasDrawSpeed)
            return;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        bool lidPart = state.IsName(drawState) && state.normalizedTime >= lidClosedPose;
        animator.SetFloat(_drawSpeedId, lidPart ? drawLidSpeed : drawRaiseSpeed);
    }

    // The draw ends with the lid opening: sound it when the draw crosses the closed-lid pose. A lit draw keeps the lid open.
    private void TrackDrawLidOpen()
    {
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName(drawState) || _litDraw)
        {
            _lastDrawTime = -1f;
            return;
        }

        float t = state.normalizedTime;
        if (_lastDrawTime >= 0f && _lastDrawTime < lidClosedPose && t >= lidClosedPose)
            PlaySfx(lidOpenSfx, lidOpenVolume);
        _lastDrawTime = t;
    }

    private static void PlaySfx(AudioClip clip, float volume)
    {
        if (clip != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(clip, volume);
    }

    // While the lid section plays reversed (closing or put-down): flame out once the lid shuts,
    // and start the closed-lid idle when a close finishes.
    private void TrackReversedLid()
    {
        AnimatorStateInfo state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);
        bool closing = state.IsName(lidCloseState);
        if (!closing && !state.IsName(putDownState))
            return;

        if (!closing && _lidLayer > 0 && animator.GetLayerWeight(_lidLayer) > 0f)
            state = animator.GetCurrentAnimatorStateInfo(_lidLayer);

        float drawTime = 1f - state.normalizedTime;
        if (IsLit && drawTime <= lidShut)
            Extinguish();
        if (_closing && closing && drawTime <= lidClosedPose)
        {
            _closing = false;
            animator.Play(lidClosedState, 0, 0f);
        }
    }

    private bool InState(string stateName) =>
        !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);

    private void CrossFade(string stateName, float normalizedOffset)
    {
        float sourceLength = animator.GetCurrentAnimatorStateInfo(0).length;
        animator.CrossFade(stateName, sourceLength > 0f ? blend / sourceLength : 0f, 0, normalizedOffset);
    }

    private IEnumerator IgniteAtStrikeFrame()
    {
        bool entered = false;
        while (true)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName(strikeState))
                state = animator.GetNextAnimatorStateInfo(0);

            if (state.IsName(strikeState))
            {
                entered = true;
                if (state.normalizedTime >= igniteNormalizedTime)
                    break;
            }
            else if (entered)
                break;

            yield return null;
        }
        Ignite();
    }
}
