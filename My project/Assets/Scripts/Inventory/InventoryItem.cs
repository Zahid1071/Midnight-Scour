using UnityEngine;

[CreateAssetMenu(fileName = "NewInventoryItem", menuName = "Inventory/Item")]
public class InventoryItem : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    public AudioClip pickUpSFX;

    public bool consumeOnUse = true;

    [TextArea] public string pickUpText;
    [TextArea] public string description;
}