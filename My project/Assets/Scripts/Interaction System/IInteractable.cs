public interface IInteractable
{
    /// <returns>True if the interaction did something; false if blocked or ignored.</returns>
    bool Interact();
}

public interface IItemReceiver
{
    /// <returns>True if the item was accepted and used.</returns>
    bool ReceiveItem(InventoryItem item);
}

