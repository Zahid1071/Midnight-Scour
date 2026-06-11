using System;
using UnityEngine;

/// <summary>
/// LMB + held item on this object. Accepts one inventory item and spawns a replacement pickup.
/// </summary>
public class ItemCombineReceiver : MonoBehaviour, IItemReceiver
{
    public static event Action OnCombineSuccess;

    [SerializeField] private InventoryItem acceptedItem;
    [SerializeField] private GameObject replacementPrefab;
    [SerializeField] private string wrongItemMessage = "That won't work here.";
    [SerializeField] private string successMessage = "Stuck the gum on the bone.";
    [Tooltip("Optional. If unset, uses the accepted item's Use Success Sound.")]
    [SerializeField] private AudioClip successSound;

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

        AudioClip sfx = successSound != null ? successSound : item.useSuccessSound;
        InteractionFeedback.Success(successMessage, sfx, position);

        OnCombineSuccess?.Invoke();

        Destroy(gameObject);
        return true;
    }

    static void ShowMessage(string message) => InteractionFeedback.Fail(message, 2.5f);
}
