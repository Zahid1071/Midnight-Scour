using UnityEngine;

/// <summary>
/// Wire Timeline / UnityEvent signal receivers to these methods so cinematics
/// acquire <see cref="PlayerControlGate.ReasonCinematic"/> while playing.
/// </summary>
public class CinematicPlayerControlLock : MonoBehaviour
{
    public void LockCinematic() => PlayerControlGate.Acquire(PlayerControlGate.ReasonCinematic);

    public void UnlockCinematic() => PlayerControlGate.Release(PlayerControlGate.ReasonCinematic);
}
