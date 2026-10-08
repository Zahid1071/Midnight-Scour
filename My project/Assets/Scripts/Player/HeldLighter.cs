using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Zippo controls on the lighter's HeldItemView. The draw ends with the lid open.
/// Left-click opens the lid if it is closed, otherwise strikes; right-click closes the lid, which puts
/// the flame out. Lid open uses the draw clip, lid close and the put-down its reversed copy, and the closed
/// lid loops its own idle (starting on draw frame 20). Timings below are draw clip times.
/// FollowHeldItemSocket keeps the flame lights off until IsLit.
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

    [Header("Strike")]
    [Tooltip("Point in the strike clip (0-1) where the flame catches: frame 10 of 30. Follows the state's speed. " +
             "Ignored if the clip calls Ignite() from an animation event.")]
    [Range(0f, 1f)]
    [SerializeField] private float igniteNormalizedTime = 10f / 30f;

    [SerializeField] private bool useAnimationEvent;

    [Header("Flame")]
    [Tooltip("Optional extra object switched on at ignition.")]
    [SerializeField] private GameObject flame;

    public bool IsLit { get; private set; }
    public bool IsLidOpen { get; private set; } = true;

    private HeldItemView _view;
    private bool _striking;
    private bool _closing;

    private void Awake()
    {
        _view = GetComponent<HeldItemView>();
        if (animator == null)
            animator = _view.Animator != null ? _view.Animator : GetComponentInChildren<Animator>();
        if (flame != null)
            flame.SetActive(false);
    }

    private void OnEnable()
    {
        IsLidOpen = true;
        _closing = false;
        Extinguish();
    }

    private void OnDisable()
    {
        _closing = false;
        Extinguish();
    }

    private void Update()
    {
        if (animator == null)
            return;

        TrackReversedLid();
        if (_view.IsHolstering || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
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

        // Put-down reverses the draw from wherever the lid is, skipping the still tail.
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        float drawTime = lidOpenRest;
        if (state.IsName(drawState) || state.IsName(lidOpenState))
            drawTime = Mathf.Clamp01(state.normalizedTime);
        else if (state.IsName(lidCloseState))
            drawTime = 1f - Mathf.Clamp01(state.normalizedTime);
        else if (state.IsName(lidClosedState))
            drawTime = lidClosedPose;
        _view.PutDownStartTime = Mathf.Min(drawTime, lidOpenRest);
    }

    public void OpenLid()
    {
        IsLidOpen = true;
        CrossFade(lidOpenState, lidClosedPose);
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
