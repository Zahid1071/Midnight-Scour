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
            HudManager.Instance?.ShowSubtitle("This slot is not configured.", 3f);
            return;
        }

        if (isInstalled)
        {
            HudManager.Instance?.ShowSubtitle($"{requiredItem.itemName} is already installed.", 3f);
            PlaySound(installFailSFX,transform);
            return;
        }

        InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();
        if (selectedItem == null)
        {
            HudManager.Instance?.ShowSubtitle("Select a car part from your inventory first.", 3f);
            PlaySound(installFailSFX,transform);
            return;
        }

        if (selectedItem != requiredItem)
        {
            HudManager.Instance?.ShowSubtitle(
                $"{selectedItem.itemName} does not fit here. You need {requiredItem.itemName}.", 4f);
            PlaySound(installFailSFX,transform);
            return;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (!inventory.Contains(selectedItem))
        {
            HudManager.Instance?.ShowSubtitle($"You do not have {selectedItem.itemName}.", 3f);
            PlaySound(installFailSFX,transform);
            return;
        }

        // ✅ Success
        isInstalled = true;
        InventoryManager.Instance.RemoveItem(selectedItem);
        InventoryUI.Instance?.RefreshUI();
        HudManager.Instance?.ShowSubtitle($"{requiredItem.itemName} installed.", 3f);
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
