public interface IInteractable
{
    void Interact();
}

public interface IItemReceiver
{
    /// <returns>True if the item was accepted and used.</returns>
    bool ReceiveItem(InventoryItem item);
}

