using UnityEngine;
using FS_Atmo;

/// <summary>
/// Sets a bool on the door while the player is inside this trigger. Enter/Stay = true, Exit = false.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DoorPlayerObstructionTrigger : MonoBehaviour
{
    public enum Motion { Opening, Closing }

    [SerializeField] SimpleOpenClose door;
    [SerializeField] Motion blocksMotion = Motion.Opening;

    void Awake()
    {
        if (door == null)
            door = GetComponentInParent<SimpleOpenClose>();
        if (door == null)
            door = transform.root.GetComponentInChildren<SimpleOpenClose>(true);

        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other) => SetBlocked(other, true);
    void OnTriggerStay(Collider other) => SetBlocked(other, true);
    void OnTriggerExit(Collider other) => SetBlocked(other, false);

    void SetBlocked(Collider other, bool blocked)
    {
        if (door == null || !IsPlayer(other))
            return;

        if (blocksMotion == Motion.Opening)
            door.playerBlockingOpen = blocked;
        else
            door.playerBlockingClose = blocked;
    }

    static bool IsPlayer(Collider other)
    {
        if (other == null) return false;
        if (other.CompareTag("Player")) return true;
        return other.GetComponentInParent<CharacterController>() != null;
    }
}
