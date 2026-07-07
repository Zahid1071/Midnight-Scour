using UnityEngine;

public class ConditionalInteractible : MonoBehaviour, IInteractable
{
    [SerializeField] private InventoryItem requiredItem;
    [SerializeField] private MonoBehaviour targetScript;

    public bool Interact()
    {
        if (requiredItem == null)
        {
            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.not_configured"), 3f);
            return false;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (inventory.Contains(requiredItem))
        {
            HudManager.Instance?.ShowSubtitle(
                Loc.Format("gameplay.item_used", requiredItem.GetLocalizedName()),
                2.5f, HudManager.Instance.SuccessColor);
            if (targetScript is IInteractable interactable)
                return interactable.Interact();
            return true;
        }

        HudManager.Instance?.ShowSubtitle(
            Loc.Format("gameplay.need_item", requiredItem.GetLocalizedName()), 3f);
        return false;
    }
}
