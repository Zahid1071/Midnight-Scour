using UnityEngine;

[CreateAssetMenu(fileName = "NewInventoryItem", menuName = "Inventory/Item")]
public class InventoryItem : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    [Header("World / drop")]
    [Tooltip("Prefab spawned when the player drops this item (InteractableItem + collider). Falls back to heldPrefab if unset.")]
    public GameObject worldPrefab;

    [Tooltip("If false, the player cannot drop this item with G.")]
    public bool canDrop = true;

    [Header("First-person held view")]
    [Tooltip("Optional mesh shown in the player's hand when this stack is selected. Colliders are disabled at runtime.")]
    public GameObject heldPrefab;

    public Vector3 heldLocalPosition;
    public Vector3 heldLocalEulerAngles;
    public Vector3 heldLocalScale = Vector3.one;

    public AudioClip pickUpSFX;
    [Tooltip("Played when this item is successfully used on a target (combine, unlock, etc.). Assign per item.")]
    public AudioClip useSuccessSound;

    public bool consumeOnUse = true;

    [Header("Interaction")]
    [Tooltip("Added to the player's interact range while this item is selected (e.g. bone to reach through a grate).")]
    public float interactionReachBonus;

    [Header("Fragile tool (optional)")]
    [Tooltip("If true, using this item on the wrong locked door (SimpleOpenClose) removes it and plays break sound.")]
    public bool breaksOnWrongUse;
    public AudioClip breakOnWrongUseSound;
    [TextArea] public string breakOnWrongUseMessage = "The screwdriver snapped.";

    [TextArea] public string pickUpText;
    [TextArea] public string description;

    public GameObject GetDropPrefab() => worldPrefab != null ? worldPrefab : heldPrefab;

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

        if (!string.IsNullOrWhiteSpace(breakOnWrongUseMessage) && HudManager.Instance != null)
            HudManager.Instance.ShowSubtitle(breakOnWrongUseMessage, 3f, HudManager.Instance.ErrorColor);

        HeldItemDisplay.Instance?.RefreshHeldVisual();
        return true;
    }
}