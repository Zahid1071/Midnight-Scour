using UnityEngine;
using System.Collections.Generic;

public class StorageObject : MonoBehaviour, IInteractable
{
    private List<InventoryItem> storedItems = new List<InventoryItem>();

    public void Interact()
    {
        var inventory = InventoryManager.Instance.GetInventory();
        if (inventory.Count == 0)
        {
            HudManager.Instance?.ShowSubtitle("You have nothing to store.", 3f);
            return;
        }

        foreach (var item in inventory)
        {
            storedItems.Add(item);
        }

        string names = string.Join(", ", storedItems.ConvertAll(i => i.itemName));
        HudManager.Instance?.ShowSubtitle($"Stored: {names}.", 4f);

        InventoryManager.Instance.ClearInventory();
        Object.FindFirstObjectByType<InventoryUI>()?.RefreshUI();

    }

    public List<InventoryItem> GetStoredItems() => storedItems;
}
