using UnityEngine;

public class InteractableItem : MonoBehaviour, IInteractable, IItemReceiver
{
    public InventoryItem itemData;
    [SerializeField] GrateSystem grateGate;
    [SerializeField] bool blockedUntilBenchViceReleased;
    [SerializeField] string benchViceStuckMessage = "It is stuck.";
    [SerializeField] string blockedMessage = "It is too far.";

    [Header("Pickup feedback (optional)")]
    [Tooltip("Loc key shown instead of the item's default pickup text.")]
    [SerializeField] string pickupSubtitleKey;
    [Tooltip("Used when the key above has no translation yet.")]
    [SerializeField, TextArea] string pickupSubtitleFallback;
    [SerializeField] float pickupSubtitleSeconds = 4f;
    [Tooltip("Skip the item's pickup hint panel (e.g. the lockpick extra-life hint) for this instance.")]
    [SerializeField] bool suppressPickupHint;

    bool _consumed;

    public bool IsBlockedByGrate => grateGate != null && !grateGate.IsRemoved;

    public bool Interact()
    {
        if (TryShowBlockedMessage())
            return false;

        return TryPickup();
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (TryShowBlockedMessage())
            return false;

        // Wrong item on a ready pickup still picks up (same as empty-hand E).
        return TryPickup();
    }

    bool TryShowBlockedMessage()
    {
        if (_consumed)
            return true;

        if (grateGate != null && !grateGate.IsRemoved)
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.cant_reach"), 3f);
            return true;
        }

        if (blockedUntilBenchViceReleased)
        {
            var vice = BenchVicePuzzle.Instance;
            if (vice != null && !vice.IsWrenchReleased)
            {
                InteractionFeedback.Fail(Loc.Get("gameplay.stuck"), 3f);
                return true;
            }
        }

        return false;
    }

    bool TryPickup()
    {
        if (_consumed)
            return false;

        if (itemData == null)
        {
            Debug.LogWarning("No item assigned.");
            return false;
        }

        InventoryItem heldTool = InventoryUI.Instance?.GetSelectedItem();

        bool added = InventoryManager.Instance.AddItem(itemData);
        if (added)
        {
            _consumed = true;

            var interactor = PlayerInteractor.Instance;
            if (interactor != null
                && interactor.ShouldConsumeReachToolOnPickup(heldTool, GetPickupPoint()))
            {
                InventoryManager.Instance.RemoveItem(heldTool);
            }

            // Empty hands equip the pickup; otherwise it goes to the bag and the held item stays out.
            if (InventoryUI.Instance != null && heldTool == null)
            {
                int index = InventoryManager.Instance.GetInventory().IndexOf(itemData);
                if (index >= 0)
                    InventoryUI.Instance.SelectSlot(index);
            }

            if (TryGetComponent(out WorldLighter worldLighter))
                worldLighter.NotifyPickedUp(itemData);

            string custom = ResolvePickupSubtitleOverride();
            if (custom != null)
            {
                HudManager.Instance?.ShowSubtitle(custom, pickupSubtitleSeconds);
            }
            else
            {
                string msg = !string.IsNullOrWhiteSpace(itemData.GetLocalizedPickupText())
                    ? itemData.GetLocalizedPickupText()
                    : Loc.Format("gameplay.item_used", itemData.GetLocalizedName());
                HudManager.Instance?.ShowSubtitle(msg);
            }
            SoundManager.Instance.PlaySFXAt(itemData.pickUpSFX, transform.position);

            if (!suppressPickupHint)
                GameplayHintManager.Instance?.NotifyItemPickedUp(itemData);

            Destroy(gameObject);
            return true;
        }

        InteractionFeedback.Fail(Loc.Get("gameplay.inventory_full"), 3f);
        return false;
    }

    string ResolvePickupSubtitleOverride()
    {
        if (!string.IsNullOrWhiteSpace(pickupSubtitleKey))
        {
            string s = Loc.Get(pickupSubtitleKey);
            if (!string.IsNullOrEmpty(s)
                && s != pickupSubtitleKey
                && s.IndexOf("No translation", System.StringComparison.OrdinalIgnoreCase) < 0)
                return s;
        }

        return string.IsNullOrWhiteSpace(pickupSubtitleFallback) ? null : pickupSubtitleFallback;
    }

    Vector3 GetPickupPoint()
    {
        var col = GetComponent<Collider>();
        return col != null ? col.bounds.center : transform.position;
    }
}
