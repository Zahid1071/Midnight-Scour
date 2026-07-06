using UnityEngine;

/// <summary>
/// Put on the same GameObject as the hiding-spot door Animator.
/// Wire Animation Events on Enter/Exit clips to <see cref="PlayDoorOpenSfx"/> / <see cref="PlayDoorCloseSfx"/>.
/// Clips are assigned on the parent <see cref="HidingSpot"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class HidingSpotDoorSfx : MonoBehaviour
{
    HidingSpot _hidingSpot;

    void Awake()
    {
        _hidingSpot = GetComponentInParent<HidingSpot>();
    }

    public void PlayDoorOpenSfx()
    {
        if (_hidingSpot == null)
            return;

        _hidingSpot.PlayDoorOpenSfx(transform.position);
    }

    public void PlayDoorCloseSfx()
    {
        if (_hidingSpot == null)
            return;

        _hidingSpot.PlayDoorCloseSfx(transform.position);
    }
}
