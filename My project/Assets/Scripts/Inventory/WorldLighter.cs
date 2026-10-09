using UnityEngine;

/// <summary>
/// Lighter lying in the world (dropped or placed). While lit its flame object (lights + flame particles)
/// stays on, so a placed lighter lights the area around it on its own.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorldLighter : MonoBehaviour
{
    [SerializeField] private GameObject flame;
    [SerializeField] private bool litOnStart;

    public bool IsLit { get; private set; }

    private void Awake() => SetLit(litOnStart);

    public void SetLit(bool lit)
    {
        IsLit = lit;
        if (flame != null)
            flame.SetActive(lit);
    }

    /// <summary>Called when picked up into the inventory. Unlike other pickups, a burning lighter keeps burning:
    /// it goes straight to the hand and is drawn already lit.</summary>
    public void NotifyPickedUp(InventoryItem item)
    {
        if (!IsLit || HeldItemController.Instance == null || InventoryUI.Instance == null || InventoryManager.Instance == null)
            return;
        if (!HeldItemController.Instance.TryGetView(item, out HeldItemView view) || !view.TryGetComponent(out HeldLighter lighter))
            return;

        lighter.DrawLitNextTime();
        int index = InventoryManager.Instance.GetInventory().IndexOf(item);
        if (index >= 0)
            InventoryUI.Instance.SelectSlot(index);
    }
}
