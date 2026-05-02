using UnityEngine;

public class ConditionalInteractible : MonoBehaviour, IInteractable
{
    [SerializeField] private InventoryItem requiredItem;
    [SerializeField] private MonoBehaviour targetScript; // The actual logic (e.g., door open script)

    public void Interact()
    {
        if (requiredItem == null)
        {
            HudManager.Instance?.ShowSubtitle("This interaction is not configured.", 3f);
            return;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (inventory.Contains(requiredItem))
        {
            HudManager.Instance?.ShowSubtitle($"{requiredItem.itemName} was used.", 2.5f);
            (targetScript as IInteractable)?.Interact();
        }
        else
        {
            HudManager.Instance?.ShowSubtitle($"You need {requiredItem.itemName}.", 3f);
        }
    }
}
