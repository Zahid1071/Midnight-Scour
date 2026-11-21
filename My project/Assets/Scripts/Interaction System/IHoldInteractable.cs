/// <summary>
/// World target that needs a held press (E / LMB) instead of a tap.
/// InventoryUI drives the timer and circular progress UI.
/// </summary>
public interface IHoldInteractable
{
    float HoldDurationSeconds { get; }

    /// <summary>True when the selected item can begin a hold on this target.</summary>
    bool CanBeginHoldInteract(InventoryItem selectedItem);

    /// <summary>Called when the hold fills completely. Returns true on success.</summary>
    bool CompleteHoldInteract(InventoryItem selectedItem);

    /// <summary>Called when the hold is cancelled early (release, look away, swap item).</summary>
    void CancelHoldInteract();
}
