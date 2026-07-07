using UnityEngine;
using System.Collections.Generic;

public class StorageObject : MonoBehaviour, IInteractable
{
    private List<InventoryItem> storedItems = new List<InventoryItem>();

    public bool Interact()
    {
        var inventory = InventoryManager.Instance.GetInventory();
        if (inventory.Count == 0)
        {
            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.nothing_to_store"), 3f);
            return false;
        }

        foreach (var item in inventory)
            storedItems.Add(item);

        string names = string.Join(", ", storedItems.ConvertAll(i => i.GetLocalizedName()));
        HudManager.Instance?.ShowSubtitle(
            Loc.Format("gameplay.stored", names), 4f, HudManager.Instance.SuccessColor);

        InventoryManager.Instance.ClearInventory();
        Object.FindFirstObjectByType<InventoryUI>()?.RefreshUI();
        return true;
    }

    public List<InventoryItem> GetStoredItems() => storedItems;
}
