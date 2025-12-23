using UnityEngine;
using System;

public class PerceptionController : MonoBehaviour
{
    [SerializeField] private Transform player;   // assign once in Inspector
    public Transform searchTarget;               // used by Suspicious/Search/Ambush

    // Events (both names for backward compatibility)
    public event Action OnVisionAlerted;   // soft-sight reached (Natural only)
    public event Action OnPlayerSeen;      // alias to the same moment
    public event Action OnVisionConfirmed; // sustained sight -> Chase

    // Runtime state
    public bool CanSeePlayer { get; private set; }
    public Vector3 LastKnownPlayerPosition { get; private set; }
    public float visionMeter { get; private set; } = 0f; // read-only (debug)

    private NPCController npc;

    // Cached config from NPCData
    float maxViewDistance, maxViewHalfAngle;
    float confirmSeconds, buildRate, decayRate;
    float preAlertSeconds;   // now comes from SO
    float gracePeriod, keepChaseDist;
    float ambushAudioMul;

    // Streak guards / timers
    bool alertedThisStreak, confirmedThisStreak;
    float lostSightTimer;

    LayerMask ignoreLayer;

    void Awake()
    {
        npc = GetComponent<NPCController>();
        var data = npc?.interaction?.data;

        if (data == null)
        {
            Debug.LogWarning($"{name}: NPCData missing. Using safe defaults.");
            maxViewDistance = 10f; maxViewHalfAngle = 90f;
            confirmSeconds = 1.5f; buildRate = 1f; decayRate = 1.5f;
            preAlertSeconds = 0.45f;               // fallback default
            gracePeriod = 2f; keepChaseDist = 3f;
            ambushAudioMul = 0.5f;
            return;
        }

        // Vision (from NPCData.VisionSettings)
        maxViewDistance  = data.vision.maxViewDistance;
        maxViewHalfAngle = data.vision.maxViewAngle;
        confirmSeconds   = data.vision.confirmSeconds;
        preAlertSeconds  = data.vision.preAlertSeconds;  // <-- read from SO
        buildRate        = data.vision.buildRate;
        decayRate        = data.vision.decayRate;

        // Hearing & chase retention
        ambushAudioMul   = data.hearing.ambushSuspicionMultiplier;
        gracePeriod      = data.chaseRetention.gracePeriod;
        keepChaseDist    = data.chaseRetention.keepChasingDistance;

        ignoreLayer      = data.vision.ignoreLayer;

        // Safety: if designer left it 0, derive a sane value
        if (preAlertSeconds <= 0f)
            preAlertSeconds = Mathf.Clamp(confirmSeconds * 0.3f, 0.2f, Mathf.Max(0.2f, confirmSeconds - 0.05f));
    }

    void Update()
    {
        VisionCheck();
        UpdateVisionMeters();
        HandleVisionLoss();
    }

    private void VisionCheck()
    {
        if (!player) { CanSeePlayer = false; return; }

        var playerCol = player.GetComponentInChildren<Collider>();
        if (!playerCol) { CanSeePlayer = false; return; }

        Vector3 eye  = transform.position + Vector3.up * 1.7f;
        Vector3 aim  = playerCol.bounds.center;              // ✅ dynamic target
        Vector3 dir  = (aim - eye).normalized;

        if (Physics.Raycast(eye, dir, out RaycastHit hit, maxViewDistance, ~ignoreLayer, QueryTriggerInteraction.Ignore))
        {
            
            //Debug.Log($"Hit: {hit.collider.name} layer={LayerMask.LayerToName(hit.collider.gameObject.layer)} CanSee={CanSeePlayer}");

            // ✅ robust: child colliders still count
            if (hit.collider.transform.root == player.root)
            {
                LastKnownPlayerPosition = aim;
                CanSeePlayer = true;
                return;
            }
        }
            Debug.DrawRay(eye,dir.normalized * maxViewDistance,CanSeePlayer ? Color.green : Color.red);

        CanSeePlayer = false;
    }


    private void UpdateVisionMeters()
    {
        // meter in seconds-equivalent
        if (CanSeePlayer) visionMeter += buildRate * Time.deltaTime;
        else              visionMeter -= decayRate * Time.deltaTime;

        visionMeter = Mathf.Clamp(visionMeter, 0f, confirmSeconds);

        // NATURAL-only soft alert gate
        if (!alertedThisStreak &&
            npc.currentState == NPCController.AIState.Natural &&
            visionMeter >= preAlertSeconds)
        {
            alertedThisStreak = true;
            OnVisionAlerted?.Invoke();
            OnPlayerSeen?.Invoke(); // alias for older/newer NPCController versions
        }

        // Confirm chase (any state)
        if (!confirmedThisStreak && visionMeter >= confirmSeconds)
        {
            confirmedThisStreak = true;
            OnVisionConfirmed?.Invoke();
        }

        // fully drained resets streak guards
        if (!CanSeePlayer && visionMeter <= 0f)
        {
            alertedThisStreak = false;
            confirmedThisStreak = false;
        }
    }

    public void HearPlayerFootstep(Transform soundSource, float suspicionAmount)
    {
        if (!soundSource) return;

        if (npc.currentState == NPCController.AIState.Ambush)
            suspicionAmount *= ambushAudioMul;

        string home = npc.interaction?.data?.homeID ?? string.Empty;
        Transform anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(soundSource.position, home);
        if (!anchor) return;

        LastKnownPlayerPosition = soundSource.position;
        searchTarget = anchor;

        npc.GetComponent<SuspicionSystem>()?.AddFootstepSuspicion(suspicionAmount);
    }

    private void HandleVisionLoss()
    {
        if (CanSeePlayer) { lostSightTimer = 0f; return; }

        if (npc.currentState == NPCController.AIState.Chase)
        {
            float dist = Vector3.Distance(transform.position, player.position);

            if (dist < keepChaseDist)
            {
                lostSightTimer = 0f;
            }
            else
            {
                lostSightTimer += Time.deltaTime;
                // if (lostSightTimer >= gracePeriod)
                //     npc.fsm.SetState(NPCController.AIState.Suspicious);
            }
        }
    }

    // Convenience accessor
    public Transform PlayerTransform => player;
}
