using UnityEngine;

/// <summary>Gives the player these items when gameplay starts (skips ones already held).</summary>
[DisallowMultipleComponent]
public sealed class PlayerStartingItems : MonoBehaviour
{
    [SerializeField] private InventoryItem[] items;

    [Tooltip("Select the first starting item so it is drawn right away.")]
    [SerializeField] private bool selectFirstItem = true;

    private void Start()
    {
        var inventory = InventoryManager.Instance;
        if (inventory == null || items == null)
            return;

        foreach (var item in items)
        {
            if (item != null && !inventory.HasItem(item))
                inventory.AddItem(item);
        }

        if (!selectFirstItem || items.Length == 0 || items[0] == null || InventoryUI.Instance == null)
            return;

        int index = inventory.GetInventory().IndexOf(items[0]);
        if (index >= 0)
            InventoryUI.Instance.SelectSlot(index);
    }
}
