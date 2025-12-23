using UnityEngine;

public class CarPartSlot : MonoBehaviour, IInteractable
{
    public InventoryItem requiredItem;
    public AudioClip installSuccessSFX;
    public AudioClip installFailSFX;


    private bool isInstalled = false;

    public void Interact()
    {
        if (isInstalled)
        {
            Debug.Log($"{requiredItem.itemName} already installed.");
            PlaySound(installFailSFX,transform);
            return;
        }

        InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();
        if (selectedItem == null)
        {
            Debug.Log("❌ No item selected.");
            PlaySound(installFailSFX,transform);
            return;
        }

        if (selectedItem != requiredItem)
        {
            Debug.Log($"❌ {selectedItem.itemName} is not the correct part. You need a {requiredItem.itemName}.");
            PlaySound(installFailSFX,transform);
            return;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (!inventory.Contains(selectedItem))
        {
            Debug.Log($"❌ You don’t have a {selectedItem.itemName}.");
            PlaySound(installFailSFX,transform);
            return;
        }

        // ✅ Success
        isInstalled = true;
        InventoryManager.Instance.RemoveItem(selectedItem);
        InventoryUI.Instance?.RefreshUI();
        Debug.Log($"✅ Installed {requiredItem.itemName}.");
        PlaySound(installSuccessSFX,transform);
        
        gameObject.SetActive(false);
    }

    private void PlaySound(AudioClip clip,Transform point)
    {
        if (clip != null&& !SoundManager.Instance.IsClipPlayingAt(clip,point.position))
            SoundManager.Instance.PlaySFXAt(clip, transform.position,1);
    }

    public bool IsInstalled() => isInstalled;
    public string GetRequiredItemName() => requiredItem.itemName;
}
