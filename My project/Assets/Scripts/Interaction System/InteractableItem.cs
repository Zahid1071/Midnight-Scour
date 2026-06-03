using UnityEngine;

public class InteractableItem : MonoBehaviour, IInteractable
{
    public InventoryItem itemData;
    [SerializeField] GrateSystem grateGate;
    [SerializeField] bool blockedUntilBenchViceReleased;
    [SerializeField] string benchViceStuckMessage = "It is stuck.";
    [SerializeField] string blockedMessage = "I can't reach that.";

    bool _consumed;

    public void Interact()
    {
        if (_consumed)
            return;

        if (grateGate != null && !grateGate.IsRemoved)
        {
            InteractionFeedback.Fail(blockedMessage, 3f);
            return;
        }

        if (blockedUntilBenchViceReleased)
        {
            var vice = BenchVicePuzzle.Instance;
            if (vice != null && !vice.IsWrenchReleased)
            {
                InteractionFeedback.Fail(benchViceStuckMessage, 3f);
                return;
            }
        }

        if (itemData == null)
        {
            Debug.LogWarning("No item assigned.");
            return;
        }

        bool added = InventoryManager.Instance.AddItem(itemData);
        if (added)
        {
            _consumed = true;
            string msg = !string.IsNullOrWhiteSpace(itemData.pickUpText)
                ? itemData.pickUpText
                : $"Picked up {itemData.itemName}.";
            HudManager.Instance?.ShowSubtitle(msg, 3f);
            SoundManager.Instance.PlaySFXAt(itemData.pickUpSFX, transform.position);

            if (InventoryUI.Instance != null)
            {
                int idx = InventoryManager.Instance.GetInventory().Count - 1;
                if (idx >= 0)
                    InventoryUI.Instance.SelectSlot(idx);
            }

            Destroy(gameObject);
        }
        else
        {
            InteractionFeedback.Fail("Your inventory is full.", 3f);
        }
    }
}
