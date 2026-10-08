using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class LighterStrike : MonoBehaviour
{
    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string strikeTrigger = "Strike";

    [Header("Flame")]
    [SerializeField] private GameObject flame;

    [Tooltip("Seconds from the start of the strike clip to ignition (clip frame 10 at 30 fps). Ignored if the clip calls Ignite() from an animation event.")]
    [Min(0f)]
    [SerializeField] private float igniteDelay = 10f / 30f;

    [SerializeField] private bool useAnimationEvent;

    public bool IsLit { get; private set; }

    private bool _striking;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (flame != null)
            flame.SetActive(false);
    }

    private void Update()
    {
        if (IsLit || _striking || Mouse.current == null)
            return;
        if (Cursor.lockState != CursorLockMode.Locked)
            return;
        if (Mouse.current.leftButton.wasPressedThisFrame)
            Strike();
    }

    public void Strike()
    {
        if (IsLit || _striking)
            return;
        _striking = true;
        if (animator != null)
            animator.SetTrigger(strikeTrigger);
        if (!useAnimationEvent)
            StartCoroutine(IgniteAfterDelay());
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

    private IEnumerator IgniteAfterDelay()
    {
        yield return new WaitForSeconds(igniteDelay);
        Ignite();
    }
}
