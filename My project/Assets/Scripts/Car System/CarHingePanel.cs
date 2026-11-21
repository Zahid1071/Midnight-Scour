using System.Collections;
using UnityEngine;

/// <summary>
/// Simple open/close hinge for car hood / doors. Interact toggles.
/// </summary>
[DisallowMultipleComponent]
public class CarHingePanel : MonoBehaviour, IInteractable
{
    public enum PanelKind { Hood, Door, Trunk, FuelDoor }

    [SerializeField] PanelKind kind = PanelKind.Hood;
    [SerializeField] Transform hinge;
    [SerializeField] Vector3 closedLocalEuler;
    [SerializeField] Vector3 openLocalEuler;
    [SerializeField] float duration = 0.55f;
    [SerializeField] AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] AudioClip openSfx;
    [SerializeField] AudioClip closeSfx;
    [SerializeField, Range(0f, 1f)] float sfxVolume = 0.7f;
    [SerializeField] string openPromptKey;
    [SerializeField] string closePromptKey;
    [SerializeField] Collider interactCollider;

    bool _open;
    bool _busy;
    Coroutine _move;

    public bool IsOpen => _open;
    public PanelKind Kind => kind;
    public event System.Action<CarHingePanel, bool> OnToggled;

    void Awake()
    {
        if (hinge == null)
            hinge = transform;
        if (interactCollider == null)
            interactCollider = GetComponent<Collider>();
        // Start closed visually
        hinge.localRotation = Quaternion.Euler(closedLocalEuler);
        _open = false;
    }

    public bool Interact()
    {
        if (_busy)
            return false;
        SetOpen(!_open, animated: true);
        return true;
    }

    public void SetOpen(bool open, bool animated)
    {
        if (_open == open && !_busy)
            return;

        if (_move != null)
            StopCoroutine(_move);

        if (!animated || !isActiveAndEnabled || duration <= 0.01f)
        {
            hinge.localRotation = Quaternion.Euler(open ? openLocalEuler : closedLocalEuler);
            _open = open;
            _busy = false;
            PlaySfx(open);
            OnToggled?.Invoke(this, _open);
            return;
        }

        _move = StartCoroutine(Animate(open));
    }

    IEnumerator Animate(bool open)
    {
        _busy = true;
        // Commit open state immediately so hood-gated UIs react on click, not after the swing.
        _open = open;
        OnToggled?.Invoke(this, _open);

        Quaternion from = hinge.localRotation;
        Quaternion to = Quaternion.Euler(open ? openLocalEuler : closedLocalEuler);
        PlaySfx(open);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = curve.Evaluate(Mathf.Clamp01(t / duration));
            hinge.localRotation = Quaternion.SlerpUnclamped(from, to, u);
            yield return null;
        }

        hinge.localRotation = to;
        _busy = false;
        _move = null;
    }

    void PlaySfx(bool opening)
    {
        var clip = opening ? openSfx : closeSfx;
        if (clip == null || SoundManager.Instance == null)
            return;
        SoundManager.Instance.PlaySFXAt(clip, hinge.position, sfxVolume);
    }

#if UNITY_EDITOR
    public void EditorConfigure(Transform hingeTransform, Vector3 closedEuler, Vector3 openEuler, PanelKind panelKind)
    {
        hinge = hingeTransform;
        closedLocalEuler = closedEuler;
        openLocalEuler = openEuler;
        kind = panelKind;
    }
#endif
}
