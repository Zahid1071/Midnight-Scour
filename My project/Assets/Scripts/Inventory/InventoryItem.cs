using UnityEngine;

[CreateAssetMenu(fileName = "NewInventoryItem", menuName = "Inventory/Item")]
public class InventoryItem : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    [Header("World / drop")]
    [Tooltip("Prefab spawned when the player drops this item (InteractableItem + collider).")]
    public GameObject worldPrefab;

    [Tooltip("Played once when this dropped item first lands (collision-based). If null, falls back to SoundManager's default.")]
    public AudioClip dropLandSFX;

    [Tooltip("If false, the player cannot drop this item with G.")]
    public bool canDrop = true;

    public AudioClip pickUpSFX;
    [Tooltip("Played when this item is successfully used on a target (combine, unlock, etc.). Assign per item.")]
    public AudioClip useSuccessSound;

    public bool consumeOnUse = true;

    [Header("First-person view")]
    [Tooltip("Arms + item prefab (root has HeldItemView) shown in front of the camera while this item is selected. Empty = nothing is held.")]
    public GameObject heldViewPrefab;

    [Header("Stacking")]
    [Tooltip("How many of this item can share one inventory slot. 1 = no stacking.")]
    [Min(1)]
    public int maxStack = 1;

    [Header("Interaction")]
    [Tooltip("Added to the player's interact range while this item is selected (e.g. bone to reach through a grate).")]
    public float interactionReachBonus;
    [TextArea] public string reachAttemptFailMessage;

    [Header("Fragile tool (optional)")]
    [Tooltip("If true, using this item on the wrong locked door (SimpleOpenClose) removes it and plays break sound.")]
    public bool breaksOnWrongUse;
    public AudioClip breakOnWrongUseSound;
    [TextArea] public string breakOnWrongUseMessage = "The screwdriver snapped.";

    [Header("Localization")]
    [SerializeField] string nameKey;
    [SerializeField] string pickupKey;
    [SerializeField] string breakMessageKey;

    [TextArea] public string pickUpText;
    [TextArea] public string description;

    public string GetLocalizedName() =>
        string.IsNullOrWhiteSpace(nameKey) ? itemName : Loc.Get(nameKey);

    public string GetLocalizedPickupText() =>
        string.IsNullOrWhiteSpace(pickupKey) ? pickUpText : Loc.Get(pickupKey);

    public GameObject GetDropPrefab() => worldPrefab;

    public string GetLocalizedBreakMessage() =>
        string.IsNullOrWhiteSpace(breakMessageKey) ? breakOnWrongUseMessage : Loc.Get(breakMessageKey);

    /// <summary>Used when this item is offered to a locked door that expects a different key.</summary>
    public bool TryBreakOnWrongUse(Vector3 soundPosition)
    {
        if (!breaksOnWrongUse)
            return false;

        var manager = InventoryManager.Instance;
        if (manager != null && manager.GetInventory().Contains(this))
            manager.RemoveItem(this);

        if (breakOnWrongUseSound != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(breakOnWrongUseSound, soundPosition);

        if (!string.IsNullOrWhiteSpace(GetLocalizedBreakMessage()) && HudManager.Instance != null)
            HudManager.Instance.ShowSubtitle(GetLocalizedBreakMessage(), 3f, HudManager.Instance.ErrorColor);
        return true;
    }
}