using UnityEngine;

[CreateAssetMenu(fileName = "NewInventoryItem", menuName = "Inventory/Item")]
public class InventoryItem : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    [Header("First-person held view")]
    [Tooltip("Optional mesh shown in the player's hand when this stack is selected. Colliders are disabled at runtime.")]
    public GameObject heldPrefab;

    public Vector3 heldLocalPosition;
    public Vector3 heldLocalEulerAngles;
    public Vector3 heldLocalScale = Vector3.one;

    public AudioClip pickUpSFX;

    public bool consumeOnUse = true;

    [TextArea] public string pickUpText;
    [TextArea] public string description;
}