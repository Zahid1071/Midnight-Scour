using UnityEngine;

public class CarPartSlot : MonoBehaviour, IInteractable, IHoldInteractable
{
    public InventoryItem requiredItem;
    public AudioClip installSuccessSFX;
    public AudioClip installFailSFX;

    [Header("Hold to install")]
    [Tooltip("How long the player must hold E / LMB with the correct part selected.")]
    [SerializeField, Min(0.15f)] float installHoldSeconds = 1.25f;

    private bool isInstalled = false;

    public float HoldDurationSeconds => installHoldSeconds;

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

    public bool CanBeginHoldInteract(InventoryItem selectedItem)
    {
        return !isInstalled && CanInstall(selectedItem, warn: false);
    }

    public bool CompleteHoldInteract(InventoryItem selectedItem)
    {
        if (isInstalled)
            return false;

        if (!CanInstall(selectedItem, warn: true))
            return false;

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

    public void CancelHoldInteract()
    {
        // No transient state beyond the shared hold UI.
    }

    /// <summary>
    /// Tap feedback only. Correct part must be held — does not install instantly.
    /// </summary>
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

        // Correct part selected — InventoryUI hold path owns the install.
        return false;
    }

    bool CanInstall(InventoryItem selectedItem, bool warn)
    {
        if (requiredItem == null)
        {
            if (warn)
                HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.slot_not_configured"), 3f);
            return false;
        }

        if (selectedItem == null)
        {
            if (warn)
            {
                HudManager.Instance?.ShowSubtitle(Loc.Get("gameplay.select_part_first"), 3f);
                PlaySound(installFailSFX, transform);
            }
            return false;
        }

        if (selectedItem != requiredItem)
        {
            if (warn)
            {
                HudManager.Instance?.ShowSubtitle(
                    Loc.Format("gameplay.need_item", requiredItem.GetLocalizedName()), 4f);
                PlaySound(installFailSFX, transform);
            }
            return false;
        }

        var inventory = InventoryManager.Instance?.GetInventory();
        if (inventory == null || !inventory.Contains(selectedItem))
        {
            if (warn)
            {
                HudManager.Instance?.ShowSubtitle(
                    Loc.Format("gameplay.dont_have_item", selectedItem.GetLocalizedName()), 3f);
                PlaySound(installFailSFX, transform);
            }
            return false;
        }

        return true;
    }

    private void PlaySound(AudioClip clip, Transform point)
    {
        if (clip != null && SoundManager.Instance != null
            && !SoundManager.Instance.IsClipPlayingAt(clip, point.position))
            SoundManager.Instance.PlaySFXAt(clip, transform.position, 1);
    }

    public bool IsInstalled() => isInstalled;
    public string GetRequiredItemName() =>
        requiredItem != null ? requiredItem.GetLocalizedName() : "?";
}
