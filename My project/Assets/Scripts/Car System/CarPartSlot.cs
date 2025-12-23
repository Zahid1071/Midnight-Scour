using UnityEngine;

public class CarPartSlot : MonoBehaviour, IInteractable
{
    [Header("Item Settings")]
    public InventoryItem requiredItem;

    [Header("Audio")]
    public AudioClip installSuccessSFX;
    public AudioClip installFailSFX;

    private bool isInstalled = false;

    public void Interact()
    {
        // 1. Zaten takılı mı?
        if (isInstalled)
        {
            Debug.Log($"{requiredItem.itemName} already installed.");
            PlaySound(installFailSFX);
            return;
        }

        InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();

        // 2. Seçili item kontrolü
        if (selectedItem == null)
        {
            Debug.Log("❌ No item selected.");
            PlaySound(installFailSFX);
            return;
        }

        // 3. Doğru item kontrolü
        if (selectedItem != requiredItem)
        {
            Debug.Log($"❌ {selectedItem.itemName} is not the correct part. You need a {requiredItem.itemName}.");
            PlaySound(installFailSFX);
            return;
        }

        // 4. Envanterde var mı kontrolü
        var inventory = InventoryManager.Instance.GetInventory();
        if (!inventory.Contains(selectedItem))
        {
            Debug.Log($"❌ You don’t have a {selectedItem.itemName}.");
            PlaySound(installFailSFX);
            return;
        }

        // ✅ BAŞARILI KURULUM
        isInstalled = true;

        // STANDART: Item'ın ayarına göre sil veya silme
        if (selectedItem.consumeOnUse)
        {
            InventoryManager.Instance.RemoveItem(selectedItem);
            InventoryUI.Instance?.RefreshUI();
        }

        Debug.Log($"✅ Installed {requiredItem.itemName}.");
        PlaySound(installSuccessSFX);
        
        // Slotu kapat (görsel olarak parça yerine geçtiği için slot gizlenir)
        gameObject.SetActive(false);
    }

    private void PlaySound(AudioClip clip)
    {
        // SoundManager üzerindeki güvenli (anti-spam) metodu kullanıyoruz
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXSafe(clip, transform.position);
    }

    public bool IsInstalled() => isInstalled;
    public string GetRequiredItemName() => requiredItem != null ? requiredItem.itemName : "None";
}