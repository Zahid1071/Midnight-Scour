using FS_Atmo;
using UnityEngine;

/// <summary>
/// Basement intro: wire MoveToGrate and LeaveBasement Timeline signals on the butcher Signal Receiver.
/// </summary>
public class CinematicSignalFunctions : MonoBehaviour
{
    NPCController npc;
    Navigator navigator;
    public Transform gratePosition;
    public Transform destination;

    bool isLeavingBasement;

    void Start()
    {
        navigator = GetComponent<Navigator>();
        npc = GetComponent<NPCController>();
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

    public void LeaveBasement()
    {
        if (destination == null || navigator == null)
            return;

        isLeavingBasement = true;
        navigator.GoToPoint(destination.position);
    }
}
