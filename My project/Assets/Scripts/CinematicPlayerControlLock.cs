using UnityEngine;

/// <summary>
/// Wire Timeline / UnityEvent signal receivers to these methods so cinematics
/// acquire <see cref="PlayerControlGate.ReasonCinematic"/> while playing.
/// </summary>
public class CinematicPlayerControlLock : MonoBehaviour
{
    public void LockCinematic()
    {
        // Continue Apply may already hold this — don't double-acquire (Unlock only Releases once).
        if (!PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            PlayerControlGate.Acquire(PlayerControlGate.ReasonCinematic);
        GameplayHudVisibility.Instance?.HideGameplayHud();
    }

    public void UnlockCinematic()
    {
        PlayerControlGate.Release(PlayerControlGate.ReasonCinematic);
        GameplayHudVisibility.Instance?.ShowGameplayHud();

        // Continue: after intro, same leave-cage grace as a fresh start (not a free 30s from unlock).
        InteractionController.ArmBasementGraceAfterContinueIntro();

        // Same kick as NPCController.Start() — Continue replays the intro, so Natural must
        // be started again when the player unlocks (no LeaveBasement / deleted signal path).
        KickNpcNaturalRoutines();
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

    public static void KickNpcNaturalRoutines()
    {
        var npcs = FindObjectsByType<NPCController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
            npcs[i]?.EnsureNaturalRoutineStarted();
    }
}
