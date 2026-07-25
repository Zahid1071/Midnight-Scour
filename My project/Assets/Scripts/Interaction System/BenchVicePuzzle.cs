using System.Collections;
using UnityEngine;

/// <summary>
/// Bench vise: insert bar (LMB) → bar won't turn (E) → strike with hammer (LMB) → wrench can be picked up.
/// </summary>
public class BenchVicePuzzle : MonoBehaviour, IInteractable, IItemReceiver
{
    public static BenchVicePuzzle Instance { get; private set; }

    [SerializeField] InventoryItem barItem;
    [SerializeField] InventoryItem hammerItem;
    [SerializeField] GameObject barVisual;
    [SerializeField] Transform dialTransform;
    [SerializeField] Animator animator;
    [SerializeField] string strikeAnimatorTrigger = "Strike";
    [Header("Dial strike (used when no Animator)")]
    [SerializeField] float dialStrikeDegrees = 45f;
    [SerializeField] float dialStrikeDuration = 0.35f;
    [SerializeField] AudioClip strikeSound;
    [Header("NPC Suspicion")]
    [Tooltip("Footstep-suspicion added to NPCs when the hammer strikes (same mechanism as body bag impact).")]
    [Min(0f)]
    [SerializeField] float npcSuspicionOnStrike = 300f;
    [SerializeField] float strikeSfxVolume = 1f;

    [Header("Post-completion")]
    [Tooltip("Objects moved off the Interactable layer once the wrench is released (defaults to this + bar visual).")]
    [SerializeField] GameObject[] disableInteractLayers;

    bool _barInserted;
    bool _wrenchReleased;
    bool _isAnimating;

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
        if (_wrenchReleased)
            return false;

        ShowContextualHint(InventoryUI.Instance?.GetSelectedItem());
        return false;
    }

    public string GetContextualHintMessage(InventoryItem held)
    {
        if (_wrenchReleased)
            return null;

        if (!_barInserted)
        {
            if (held == barItem || held == hammerItem)
                return Loc.Get("gameplay.use_bar_vise");
            return Loc.Get("gameplay.need_bar");
        }

        if (held == hammerItem)
            return Loc.Get("gameplay.use_hammer_bar");

        return Loc.Get("gameplay.bar_stuck_rusty");
    }

    void ShowContextualHint(InventoryItem held)
    {
        string message = GetContextualHintMessage(held);
        if (!string.IsNullOrWhiteSpace(message))
            InteractionFeedback.Fail(message, 3f);
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (_isAnimating)
            return false;

        if (item == null)
            return false;

        if (!_barInserted && item == barItem)
            return TryInsertBar(item);

        if (_barInserted && !_wrenchReleased && item == hammerItem)
            return TryStrikeWithHammer(item);

        ShowContextualHint(item);
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

    bool TryStrikeWithHammer(InventoryItem item)
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.dont_have_item"), 2.5f);
            return false;
        }

        StartCoroutine(StrikeRoutine(item));
        return true;
    }

    IEnumerator StrikeRoutine(InventoryItem hammer)
    {
        _isAnimating = true;

        Vector3 noisePos = transform.position;
        WorldNoiseAlert.Notify(noisePos, npcSuspicionOnStrike);

        // Prefer vise strikeSound; fall back to hammer useSuccessSound so the hit plays with the animation.
        AudioClip hitSfx = strikeSound != null ? strikeSound : hammer.useSuccessSound;
        if (hitSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(hitSfx, noisePos, strikeSfxVolume);
        InteractionUIManager.Instance?.PlaySuccessShake();

        if (animator != null)
        {
            if (!animator.enabled)
                animator.enabled = true;
            animator.SetTrigger(strikeAnimatorTrigger);
            yield return new WaitForSeconds(dialStrikeDuration);
        }
        else if (dialTransform != null)
        {
            Quaternion start = dialTransform.localRotation;
            Quaternion end = start * Quaternion.Euler(0f, 0f, dialStrikeDegrees);
            float t = 0f;
            while (t < dialStrikeDuration)
            {
                t += Time.deltaTime;
                float p = Mathf.SmoothStep(0f, 1f, t / dialStrikeDuration);
                dialTransform.localRotation = Quaternion.Slerp(start, end, p);
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(dialStrikeDuration);
        }

        _wrenchReleased = true;
        _isAnimating = false;
        ClearInteractableLayers();

        if (hammer.consumeOnUse)
            InventoryManager.Instance?.RemoveItem(hammer);
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
