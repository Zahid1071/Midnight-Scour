using UnityEngine;

/// <summary>
/// The bar in the bench vise handle after insertion: E turns it, same as using the vise itself.
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
        if (puzzle != null)
            return puzzle.Interact();

        InteractionFeedback.Fail(Loc.Get("gameplay.wont_budge"), 3f);
        return false;
    }
}
