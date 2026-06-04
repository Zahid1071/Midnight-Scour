using FS_Atmo;
using UnityEngine;

/// <summary>
/// Basement intro: wire three Timeline signals on the butcher Signal Receiver —
/// MoveToGrate, DropKey, LeaveBasement (in that order).
/// </summary>
public class CinematicSignalFunctions : MonoBehaviour
{
    NPCController npc;
    Navigator navigator;
    public Transform gratePosition;
    public Transform destination;
    public Transform key;
    [SerializeField] private Collider keyCollider;
    [SerializeField] private Collider grateCollider;

    bool isLeavingBasement;

    void Start()
    {
        navigator = GetComponent<Navigator>();
        npc = GetComponent<NPCController>();
        if (keyCollider != null && grateCollider != null)
            Physics.IgnoreCollision(keyCollider, grateCollider, true);
    }

    void Update()
    {
        if (!isLeavingBasement || destination == null || npc == null)
            return;

        if ((transform.position - destination.position).sqrMagnitude > 0.01f)
            return;

        isLeavingBasement = false;
        npc.fsm.SetState(NPCController.AIState.Natural);
    }

    public void MoveToGrate()
    {
        if (gratePosition == null || navigator == null)
            return;

        navigator.GoToPoint(gratePosition.position);
    }

    public void DropKey()
    {
        if (key == null)
            return;

        key.SetParent(null);
        var rb = key.GetComponent<Rigidbody>();
        if (rb == null)
            return;

        rb.useGravity = true;
        rb.isKinematic = false;
    }

    public void LeaveBasement()
    {
        if (destination == null || navigator == null)
            return;

        isLeavingBasement = true;
        navigator.GoToPoint(destination.position);
    }
}
