using FS_Atmo;
using UnityEngine;

public class CinematicSignalFunctions : MonoBehaviour
{
    NPCController npc;
    Navigator navigator;
    public Transform destination;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        navigator = GetComponent<Navigator>();
        npc = GetComponent<NPCController>();
    }

    // Update is called once per frame
    void Update()
    {
        if ((transform.position-destination.position).magnitude < 1)
        {
            
            npc.fsm.SetState(NPCController.AIState.Natural);
        }
    }

    public void LeaveBasement()
    {
        navigator.GoToPoint(destination.position);
        npc.walkSpeed = 1;
    }
}
