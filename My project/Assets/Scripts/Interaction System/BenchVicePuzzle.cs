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
    [Header("Messages")]
    [SerializeField] string wrongItemMessage = "That won't work here.";
    [SerializeField] string barWontBudgeMessage = "It won't budge.";
    [SerializeField] string needBarMessage = "A bar might loosen this.";
    [SerializeField] string useBarOnViseMessage = "Use the bar on the vise.";
    [SerializeField] string useHammerOnBarMessage = "Use the hammer on the bar.";
    [SerializeField] AudioClip strikeSound;
    [Header("NPC Suspicion")]
    [Tooltip("Footstep-suspicion added to NPCs when the hammer strikes (same mechanism as body bag impact).")]
    [Min(0f)]
    [SerializeField] float npcSuspicionOnStrike = 300f;
    [SerializeField] float strikeSfxVolume = 1f;

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

    public void Interact()
    {
        if (_wrenchReleased)
            return;

        if (!_barInserted)
        {
            InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
            if (selected == barItem || selected == hammerItem)
                InteractionFeedback.Fail(useBarOnViseMessage, 3f);
            else
                InteractionFeedback.Fail(needBarMessage, 3f);
            return;
        }

        InventoryItem held = InventoryUI.Instance?.GetSelectedItem();
        if (held == hammerItem)
        {
            InteractionFeedback.Fail(useHammerOnBarMessage, 3f);
            return;
        }

        InteractionFeedback.Fail(barWontBudgeMessage, 3f);
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

        InteractionFeedback.Fail(wrongItemMessage, 2.5f);
        return false;
    }

    bool TryInsertBar(InventoryItem item)
    {
        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            InteractionFeedback.Fail("You do not have that item.", 2.5f);
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
            InteractionFeedback.Fail("You do not have that item.", 2.5f);
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

        if (strikeSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(strikeSound, noisePos, strikeSfxVolume);

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

        if (hammer.consumeOnUse)
            InventoryManager.Instance?.RemoveItem(hammer);

        AudioClip sfx = hammer.useSuccessSound;
        if (sfx != null)
            InteractionFeedback.PlaySound(sfx, transform.position);
        InteractionUIManager.Instance?.PlaySuccessShake();
    }
}
