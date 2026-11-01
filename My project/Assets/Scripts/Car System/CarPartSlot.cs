using UnityEngine;

public class CarPartSlot : MonoBehaviour, IInteractable
{
    public InventoryItem requiredItem;
    public AudioClip installSuccessSFX;
    public AudioClip installFailSFX;

    private bool isInstalled = false;

    void Start()
    {
        var escape = FindFirstObjectByType<CarEscapePoint>();
        if (escape == null || !escape.RepairDisabledForDemo)
            return;

        if (requiredItem != null)
        {
            foreach (var pickup in FindObjectsByType<InteractableItem>(FindObjectsSortMode.None))
                if (pickup.itemData == requiredItem)
                    pickup.gameObject.SetActive(false);
        }

        gameObject.SetActive(false);
    }

    public bool Interact()
    {
        if (requiredItem == null)
        {
            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.slot_not_configured"), 3f);
            return false;
        }

        if (isInstalled)
        {
            HudManager.Instance?.ShowSubtitle(
                Loc.Format("gameplay.already_installed", requiredItem.GetLocalizedName()), 3f);
            PlaySound(installFailSFX, transform);
            return false;
        }

        InventoryItem selectedItem = InventoryUI.Instance?.GetSelectedItem();
        if (selectedItem == null)
        {
            HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.select_part_first"), 3f);
            PlaySound(installFailSFX, transform);
            return false;
        }

        if (selectedItem != requiredItem)
        {
            HudManager.Instance?.ShowSubtitle(
                Loc.Format("gameplay.need_item", requiredItem.GetLocalizedName()), 4f);
            PlaySound(installFailSFX, transform);
            return false;
        }

        var inventory = InventoryManager.Instance.GetInventory();
        if (!inventory.Contains(selectedItem))
        {
            HudManager.Instance?.ShowSubtitle(
                Loc.Format("gameplay.dont_have_item", selectedItem.GetLocalizedName()), 3f);
            PlaySound(installFailSFX, transform);
            return false;
        }

        isInstalled = true;
        InventoryManager.Instance.RemoveItem(selectedItem);
        InventoryUI.Instance?.RefreshUI();
        HudManager.Instance?.ShowSubtitle(
            Loc.Format("gameplay.item_installed", requiredItem.GetLocalizedName()),
            3f, HudManager.Instance.SuccessColor);
        PlaySound(installSuccessSFX, transform);

        gameObject.SetActive(false);
        return true;
    }

    private void PlaySound(AudioClip clip, Transform point)
    {
        if (clip != null && !SoundManager.Instance.IsClipPlayingAt(clip, point.position))
            SoundManager.Instance.PlaySFXAt(clip, transform.position, 1);
    }

    public bool IsInstalled() => isInstalled;
    public string GetRequiredItemName() =>
        requiredItem != null ? requiredItem.GetLocalizedName() : "?";
}
