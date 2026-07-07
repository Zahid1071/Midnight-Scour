using UnityEngine;

public class InteractableItem : MonoBehaviour, IInteractable, IItemReceiver
{
    public InventoryItem itemData;
    [SerializeField] GrateSystem grateGate;
    [SerializeField] bool blockedUntilBenchViceReleased;
    [SerializeField] string benchViceStuckMessage = "It is stuck.";
    [SerializeField] string blockedMessage = "I can't reach that.";
    [SerializeField] DemoEndDirector demoEndOnPickup;

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

            string msg = !string.IsNullOrWhiteSpace(itemData.GetLocalizedPickupText())
                ? itemData.GetLocalizedPickupText()
                : Loc.Format("gameplay.item_used", itemData.GetLocalizedName());
            HudManager.Instance?.ShowSubtitle(msg, 3f);
            SoundManager.Instance.PlaySFXAt(itemData.pickUpSFX, transform.position);

            if (InventoryUI.Instance != null)
            {
                var inventory = InventoryManager.Instance.GetInventory();
                int idx = inventory.IndexOf(itemData);
                if (idx >= 0)
                    InventoryUI.Instance.SelectSlot(idx);
            }

            demoEndOnPickup?.NotifyItemPickedUp(itemData);
            if (demoEndOnPickup == null)
                FindFirstObjectByType<DemoEndDirector>()?.NotifyItemPickedUp(itemData);

            Destroy(gameObject);
            return true;
        }

        InteractionFeedback.Fail(Loc.Get("gameplay.inventory_full"), 3f);
        return false;
    }

    Vector3 GetPickupPoint()
    {
        var col = GetComponent<Collider>();
        return col != null ? col.bounds.center : transform.position;
    }
}
