using UnityEngine;

/// <summary>
/// World lockpick pickup that adds a stackable Lockpick to inventory.
/// Prefer <see cref="InteractableItem"/> on new prefabs; this remains for existing Lockpick instances.
/// </summary>
[DisallowMultipleComponent]
public class ExtraLifeWorldPickup : MonoBehaviour, IInteractable
{
    [SerializeField] InventoryItem lockpickItem;
    [SerializeField] AudioClip pickUpSfx;
    [SerializeField] string pickupSubtitleKey = "item.lockpick.pickup";
    [SerializeField] string pickupSubtitleFallback = "Picked up Lockpick";

    bool _consumed;

    public bool Interact()
    {
        if (_consumed)
            return false;

        if (lockpickItem == null || InventoryManager.Instance == null)
        {
            Debug.LogWarning($"[ExtraLifeWorldPickup] Missing lockpick item on {name}.", this);
            return false;
        }

        if (!InventoryManager.Instance.AddItem(lockpickItem))
        {
            InteractionFeedback.Fail(Loc.Get("gameplay.inventory_full"), 3f);
            return false;
        }

        _consumed = true;

        Vector3 pos = transform.position;
        var col = GetComponent<Collider>();
        if (col != null)
            pos = col.bounds.center;

        if (pickUpSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(pickUpSfx, pos);
        else if (lockpickItem.pickUpSFX != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(lockpickItem.pickUpSFX, pos);

        string msg = Loc.Get(pickupSubtitleKey);
        if (string.IsNullOrEmpty(msg) || msg == pickupSubtitleKey)
        {
            if (!string.IsNullOrWhiteSpace(lockpickItem.GetLocalizedPickupText()))
                msg = lockpickItem.GetLocalizedPickupText();
            else
                msg = pickupSubtitleFallback;
        }

        HudManager.Instance?.ShowSubtitle(msg);
        GameplayHintManager.Instance?.NotifyLockpickPickedUp();

        if (InventoryUI.Instance != null)
        {
            var inventory = InventoryManager.Instance.GetInventory();
            int idx = inventory.IndexOf(lockpickItem);
            if (idx >= 0)
                InventoryUI.Instance.SelectSlot(idx);
        }

        Destroy(gameObject);
        return true;
    }
}
