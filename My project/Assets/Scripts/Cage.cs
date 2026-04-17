using UnityEngine;
using FS_Atmo;

public class Cage : MonoBehaviour
{
    [Header("References")]
    public SimpleOpenClose cageDoor;
    public bool isOccupied = false;

    // Hardcoded layer ID as requested
    private const int CAPTIVE_LAYER = 12;

    // Variables to track player state
    private int originalLayerIndex;
    private GameObject playerRef;

    private void Update()
    {
        // Safety check
        if (!isOccupied || playerRef == null || cageDoor == null) return;

        // Logic:
        // Door OPEN   -> Use Original Layer (Visible)
        // Door CLOSED -> Use Captive Layer (Hidden/ID 12)
        int targetLayer = cageDoor.objectOpen ? originalLayerIndex : CAPTIVE_LAYER;

        // Only assign if different to avoid overhead
        if (playerRef.layer != targetLayer)
        {
            playerRef.layer = targetLayer;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Capture the layer the player had when they walked in
            originalLayerIndex = other.gameObject.layer;
            
            playerRef = other.gameObject;
            isOccupied = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            isOccupied = false;

            // Restore the player to their original layer upon exit
            if (playerRef != null)
            {
                playerRef.layer = originalLayerIndex;
            }

            playerRef = null;
        }
    }
}