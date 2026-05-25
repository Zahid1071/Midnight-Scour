using UnityEngine;

public class CarPartSlot : MonoBehaviour, IInteractable
{
    public InventoryItem requiredItem;
    public AudioClip installSuccessSFX;
    public AudioClip installFailSFX;


    private bool isInstalled = false;

    public void Interact()
    {
        if (requiredItem == null)
        {
            HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.SlotNotConfigured, 3f);
            return;
        }

        if (isInstalled)
        {
            HudManager.Instance?.ShowGameplaySubtitle(
                LocalizationTables.GameplayKeys.PartAlreadyInstalled, 3f,
                ("item", requiredItem.itemName));
            PlaySound(installFailSFX,transform);
            return;
        }

        InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();
        if (selectedItem == null)
        {
            HudManager.Instance?.ShowGameplaySubtitle(LocalizationTables.GameplayKeys.SelectCarPartFirst, 3f);
            PlaySound(installFailSFX,transform);
            return;
        }

        if (selectedItem != requiredItem)
        {
            HudManager.Instance?.ShowGameplaySubtitle(
                LocalizationTables.GameplayKeys.PartDoesNotFit, 4f,
                ("selected", selectedItem.itemName),
                ("required", requiredItem.itemName));
            PlaySound(installFailSFX,transform);
            return;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (!inventory.Contains(selectedItem))
        {
            HudManager.Instance?.ShowGameplaySubtitle(
                LocalizationTables.GameplayKeys.DoNotHaveItem, 3f,
                ("item", selectedItem.itemName));
            PlaySound(installFailSFX,transform);
            return;
        }

        // ✅ Success
        isInstalled = true;
        InventoryManager.Instance.RemoveItem(selectedItem);
        InventoryUI.Instance?.RefreshUI();
        HudManager.Instance?.ShowGameplaySubtitle(
            LocalizationTables.GameplayKeys.PartInstalled, 3f,
            ("item", requiredItem.itemName));
        PlaySound(installSuccessSFX,transform);
        
        gameObject.SetActive(false);
    }

    private void PlaySound(AudioClip clip,Transform point)
    {
        if (clip != null&& !SoundManager.Instance.IsClipPlayingAt(clip,point.position))
            SoundManager.Instance.PlaySFXAt(clip, transform.position,1);
    }

    public bool IsInstalled() => isInstalled;
    public string GetRequiredItemName() => requiredItem != null ? requiredItem.itemName : "?";
}
