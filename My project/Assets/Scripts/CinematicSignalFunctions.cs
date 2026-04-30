using FS_Atmo;
using UnityEngine;

public class CinematicSignalFunctions : MonoBehaviour
{
    NPCController npc;
    Navigator navigator;
    public Transform destination;
    
    // Flag to track when the cinematic move is active
    private bool isMoving = false;

    void Start()
    {
        navigator = GetComponent<Navigator>();
        npc = GetComponent<NPCController>();
    }

    void Update()
    {
        // Only check distance if the Timeline signal has started the movement
        if (isMoving && (transform.position - destination.position).magnitude < 0.1f)
        {
            // Shut off the flag immediately to prevent Update from firing this again
            isMoving = false; 
            
            npc.fsm.SetState(NPCController.AIState.Natural);
        }
    }

    public void LeaveBasement()
    {
        // Open the gate so Update starts looking for the destination
        isMoving = true; 
        
        navigator.GoToPoint(destination.position);
        npc.walkSpeed = 1f;
    }
}