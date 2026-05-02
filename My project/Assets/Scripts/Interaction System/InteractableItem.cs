using UnityEngine;

public class InteractableItem : MonoBehaviour, IInteractable
{
    public InventoryItem itemData;

    public void Interact()
    {
        if (itemData == null)
        {
            Debug.LogWarning("No item assigned.");
            return;
        }

        bool added = InventoryManager.Instance.AddItem(itemData);
        if (added)
        {
            string msg = !string.IsNullOrWhiteSpace(itemData.pickUpText)
                ? itemData.pickUpText
                : $"Picked up {itemData.itemName}.";
            HudManager.Instance?.ShowSubtitle(msg, 3f);
            SoundManager.Instance.PlaySFXAt(itemData.pickUpSFX, transform.position);
            Destroy(gameObject);
        }
        else
        {
            HudManager.Instance?.ShowSubtitle("Your inventory is full.", 3f);
        }
    }
}
