using System;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public static InventoryManager Instance { get; private set; }

    public static event Action OnInventoryChanged;

    [SerializeField] private int maxSlots = 4;

    class Slot
    {
        public InventoryItem item;
        public int count;
    }

    readonly List<Slot> slots = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Inventory must not survive scene loads — new game / restart starts empty.
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool AddItem(InventoryItem item)
    {
        if (item == null)
            return false;

        int maxStack = Mathf.Max(1, item.maxStack);
        if (maxStack > 1)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                Slot slot = slots[i];
                if (slot.item != item || slot.count >= maxStack)
                    continue;

                slot.count++;
                RefreshAfterInventoryChange();
                return true;
            }
        }

        if (slots.Count >= maxSlots)
        {
            Debug.Log("❌ Inventory full.");
            return false;
        }

        slots.Add(new Slot { item = item, count = 1 });
        RefreshAfterInventoryChange();
        return true;
    }

    /// <summary>Removes one from the first matching stack (or the whole slot if count hits 0).</summary>
    public void RemoveItem(InventoryItem item)
    {
        if (item == null)
            return;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].item != item)
                continue;

            RemoveOneAt(i);
            return;
        }
    }

    public bool RemoveItemAt(int index, bool refreshUI = true)
    {
        return RemoveOneAt(index, refreshUI);
    }

    public bool RemoveOneAt(int index, bool refreshUI = true)
    {
        if (index < 0 || index >= slots.Count)
            return false;

        Slot slot = slots[index];
        slot.count--;
        if (slot.count <= 0)
            slots.RemoveAt(index);

        if (refreshUI)
            InventoryUI.Instance?.RefreshUI();

        OnInventoryChanged?.Invoke();
        return true;
    }

    public int GetCountAt(int index)
    {
        if (index < 0 || index >= slots.Count)
            return 0;
        return slots[index].count;
    }

    public int CountOf(InventoryItem item)
    {
        if (item == null)
            return 0;

        int total = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].item == item)
                total += slots[i].count;
        }

        return total;
    }

    public bool HasItem(InventoryItem item) => CountOf(item) > 0;

    /// <summary>Spend one of this item from inventory. Returns false if none.</summary>
    public bool TryConsumeOne(InventoryItem item)
    {
        if (item == null || CountOf(item) <= 0)
            return false;

        RemoveItem(item);
        return true;
    }

    private void RefreshAfterInventoryChange()
    {
        InventoryUI.Instance?.RefreshUI();
        OnInventoryChanged?.Invoke();
    }

    public void ClearInventory()
    {
        slots.Clear();
        InventoryUI.Instance?.RefreshUI();
        OnInventoryChanged?.Invoke();
    }

    /// <summary>
    /// Wipe inventory before leaving gameplay (New Game, quit-to-menu, capture restart).
    /// </summary>
    public static void ResetForNewGame()
    {
        LockpickExtraLife.Clear();
        CaptureContinue.ClearSpawnCache();

        if (Instance == null)
            return;

        Instance.ClearInventory();
        var go = Instance.gameObject;
        Instance = null;
        Destroy(go);
    }

    /// <summary>Occupied slots only (one entry per stack). Compatible with existing Contains checks.</summary>
    public List<InventoryItem> GetInventory()
    {
        var list = new List<InventoryItem>(slots.Count);
        for (int i = 0; i < slots.Count; i++)
            list.Add(slots[i].item);
        return list;
    }

    public int SlotCount() => maxSlots;
}
