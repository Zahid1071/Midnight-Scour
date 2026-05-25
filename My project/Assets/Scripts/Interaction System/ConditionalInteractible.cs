using UnityEngine;

public class ConditionalInteractible : MonoBehaviour, IInteractable
{
    [SerializeField] private InventoryItem requiredItem;
    [SerializeField] private MonoBehaviour targetScript; // The actual logic (e.g., door open script)

    public void Interact()
    {
        if (requiredItem == null)
        {
            HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.InteractionNotConfigured, 3f);
            return;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (inventory.Contains(requiredItem))
        {
            HudManager.Instance?.ShowGameplaySubtitle(
                LocalizationTables.GameplayKeys.ItemWasUsed, 2.5f,
                ("item", requiredItem.itemName));
            (targetScript as IInteractable)?.Interact();
        }
        else
        {
            HudManager.Instance?.ShowGameplaySubtitle(
                LocalizationTables.GameplayKeys.NeedItem, 3f,
                ("item", requiredItem.itemName));
        }
    }
}
