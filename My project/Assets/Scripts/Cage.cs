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
    private bool _doorWasOpen;

    private void Awake()
    {
        if (cageDoor != null)
            _doorWasOpen = cageDoor.objectOpen;
    }

    private void Update()
    {
        if (cageDoor != null)
        {
            if (cageDoor.objectOpen && !_doorWasOpen)
                GameplayHintManager.Instance?.NotifyCageDoorOpenedFirstTime();

            _doorWasOpen = cageDoor.objectOpen;
        }

        // Safety check
        if (!isOccupied || playerRef == null || cageDoor == null) return;

        // Logic:
        // Door OPEN   -> Use Original Layer (Visible)
        // Door CLOSED -> Use Captive Layer (Hidden/ID 12)
        int targetLayer = originalLayerIndex;

        bool cageVisible = false;

        if (!cageDoor.objectOpen)
        {
            // Captive layer only if no LOS to cage and door closed.
            cageVisible = IsCageVisibleToAnyNPC();
            if (!cageVisible)
                targetLayer = CAPTIVE_LAYER;
        }

        // Only assign if different to avoid overhead
        if (playerRef.layer != targetLayer)
        {
            playerRef.layer = targetLayer;
        }
    }

    private bool IsCageVisibleToAnyNPC()
    {
#if UNITY_2023_1_OR_NEWER
        var perceivers = Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None);
#else
        var perceivers = Object.FindObjectsOfType<PerceptionController>();
#endif

        // cage center target for raycasts
        Vector3 cageCenter = transform.position + Vector3.up * 1.5f;
        Collider myCol = GetComponent<Collider>();
        if (myCol != null) cageCenter = myCol.bounds.center;

        foreach (var p in perceivers)
        {
            if (p == null) continue;

            Vector3 eye = p.transform.position + Vector3.up * 1.7f;
            Vector3 dir = (cageCenter - eye).normalized;
            float dist = Vector3.Distance(eye, cageCenter);

            if (dist > p.MaxViewDistance) continue;

            float angle = Vector3.Angle(p.transform.forward, dir);
            if (angle > p.MaxViewHalfAngle) continue;

            if (Physics.Raycast(eye, dir, out RaycastHit hit, p.MaxViewDistance, ~p.IgnoreLayer, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.collider.GetComponentInParent<Cage>() == this)
                    return true;
            }
        }

        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Capture the layer the player had when they walked in
            originalLayerIndex = other.gameObject.layer;

            // Track the root so we don't accidentally toggle only a child collider.
            playerRef = other.transform.root != null ? other.transform.root.gameObject : other.gameObject;
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