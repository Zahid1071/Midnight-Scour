using UnityEngine;

/// <summary>
/// The bar wedged in the bench vise after insertion — E reports it won't budge until the hammer sequence completes.
/// </summary>
public class BenchViceBarInteract : MonoBehaviour, IInteractable
{
    [SerializeField] BenchVicePuzzle puzzle;

    void Awake()
    {
        if (puzzle == null)
            puzzle = GetComponentInParent<BenchVicePuzzle>();
    }

    public bool Interact()
    {
        if (puzzle != null && puzzle.IsWrenchReleased)
            return false;

        string message = puzzle != null
            ? puzzle.GetContextualHintMessage(InventoryUI.Instance?.GetSelectedItem())
            : Loc.Get("gameplay.wont_budge");

        if (string.IsNullOrWhiteSpace(message))
            message = Loc.Get("gameplay.wont_budge");

        InteractionFeedback.Fail(message, 3f);
        return false;
    }
}
