using UnityEngine;

/// <summary>
/// The bar wedged in the bench vise after insertion — E reports it won't budge until the hammer sequence completes.
/// </summary>
public class BenchViceBarInteract : MonoBehaviour, IInteractable
{
    [SerializeField] BenchVicePuzzle puzzle;
    [SerializeField] string wontBudgeMessage = "It won't budge.";

    void Awake()
    {
        if (puzzle == null)
            puzzle = GetComponentInParent<BenchVicePuzzle>();
    }

    public bool Interact()
    {
        if (puzzle != null && puzzle.IsWrenchReleased)
            return false;

        InteractionFeedback.Fail(wontBudgeMessage, 3f);
        return false;
    }
}
