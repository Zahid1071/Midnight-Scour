using UnityEngine;

/// <summary>
/// LMB + held item on this object. Accepts one inventory item and spawns a replacement pickup.
/// </summary>
public class ItemCombineReceiver : MonoBehaviour, IItemReceiver
{
    [SerializeField] private InventoryItem acceptedItem;
    [SerializeField] private GameObject replacementPrefab;
    [SerializeField] private string wrongItemMessage = "That won't work here.";
    [SerializeField] private string successMessage = "Stuck the gum on the bone.";

    public bool ReceiveItem(InventoryItem item)
    {
        if (acceptedItem == null || replacementPrefab == null)
            return false;

        if (item == null || item != acceptedItem)
        {
            ShowMessage(wrongItemMessage);
            return false;
        }

        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(item))
        {
            ShowMessage("You do not have that item.");
            return false;
        }

        if (item.consumeOnUse)
            InventoryManager.Instance.RemoveItem(item);

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        Instantiate(replacementPrefab, position, rotation);

        ShowSuccess(successMessage);

        Destroy(gameObject);
        return true;
    }

    static void ShowSuccess(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || HudManager.Instance == null)
            return;
        HudManager.Instance.ShowSubtitle(message, 3f, HudManager.Instance.SuccessColor);
    }

    static void ShowMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        HudManager.Instance?.ShowSubtitle(message, 2.5f, HudManager.Instance.WarningColor);
    }
}
