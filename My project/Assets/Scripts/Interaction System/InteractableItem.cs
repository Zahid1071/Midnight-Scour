using UnityEngine;
using UnityEngine.Events;

public class InteractableItem : MonoBehaviour, IInteractable, IItemReceiver, IHoldInteractable
{
    public InventoryItem itemData;
    [SerializeField] GrateSystem grateGate;
    [SerializeField] bool blockedUntilBenchViceReleased;
    [SerializeField] string benchViceStuckMessage = "It is stuck.";
    [SerializeField] string blockedMessage = "It is too far.";
    [Tooltip("If set, pickup waits until that corpse scare finishes (bar visible early, grab later).")]
    [SerializeField] CorpseVanishEvent blockedUntilCorpseScareDone;
    [SerializeField] string corpseScareBlockedKey = "gameplay.stuck";

    [Header("Hold to pick up (optional)")]
    [Tooltip("If enabled, E/LMB must be held (circular progress) instead of a tap.")]
    [SerializeField] bool requireHoldToPickup;
    [SerializeField, Min(0.15f)] float pickupHoldSeconds = 1.25f;
    [Tooltip("Fired after a successful pickup, before this object is destroyed.")]
    [SerializeField] UnityEvent onPickedUp;

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
    public float HoldDurationSeconds => Mathf.Max(0.15f, pickupHoldSeconds);

    public bool CanBeginHoldInteract(InventoryItem selectedItem)
    {
        if (!requireHoldToPickup || _consumed || itemData == null)
            return false;
        return !IsPickupBlocked(warn: false);
    }

    public bool CompleteHoldInteract(InventoryItem selectedItem)
    {
        if (!requireHoldToPickup)
            return false;
        if (TryShowBlockedMessage())
            return false;
        return TryPickup();
    }

    public void CancelHoldInteract()
    {
        // Shared hold UI owns transient state.
    }

    public bool Interact()
    {
        if (TryShowBlockedMessage())
            return false;

        // Hold-to-pickup items are owned by InventoryUI's hold path.
        if (requireHoldToPickup)
            return false;

        return TryPickup();
    }

    public bool ReceiveItem(InventoryItem item)
    {
        if (TryShowBlockedMessage())
            return false;

        if (requireHoldToPickup)
            return false;

        // Wrong item on a ready pickup still picks up (same as empty-hand E).
        return TryPickup();
    }

    bool TryShowBlockedMessage() => IsPickupBlocked(warn: true);

    bool IsPickupBlocked(bool warn)
    {
        if (_consumed)
            return true;

        if (grateGate != null && !grateGate.IsRemoved)
        {
            if (warn)
                InteractionFeedback.Fail(Loc.Get("gameplay.cant_reach"), 3f);
            return true;
        }

        if (blockedUntilBenchViceReleased)
        {
            var vice = BenchVicePuzzle.Instance;
            if (vice != null && !vice.IsWrenchReleased)
            {
                if (warn)
                    InteractionFeedback.Fail(Loc.Get("gameplay.stuck"), 3f);
                return true;
            }
        }

        if (blockedUntilCorpseScareDone != null && !blockedUntilCorpseScareDone.BarPickupAllowed)
        {
            if (warn)
            {
                string key = string.IsNullOrWhiteSpace(corpseScareBlockedKey) ? "gameplay.stuck" : corpseScareBlockedKey;
                InteractionFeedback.Fail(Loc.Get(key), 3f);
            }
            return true;
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
            // Flat UI-style pickup — keep it quiet (clips read hot at 1 on the flat bus).
            SoundManager.Instance.PlaySFX(itemData.pickUpSFX, 0.28f);

            if (!suppressPickupHint)
                GameplayHintManager.Instance?.NotifyItemPickedUp(itemData);

            onPickedUp?.Invoke();

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
