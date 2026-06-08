using UnityEngine;

/// <summary>
/// Wire Timeline / UnityEvent signal receivers to these methods so cinematics
/// acquire <see cref="PlayerControlGate.ReasonCinematic"/> while playing.
/// </summary>
public class CinematicPlayerControlLock : MonoBehaviour
{
    public void LockCinematic()
    {
        PlayerControlGate.Acquire(PlayerControlGate.ReasonCinematic);
        GameplayHudVisibility.Instance?.HideGameplayHud();
    }

    public void UnlockCinematic()
    {
        PlayerControlGate.Release(PlayerControlGate.ReasonCinematic);
        GameplayHudVisibility.Instance?.ShowGameplayHud();
    }

    /// <summary>Timeline signal entry point — always-active relay to the UI prefab.</summary>
    public void ShowControlsHint()
    {
        var hint = FindFirstObjectByType<ControlsPanelHint>(FindObjectsInactive.Include);
        if (hint == null)
        {
            Debug.LogWarning("[CinematicPlayerControlLock] ControlsPanelHint not found in scene.");
            return;
        }

        hint.Show();
    }
}
