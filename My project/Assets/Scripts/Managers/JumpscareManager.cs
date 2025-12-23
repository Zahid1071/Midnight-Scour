using UnityEngine;
using Unity.Cinemachine;

public class JumpscareManager : MonoBehaviour
{
    public static JumpscareManager Instance;
    public CinemachineCamera jumpscareCam;
    private GameObject player;

    void Awake()
    {
        Instance = this;
        player = GameObject.FindWithTag("Player");
    }

    public void TriggerJumpscare(Transform jumpscarePoint, Transform npcFace)
    {
        // Position the camera at the jumpscare point
        jumpscareCam.transform.position = jumpscarePoint.position;

        jumpscareCam.Priority = 50;

        // Disable player input here
    }


    public void EndJumpscare()
    {
        // Lower camera priority so player cam takes over
        jumpscareCam.Priority = 0;

        // Re-enable player input or show game over screen
        // playerControllerScript.enabled = true; // if you want to restore control
    }
}
