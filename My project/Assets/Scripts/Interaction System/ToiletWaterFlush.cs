using UnityEngine;

/// <summary>
/// Toilet water target: use Plunger (LMB or E while selected) to play the drain animation.
/// </summary>
public class ToiletWaterFlush : MonoBehaviour, IInteractable, IItemReceiver
{
    static readonly int FlushTrigger = Animator.StringToHash("Flush");

    [SerializeField] private InventoryItem plungerItem;
    [SerializeField] private Animator animator;
    [SerializeField] private string needPlungerMessage = "I need a plunger.";
    [SerializeField] private string wrongItemMessage = "That won't work here.";
    [SerializeField] private string alreadyFlushedMessage = "The water's already gone.";
    [SerializeField] private string successMessage = "";
    [SerializeField] AudioClip flush;
    [Header("NPC Suspicion")]
    [Tooltip("Footstep-suspicion added to NPCs when the toilet flushes.")]
    [Min(0f)]
    [SerializeField] float npcSuspicionOnFlush = 300f;
    [SerializeField] float flushSfxVolume = 1f;

    bool _hasFlushed;

    void Awake()
    {
        if (animator == null)
            TryGetComponent(out animator);
    }

    public void Interact()
    {
        InventoryItem selected = InventoryUI.Instance?.GetSelectedItem();
        if (selected == null)
        {
            InteractionFeedback.Fail(needPlungerMessage, 2.5f);
            return;
        }

        if (selected != plungerItem)
        {
            InteractionFeedback.Fail(wrongItemMessage, 2.5f);
            return;
        }

        TryFlush(selected, playFeedback: true);
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (_hasFlushed)
        {
            InteractionFeedback.Fail(alreadyFlushedMessage, 2.5f);
            return false;
        }

        if (item == null || item != plungerItem)
        {
            InteractionFeedback.Fail(wrongItemMessage, 2.5f);
            return false;
        }

        // InventoryUI plays SFX / crosshair shake after a successful ReceiveItem.
        return TryFlush(item, playFeedback: false);
    }

    bool TryFlush(InventoryItem item, bool playFeedback)
    {
        if (_hasFlushed)
        {
            InteractionFeedback.Fail(alreadyFlushedMessage, 2.5f);
            return false;
        }

        if (animator == null)
        {
            Debug.LogWarning($"{nameof(ToiletWaterFlush)} on {name}: no Animator assigned.");
            return false;
        }

        if (!animator.enabled)
            animator.enabled = true;

        animator.SetTrigger(FlushTrigger);

        Vector3 noisePos = transform.position;
        if (flush != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(flush, noisePos, flushSfxVolume);
        WorldNoiseAlert.Notify(noisePos, npcSuspicionOnFlush);

        _hasFlushed = true;

        if (item.consumeOnUse)
            InventoryManager.Instance?.RemoveItem(item);

        if (!string.IsNullOrWhiteSpace(successMessage))
            HudManager.Instance?.ShowSubtitle(successMessage, 3f);

        if (playFeedback)
        {
            AudioClip sfx = item.useSuccessSound;
            if (sfx != null)
                InteractionFeedback.PlaySound(sfx, transform.position);
            InteractionUIManager.Instance?.PlaySuccessShake();
        }

        return true;
    }
}
