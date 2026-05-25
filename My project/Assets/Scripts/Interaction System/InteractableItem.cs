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
            if (!string.IsNullOrWhiteSpace(itemData.pickUpText))
                HudManager.Instance?.ShowSubtitle(itemData.pickUpText, 3f);
            else
                HudManager.Instance?.ShowGameplaySubtitle(
                    LocalizationTables.GameplayKeys.PickedUpItem, 3f,
                    ("item", itemData.itemName));
            SoundManager.Instance.PlaySFXAt(itemData.pickUpSFX, transform.position);

            if (InventoryUI.Instance != null)
            {
                int idx = InventoryManager.Instance.GetInventory().Count - 1;
                if (idx >= 0)
                    InventoryUI.Instance.SelectSlot(idx);
            }

            Destroy(gameObject);
        }
        else
        {
            HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.InventoryFull, 3f);
        }
    }
}
