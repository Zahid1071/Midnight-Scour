using UnityEngine;

public class InteractableItem : MonoBehaviour, IInteractable, IItemReceiver
{
    public InventoryItem itemData;
    
    [Header("Tool-Assisted Pickup")]
    [Tooltip("Item that can be used as a tool to pick this up (e.g., stick to reach through cage). Leave empty if no tool needed.")]
    [SerializeField] private InventoryItem requiredTool;
    
    [Tooltip("If true, this item can only be picked up using the required tool.")]
    [SerializeField] private bool requiresTool = false;

    public void Interact()
    {
        // ✅ Check if tool is required
        if (requiresTool && requiredTool != null)
        {
            Debug.Log($"❌ You need a {requiredTool.itemName} to pick this up.");
            return;
        }
        
        if (itemData == null)
        {
            Debug.LogWarning("No item assigned.");
            return;
        }

        bool added = InventoryManager.Instance.AddItem(itemData);
        if (added)
        {
            SoundManager.Instance.PlaySFXAt(itemData.pickUpSFX, transform.position);
            Destroy(gameObject);
        }
    }
    
    // ✅ Called when player uses an item (tool) on this object (via InventoryUI - E key)
    public void ReceiveItem(InventoryItem item)
    {
        if (requiredTool == null)
        {
            Debug.Log("This item doesn't require a tool.");
            return;
        }
        
        if (item == null)
        {
            Debug.Log("❌ Invalid item.");
            return;
        }
        
        // ✅ Check if the used item is the required tool
        if (item != requiredTool)
        {
            Debug.Log($"❌ {item.itemName} can't be used to pick this up. You need a {requiredTool.itemName}.");
            return;
        }
        
        var inventory = InventoryManager.Instance.GetInventory();
        if (!inventory.Contains(item))
        {
            Debug.Log($"❌ You don't have {item.itemName}.");
            return;
        }
        
        // ✅ Tool is correct - pick up the item
        if (itemData == null)
        {
            Debug.LogWarning("No item assigned.");
            return;
        }

        bool added = InventoryManager.Instance.AddItem(itemData);
        if (added)
        {
            Debug.Log($"✅ Used {item.itemName} to pick up {itemData.itemName}!");
            SoundManager.Instance.PlaySFXAt(itemData.pickUpSFX, transform.position);
            
            // Optional: Remove the tool after use (uncomment if tool is single-use)
            // InventoryManager.Instance.RemoveItem(item);
            
            Destroy(gameObject);
        }
    }
}
