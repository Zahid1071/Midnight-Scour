using System.Collections;
using UnityEngine;

/// <summary>
/// Bench vise with the wrench clamped in its jaws: insert the bar (LMB), then turn it (E). The handle spins,
/// the jaw opens and the wrench drops out to the floor, where it can be picked up.
/// </summary>
public class BenchVicePuzzle : MonoBehaviour, IInteractable, IItemReceiver
{
    public static BenchVicePuzzle Instance { get; private set; }

    [SerializeField] InventoryItem barItem;
    [SerializeField] GameObject barVisual;
    [Tooltip("Handle hub; spins about its local Y (the screw) while the bar turns.")]
    [SerializeField] Transform dialTransform;
    [Tooltip("Moving jaw; slides out along its local Z as the screw opens.")]
    [SerializeField] Transform clampTransform;

    [Header("Turning")]
    [SerializeField] float turnDegrees = 540f;
    [SerializeField] float turnDuration = 1.8f;
    [SerializeField] float jawOpenDistance = 0.025f;
    [SerializeField] AudioClip turnSound;
    [SerializeField, Range(0f, 1f)] float turnVolume = 0.8f;

    [Header("Wrench")]
    [SerializeField] Transform wrench;
    [Tooltip("Rest pose the wrench falls to.")]
    [SerializeField] Transform wrenchLanding;
    [Tooltip("Fraction of the turn at which the jaw lets go of the wrench.")]
    [SerializeField, Range(0f, 1f)] float wrenchReleaseAt = 0.65f;
    [SerializeField] AudioClip wrenchDropSound;
    [SerializeField, Range(0f, 1f)] float wrenchDropVolume = 0.9f;

    [Header("Stuck Wrench Shot")]
    [Tooltip("Closes in on the stuck wrench the first time ShowStuckWrench is called (e.g. by the vise note's On Closed).")]
    [SerializeField] HideOptionsShot stuckWrenchShot;

    [Header("Post-completion")]
    [Tooltip("Objects moved off the Interactable layer once the wrench is released (defaults to this + bar visual).")]
    [SerializeField] GameObject[] disableInteractLayers;

    bool _barInserted;
    bool _wrenchReleased;
    bool _isAnimating;
    bool _stuckWrenchShown;

    public bool IsWrenchReleased => _wrenchReleased;
    public bool BarInserted => _barInserted;

    void Awake()
    {
        Instance = this;
        if (barVisual != null)
            barVisual.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool Interact()
    {
        if (_wrenchReleased || _isAnimating)
            return false;

        if (!_barInserted)
        {
            ShowContextualHint();
            return false;
        }

        StartCoroutine(TurnRoutine());
        return true;
    }

    /// <summary>Plays the shot on the wrench in the jaws, only the first time and only while it is still stuck.</summary>
    public void ShowStuckWrench()
    {
        if (_stuckWrenchShown || _wrenchReleased || _isAnimating || stuckWrenchShot == null || !stuckWrenchShot.isActiveAndEnabled)
            return;
        _stuckWrenchShown = true;
        stuckWrenchShot.StartCoroutine(stuckWrenchShot.Play());
    }

    public string GetContextualHintMessage(InventoryItem held)
    {
        if (_wrenchReleased || _barInserted)
            return null;
        return Loc.Get("gameplay.need_bar");
    }

    void ShowContextualHint()
    {
        string message = GetContextualHintMessage(null);
        if (!string.IsNullOrWhiteSpace(message))
            InteractionFeedback.Fail(message, 3f);
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (_isAnimating || item == null || _wrenchReleased)
            return false;

        if (!_barInserted && item == barItem)
            return TryInsertBar(item);

        if (_barInserted)
            return Interact();

        ShowContextualHint();
        return false;
    }

    bool TryInsertBar(InventoryItem item)
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.dont_have_item"), 2.5f);
            return false;
        }

        if (item.consumeOnUse)
            InventoryManager.Instance.RemoveItem(item);

        _barInserted = true;
        if (barVisual != null)
            barVisual.SetActive(true);

        AudioClip sfx = item.useSuccessSound;
        if (sfx != null)
            InteractionFeedback.PlaySound(sfx, transform.position);
        InteractionUIManager.Instance?.PlaySuccessShake();
        return true;
    }

    IEnumerator TurnRoutine()
    {
        _isAnimating = true;
        Vector3 soundAt = dialTransform != null ? dialTransform.position : transform.position;
        if (turnSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(turnSound, soundAt, turnVolume);
        InteractionUIManager.Instance?.PlaySuccessShake();

        Quaternion dialStart = dialTransform != null ? dialTransform.localRotation : Quaternion.identity;
        Vector3 clampStart = clampTransform != null ? clampTransform.localPosition : Vector3.zero;
        bool dropped = false;
        for (float t = 0f; t < turnDuration; t += Time.deltaTime)
        {
            float u = t / turnDuration;
            float eased = Mathf.SmoothStep(0f, 1f, u);
            if (dialTransform != null)
                dialTransform.localRotation = dialStart * Quaternion.Euler(0f, turnDegrees * eased, 0f);
            if (clampTransform != null)
                clampTransform.localPosition = clampStart + Vector3.forward * (jawOpenDistance * eased);
            if (!dropped && u >= wrenchReleaseAt)
            {
                dropped = true;
                StartCoroutine(DropWrenchRoutine());
            }
            yield return null;
        }
        if (dialTransform != null)
            dialTransform.localRotation = dialStart * Quaternion.Euler(0f, turnDegrees, 0f);
        if (clampTransform != null)
            clampTransform.localPosition = clampStart + Vector3.forward * jawOpenDistance;
        if (!dropped)
            yield return DropWrenchRoutine();

        ClearInteractableLayers();
        _isAnimating = false;
    }

    IEnumerator DropWrenchRoutine()
    {
        if (wrench == null || wrenchLanding == null)
        {
            _wrenchReleased = true;
            yield break;
        }

        Vector3 from = wrench.position;
        Quaternion fromRot = wrench.rotation;
        Vector3 to = wrenchLanding.position;
        Quaternion toRot = wrenchLanding.rotation;
        float drop = Mathf.Max(0.05f, from.y - to.y);
        float fallTime = Mathf.Sqrt(2f * drop / 9.81f);
        for (float t = 0f; t < fallTime; t += Time.deltaTime)
        {
            float u = t / fallTime;
            Vector3 p = Vector3.Lerp(from, to, u);
            p.y = from.y - 0.5f * 9.81f * t * t;
            wrench.SetPositionAndRotation(p, Quaternion.Slerp(fromRot, toRot, u));
            yield return null;
        }
        wrench.SetPositionAndRotation(to, toRot);
        if (wrenchDropSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(wrenchDropSound, to, wrenchDropVolume);

        // One small hop off the concrete before it settles.
        const float hopTime = 0.16f;
        const float hopHeight = 0.035f;
        Quaternion tilted = toRot * Quaternion.Euler(0f, 0f, 8f);
        for (float t = 0f; t < hopTime; t += Time.deltaTime)
        {
            float u = t / hopTime;
            wrench.SetPositionAndRotation(to + Vector3.up * (4f * hopHeight * u * (1f - u)),
                Quaternion.Slerp(tilted, toRot, u));
            yield return null;
        }
        wrench.SetPositionAndRotation(to, toRot);
        _wrenchReleased = true;
    }

    void ClearInteractableLayers()
    {
        const int defaultLayer = 0;

        if (disableInteractLayers != null && disableInteractLayers.Length > 0)
        {
            for (int i = 0; i < disableInteractLayers.Length; i++)
            {
                if (disableInteractLayers[i] != null)
                    disableInteractLayers[i].layer = defaultLayer;
            }
            return;
        }

        gameObject.layer = defaultLayer;
        if (barVisual != null)
            barVisual.layer = defaultLayer;
    }
}
