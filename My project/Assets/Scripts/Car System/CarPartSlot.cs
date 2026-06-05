using UnityEngine;

public class CarPartSlot : MonoBehaviour, IInteractable
{
    public InventoryItem requiredItem;
    public AudioClip installSuccessSFX;
    public AudioClip installFailSFX;


    private bool isInstalled = false;

    public bool Interact()
    {
        if (requiredItem == null)
        {
            HudManager.Instance?.ShowSubtitle("This slot is not configured.", 3f);
            return false;
        }

        if (isInstalled)
        {
            HudManager.Instance?.ShowSubtitle($"{requiredItem.itemName} is already installed.", 3f);
            PlaySound(installFailSFX,transform);
            return false;
        }

        InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();
        if (selectedItem == null)
        {
            HudManager.Instance?.ShowSubtitle("Select a car part from your inventory first.", 3f);
            PlaySound(installFailSFX,transform);
            return false;
        }

        if (selectedItem != requiredItem)
        {
            HudManager.Instance?.ShowSubtitle(
                $"{selectedItem.itemName} does not fit here. You need {requiredItem.itemName}.", 4f);
            PlaySound(installFailSFX,transform);
            return false;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (!inventory.Contains(selectedItem))
        {
            HudManager.Instance?.ShowSubtitle($"You do not have {selectedItem.itemName}.", 3f);
            PlaySound(installFailSFX,transform);
            return false;
        }

        isInstalled = true;
        InventoryManager.Instance.RemoveItem(selectedItem);
        InventoryUI.Instance?.RefreshUI();
        HudManager.Instance?.ShowSubtitle($"{requiredItem.itemName} installed.", 3f, HudManager.Instance.SuccessColor);
        PlaySound(installSuccessSFX,transform);

        gameObject.SetActive(false);
        return true;
    }

    private void PlaySound(AudioClip clip,Transform point)
    {
        if (clip != null&& !SoundManager.Instance.IsClipPlayingAt(clip,point.position))
            SoundManager.Instance.PlaySFXAt(clip, transform.position,1);
    }

    public bool IsInstalled() => isInstalled;
    public string GetRequiredItemName() => requiredItem != null ? requiredItem.itemName : "?";
}
