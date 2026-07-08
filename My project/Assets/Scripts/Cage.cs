using UnityEngine;
using FS_Atmo;

public class Cage : MonoBehaviour
{
    [Header("References")]
    public SimpleOpenClose cageDoor;
    [Tooltip("Basement-side pre-point for the cage door. Auto-finds DoorController.prePointA if unset.")]
    public Transform basementSidePrePoint;
    public bool isOccupied = false;

    [Header("NPC Noise")]
    [Tooltip("Footstep-suspicion value added to NPCs whenever the cage door opens or closes.")]
    [Min(0f)]
    public float doorSoundSuspicion = 300f;
    [Tooltip("Distance at which the cage door sound is barely audible to NPCs.")]
    [Min(0f)]
    public float doorSoundMaxHearDistance = 150f;
    [Tooltip("Distance within which the cage door sound is heard at full strength.")]
    [Min(0f)]
    public float doorSoundMinHearDistance = 5f;

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
        // If the player starts inside the trigger (e.g. scene start/load),
        // OnTriggerEnter may never fire, so we re-detect occupancy cheaply.
        EnsureOccupancy();

        if (cageDoor != null)
        {
            bool isOpenNow = cageDoor.objectOpen;
            if (isOpenNow != _doorWasOpen)
            {
                if (isOpenNow)
                    GameplayHintManager.Instance?.NotifyCageDoorOpenedFirstTime();

                NotifyNpcsOfDoorSound();
            }

            _doorWasOpen = isOpenNow;
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
            bool becameCaptive = targetLayer == CAPTIVE_LAYER && playerRef.layer != CAPTIVE_LAYER;
            playerRef.layer = targetLayer;

            if (becameCaptive)
                RedirectNpcHearingAwayFromCage();
        }
    }

    void NotifyNpcsOfDoorSound()
    {
        if (doorSoundSuspicion <= 0f)
            return;

        // Emit from just outside the cage (basement side) rather than the door/cage interior itself -
        // the cage isn't a real searchable room, so we don't want NPC anchor resolution latching onto
        // it as the "closest room" to investigate.
        Transform prePoint = ResolveBasementSidePrePoint();
        Vector3 soundPosition = prePoint != null
            ? prePoint.position
            : (cageDoor != null ? cageDoor.transform.position : transform.position);

#if UNITY_2023_1_OR_NEWER
        foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
#else
        foreach (var perception in Object.FindObjectsOfType<PerceptionController>())
#endif
        {
            if (perception != null)
                perception.HearNoiseAt(soundPosition, doorSoundSuspicion, doorSoundMaxHearDistance, doorSoundMinHearDistance);
        }
    }

    void RedirectNpcHearingAwayFromCage()
    {
        Transform prePoint = ResolveBasementSidePrePoint();
        if (prePoint == null)
            return;

        PerceptionController.RedirectAllLastKnownPositions(prePoint.position);
    }

    Transform ResolveBasementSidePrePoint()
    {
        if (basementSidePrePoint != null)
            return basementSidePrePoint;

        if (cageDoor != null)
        {
            var doorController = cageDoor.GetComponentInParent<DoorController>();
            if (doorController != null && doorController.prePointA != null)
                return doorController.prePointA;
        }

        var transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null)
                continue;

            string name = t.name.Trim();
            if (name.Equals("PrePointA", System.StringComparison.OrdinalIgnoreCase))
                return t;
        }

        return null;
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

    void EnsureOccupancy()
    {
        if (isOccupied && playerRef != null)
            return;

        Collider triggerCol = GetComponent<Collider>();
        if (triggerCol == null)
            return;

        // Must be a trigger volume for this to make sense.
        if (!triggerCol.isTrigger)
            return;

        var player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        Vector3 p = player.transform.position;
        if (!triggerCol.bounds.Contains(p))
            return;

        // Treat as captured/inside.
        playerRef = player.transform.root != null ? player.transform.root.gameObject : player;
        originalLayerIndex = playerRef.layer;
        isOccupied = true;
    }
}