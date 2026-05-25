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

    public bool consumeOnUse = true;

    [Header("Interaction")]
    [Tooltip("Added to the player's interact range while this item is selected (e.g. bone to reach through a grate).")]
    public float interactionReachBonus;

    [TextArea] public string pickUpText;
    [TextArea] public string description;

    public GameObject GetDropPrefab() => worldPrefab != null ? worldPrefab : heldPrefab;
}