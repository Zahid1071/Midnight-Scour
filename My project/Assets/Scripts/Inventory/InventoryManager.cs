using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    [SerializeField] private int maxSlots = 4;
    private List<InventoryItem> inventory = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool AddItem(InventoryItem item)
    {
        if (inventory.Count >= maxSlots)
        {
            Debug.Log("❌ Inventory full.");
            return false;
        }

        inventory.Add(item);
        Debug.Log("👜 Added to inventory: " + item.itemName);

        InventoryUI.Instance?.RefreshUI(); // ✅ Updates slotImages with icons
        HeldItemDisplay.Instance?.RefreshHeldVisual();
        return true;
    }

    public void RemoveItem(InventoryItem item)
    {
        inventory.Remove(item);
        RefreshAfterInventoryChange();
    }

    public bool RemoveItemAt(int index, bool refreshUI = true)
    {
        if (index < 0 || index >= inventory.Count)
            return false;

        inventory.RemoveAt(index);
        if (refreshUI)
            RefreshAfterInventoryChange();
        return true;
    }

    private void RefreshAfterInventoryChange()
    {
        InventoryUI.Instance?.RefreshUI();
        HeldItemDisplay.Instance?.RefreshHeldVisual();
    }

    public void ClearInventory()
    {
        inventory.Clear();
        InventoryUI.Instance?.RefreshUI(); // ✅ Refresh cleared state
        HeldItemDisplay.Instance?.RefreshHeldVisual();
    }

    public List<InventoryItem> GetInventory() => inventory;
    public int SlotCount() => maxSlots;
}
