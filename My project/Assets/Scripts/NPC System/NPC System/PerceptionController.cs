using UnityEngine;
using System;

public class PerceptionController : MonoBehaviour
{
    private enum FaceAxis
    {
        Forward,
        Back,
        Up,
        Down,
        Right,
        Left
    }

    [SerializeField] private Transform player;   // assign once in Inspector
    [SerializeField] private Transform face;     // ray origin + forward for vision/FOV
    [SerializeField] private FaceAxis faceForwardAxis = FaceAxis.Forward;
    [SerializeField] private Vector3 faceForwardEulerOffset;
    public Transform searchTarget;               // used by Suspicious/Search/Ambush

    public event Action OnVisionConfirmed; // sustained sight -> Chase

    // Runtime state
    public bool CanSeePlayer { get; private set; }
    /// <summary>Time.time of the last frame the player was in sight.</summary>
    public float LastSawPlayerTime { get; private set; } = float.NegativeInfinity;
    public Vector3 LastKnownPlayerPosition { get; private set; }
    public float visionMeter { get; private set; } = 0f; // read-only (debug)

    private NPCController npc;

    // Cached config from NPCData
    float maxViewDistance, maxViewHalfAngle;
    float confirmSeconds, buildRate, decayRate, instantConfirmDistance;
    float gracePeriod, keepChaseDist;
    float ambushAudioMul;

    bool confirmedThisStreak;
    float lostSightTimer;

    LayerMask ignoreLayer;

    // Expose read-only view config for simple external checks (hiding/cage).
    public float MaxViewDistance => maxViewDistance;
    public float MaxViewHalfAngle => maxViewHalfAngle;
    public LayerMask IgnoreLayer => ignoreLayer;

    // Up close, peripheral vision effectively becomes a half-circle - the player can't stand
    // right next to/behind the NPC's shoulder and expect to go unnoticed just because they're
    // outside the "normal" cone.
    const float CloseRangeWideFovDistance = 2f;
    const float CloseRangeWideFovHalfAngle = 90f;

    float EffectiveViewHalfAngle(float distanceToTarget) =>
        distanceToTarget < CloseRangeWideFovDistance ? CloseRangeWideFovHalfAngle : maxViewHalfAngle;

    void Awake()
    {
        npc = GetComponent<NPCController>();
        var data = npc?.interaction?.data;

        if (data == null)
        {
            Debug.LogWarning($"{name}: NPCData missing. Using safe defaults.");
            maxViewDistance = 10f; maxViewHalfAngle = 90f;
            confirmSeconds = 1.5f; buildRate = 1f; decayRate = 1.5f;
            instantConfirmDistance = 2f;
            gracePeriod = 2f; keepChaseDist = 3f;
            ambushAudioMul = 0.5f;
            return;
        }

        maxViewDistance  = data.vision.maxViewDistance;
        maxViewHalfAngle = data.vision.maxViewAngle;
        confirmSeconds   = Mathf.Max(0f, data.vision.confirmSeconds);
        buildRate        = Mathf.Max(0.01f, data.vision.buildRate);
        decayRate        = Mathf.Max(0.01f, data.vision.decayRate);
        instantConfirmDistance = Mathf.Max(0f, data.vision.instantConfirmDistance);

        ambushAudioMul   = data.hearing.ambushSuspicionMultiplier;
        gracePeriod      = data.chaseRetention.gracePeriod;
        keepChaseDist    = data.chaseRetention.keepChasingDistance;

        ignoreLayer      = data.vision.ignoreLayer;
        int cageDoorLayer = LayerMask.NameToLayer("CageDoor");
        if (cageDoorLayer >= 0)
            ignoreLayer |= 1 << cageDoorLayer;
    }

    void Update()
    {
        PlayerCheckVision();
        UpdateVisionMeters();
        HandleVisionLoss();
        CageCheckVision();
    }

    private static Vector3 AxisVector(FaceAxis axis)
    {
        return axis switch
        {
            FaceAxis.Forward => Vector3.forward,
            FaceAxis.Back => Vector3.back,
            FaceAxis.Up => Vector3.up,
            FaceAxis.Down => Vector3.down,
            FaceAxis.Right => Vector3.right,
            FaceAxis.Left => Vector3.left,
            _ => Vector3.forward
        };
    }

    private void GetViewBasis(out Vector3 origin, out Vector3 forward, out Vector3 upAxis)
    {
        Transform view = face ? face : transform;

        Quaternion basisRot = view.rotation * Quaternion.Euler(faceForwardEulerOffset);
        origin = view.position;
        forward = (basisRot * AxisVector(faceForwardAxis)).normalized;
        Vector3 candidateUp = (basisRot * Vector3.up).normalized;

        if (Mathf.Abs(Vector3.Dot(candidateUp, forward)) > 0.95f)
            upAxis = (basisRot * Vector3.right).normalized;
        else
            upAxis = candidateUp;
    }

    private void OnDrawGizmosSelected()
    {
        float dist = maxViewDistance > 0f ? maxViewDistance : 10f;
        float half = maxViewHalfAngle > 0f ? maxViewHalfAngle : 90f;

        GetViewBasis(out Vector3 origin, out Vector3 forward, out Vector3 upAxis);

        Vector3 leftDir = Quaternion.AngleAxis(-half, upAxis) * forward;
        Vector3 rightDir = Quaternion.AngleAxis(half, upAxis) * forward;

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(origin, origin + forward * dist);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(origin, origin + leftDir * dist);
        Gizmos.DrawLine(origin, origin + rightDir * dist);

        Gizmos.color = Color.blue;
        const int segments = 24;
        Vector3 prev = origin + leftDir * dist;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(-half, half, t);
            Vector3 dir = Quaternion.AngleAxis(angle, upAxis) * forward;
            Vector3 next = origin + dir * dist;
            Gizmos.DrawLine(prev, next);
            prev = next;
        }

        // Close-range "peripheral vision" cone (widens to 90° each side inside this radius).
        Gizmos.color = Color.magenta;
        Vector3 wideLeft = Quaternion.AngleAxis(-CloseRangeWideFovHalfAngle, upAxis) * forward;
        Vector3 wideRight = Quaternion.AngleAxis(CloseRangeWideFovHalfAngle, upAxis) * forward;
        Gizmos.DrawLine(origin, origin + wideLeft * CloseRangeWideFovDistance);
        Gizmos.DrawLine(origin, origin + wideRight * CloseRangeWideFovDistance);

        Vector3 prevWide = origin + wideLeft * CloseRangeWideFovDistance;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(-CloseRangeWideFovHalfAngle, CloseRangeWideFovHalfAngle, t);
            Vector3 dir = Quaternion.AngleAxis(angle, upAxis) * forward;
            Vector3 next = origin + dir * CloseRangeWideFovDistance;
            Gizmos.DrawLine(prevWide, next);
            prevWide = next;
        }
    }

    /// <summary>
    /// Raw sight check that ignores gameplay "hidden" layers.
    /// Use this BEFORE the player changes layers to decide if hiding/cage should be allowed.
    /// </summary>
    public bool HasDirectLineOfSightToPlayer()
    {
        if (!player) return false;

        var playerCol = player.GetComponentInChildren<Collider>();
        if (!playerCol) return false;

        GetViewBasis(out Vector3 eye, out Vector3 viewForward, out _);
        Vector3 aim = playerCol.bounds.center;
        Vector3 dirToTarget = (aim - eye).normalized;
        float distanceToTarget = Vector3.Distance(eye, aim);

        float angleToTarget = Vector3.Angle(viewForward, dirToTarget);
        if (angleToTarget > EffectiveViewHalfAngle(distanceToTarget)) return false;

        if (Physics.Raycast(eye, dirToTarget, out RaycastHit hit, maxViewDistance, ~ignoreLayer, QueryTriggerInteraction.Ignore))
        {
            return hit.collider != null && hit.collider.transform.root == player.root;
        }

        return false;
    }

    private void PlayerCheckVision()
    {
        if (!player) { CanSeePlayer = false; return; }

        // HiddenPlayer (7) or Captive in closed cage (12) — invisible to vision.
        int layer = player.gameObject.layer;
        if (layer == 7 || layer == 12)
        {
            CanSeePlayer = false;
            return;
        }

        var hider = player.GetComponent<PlayerHider>();
        if (hider != null && hider.isHidden)
        {
            CanSeePlayer = false;
            return;
        }

        var playerCol = player.GetComponentInChildren<Collider>();
        if (!playerCol) { CanSeePlayer = false; return; }

        GetViewBasis(out Vector3 eye, out Vector3 viewForward, out _);
        Vector3 aim = playerCol.bounds.center;
        Vector3 dirToTarget = (aim - eye).normalized;
        float distanceToTarget = Vector3.Distance(eye, aim);

        float angleToTarget = Vector3.Angle(viewForward, dirToTarget);
        if (angleToTarget > EffectiveViewHalfAngle(distanceToTarget))
        {
            CanSeePlayer = false;
            return;
        }

        if (Physics.Raycast(eye, dirToTarget, out RaycastHit hit, maxViewDistance, ~ignoreLayer, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.root == player.root)
            {
                if (!Cage.ContainsWorldPoint(aim))
                    LastKnownPlayerPosition = aim;
                CanSeePlayer = true;
                LastSawPlayerTime = Time.time;
                return;
            }
        }

        Debug.DrawRay(eye, dirToTarget * maxViewDistance, CanSeePlayer ? Color.green : Color.red);

        CanSeePlayer = false;
    }

    private void UpdateVisionMeters()
    {
        // Scripted cinematic holds ignore OnVisionConfirmed — never arm a streak that
        // would block the next real sighting after release.
        if (npc != null && npc.currentState == NPCController.AIState.Scripted)
        {
            visionMeter = 0f;
            confirmedThisStreak = false;
            return;
        }

        if (CanSeePlayer && ShouldInstantConfirmVision())
        {
            visionMeter = confirmSeconds;
            if (!confirmedThisStreak)
            {
                confirmedThisStreak = true;
                OnVisionConfirmed?.Invoke();
            }
            return;
        }

        if (CanSeePlayer)
            visionMeter += buildRate * Time.deltaTime;
        else
            visionMeter -= decayRate * Time.deltaTime;

        visionMeter = Mathf.Clamp(visionMeter, 0f, confirmSeconds);

        if (!confirmedThisStreak && visionMeter >= confirmSeconds)
        {
            confirmedThisStreak = true;
            OnVisionConfirmed?.Invoke();
        }

        if (!CanSeePlayer && visionMeter <= 0f)
            confirmedThisStreak = false;
    }

    /// <summary>Clears a pending vision confirm so the next LoS can start a chase again.</summary>
    public void ResetVisionConfirmation()
    {
        visionMeter = 0f;
        confirmedThisStreak = false;
    }

    /// <summary>
    /// True when the player is currently visible and within <see cref="NPCData.vision.instantConfirmDistance"/>.
    /// </summary>
    public bool CanInstantConfirmVisiblePlayer()
    {
        if (!CanSeePlayer)
            return false;

        return ShouldInstantConfirmVision();
    }

    bool ShouldInstantConfirmVision()
    {
        if (instantConfirmDistance <= 0f || !player)
            return false;

        if (player.gameObject.layer == 7 || player.gameObject.layer == 12)
            return false;

        var hider = player.GetComponent<PlayerHider>();
        if (hider != null && hider.isHidden)
            return false;

        return Vector3.Distance(transform.position, player.position) < instantConfirmDistance;
    }

    public const float DefaultMaxHearingDistance = 15f;
    public const float DefaultMinHearingDistance = 5f;
    public const float DistanceHearingFalloffPower = 5f;

    public static float DistanceHearingMultiplier(
        Vector3 soundPosition,
        Vector3 listenerPosition,
        float maxHearingDistance = DefaultMaxHearingDistance,
        float minHearingDistance = DefaultMinHearingDistance)
    {
        float dist = Vector3.Distance(listenerPosition, soundPosition);
        float t = Mathf.InverseLerp(maxHearingDistance, minHearingDistance, dist);
        return Mathf.Pow(Mathf.Clamp01(t), DistanceHearingFalloffPower);
    }

    /// <summary>
    /// Loud local noise scaled by the same distance falloff used for player footsteps.
    /// </summary>
    public void HearNoiseAt(
        Vector3 worldPosition,
        float baseSuspicion,
        float maxHearingDistance = DefaultMaxHearingDistance,
        float minHearingDistance = DefaultMinHearingDistance,
        bool playInvestigateSfx = true)
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            return;
        if (npc != null && npc.HearingDisabled)
            return;
        if (ThunderManager.ArePlayerNoisesMasked)
            return;

        float scaled = baseSuspicion * DistanceHearingMultiplier(
            worldPosition, transform.position, maxHearingDistance, minHearingDistance);

        if (scaled <= 0f)
            return;

        HearLoudWorldNoise(worldPosition, scaled, playInvestigateSfx);
    }

    public void HearPlayerFootstep(Transform soundSource, float suspicionAmount)
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            return;
        if (npc != null && npc.HearingDisabled)
            return;
        if (ThunderManager.ArePlayerNoisesMasked)
            return;

        if (npc != null && npc.IsIgnoringPlayerFootsteps)
            return;

        if (!soundSource) return;

        if (soundSource.gameObject.layer == 12) return;
        if (Cage.ContainsWorldPoint(soundSource.position)) return;

        if (npc.currentState == NPCController.AIState.Ambush)
            suspicionAmount *= ambushAudioMul;

        string home = npc.interaction?.data?.homeID ?? string.Empty;
        Transform anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(soundSource.position, home);
        if (!anchor) return;

        LastKnownPlayerPosition = soundSource.position;
        searchTarget = anchor;

        npc.GetComponent<SuspicionSystem>()?.AddFootstepSuspicion(suspicionAmount);
    }

    /// <summary>
    /// Loud one-shot at a world position (e.g. body bag impact). Unlike <see cref="HearPlayerFootstep"/>,
    /// does not abort when no anchor is found — NPC still gets footstep suspicion and last-known point.
    /// </summary>
    public void HearLoudWorldNoise(
        Vector3 worldPosition,
        float footstepSuspicionToAdd = 300f,
        bool playInvestigateSfx = true)
    {
        if (npc != null && npc.HearingDisabled)
            return;
        if (ThunderManager.ArePlayerNoisesMasked)
            return;

        if (npc.currentState == NPCController.AIState.Ambush)
            footstepSuspicionToAdd *= ambushAudioMul;

        if (Cage.ContainsWorldPoint(worldPosition))
        {
            npc.GetComponent<SuspicionSystem>()?.AddFootstepSuspicion(footstepSuspicionToAdd);
            return;
        }

        string home = npc.interaction?.data?.homeID ?? string.Empty;
        Transform anchor = NPCAnchorManager.Instance != null
            ? NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(worldPosition, home)
            : null;

        LastKnownPlayerPosition = worldPosition;
        searchTarget = anchor;

        npc.GetComponent<SuspicionSystem>()?.AddFootstepSuspicion(footstepSuspicionToAdd);

        // Pig bark is handled by Chase enter (Natural/Ambush → Chase) and other story cues —
        // do not bark again on every vase/grate/world noise while already hunting.
    }

    /// <summary>
    /// Moves the NPC's last-heard/last-known point (e.g. when the player hides in the cage).
    /// </summary>
    public void RedirectLastKnownPosition(Vector3 worldPosition)
    {
        if (Cage.ContainsWorldPoint(worldPosition))
            return;

        LastKnownPlayerPosition = worldPosition;

        string home = npc?.interaction?.data?.homeID ?? string.Empty;
        if (NPCAnchorManager.Instance != null)
        {
            Transform anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(worldPosition, home);
            if (anchor != null)
                searchTarget = anchor;
        }

        if (npc != null)
            npc._lastChaseTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
    }

    private void HandleVisionLoss()
    {
        if (CanSeePlayer) { lostSightTimer = 0f; return; }

        if (npc.currentState == NPCController.AIState.Chase)
        {
            float dist = Vector3.Distance(transform.position, player.position);

            if (dist < keepChaseDist)
                lostSightTimer = 0f;
            else
                lostSightTimer += Time.deltaTime;
        }
    }

    [Header("Cage Settings")]
    public Cage targetCage;
    private bool cageAlertTriggered = false;

    /// <summary>Continue / day-reset — allow a fresh cage-escape reaction later.</summary>
    public void ResetCageAlert() => cageAlertTriggered = false;

    public void ClearVisionState()
    {
        CanSeePlayer = false;
        LastSawPlayerTime = float.NegativeInfinity;
        visionMeter = 0f;
        confirmedThisStreak = false;
        lostSightTimer = 0f;
    }

    private void CageCheckVision()
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic)) return;
        if (targetCage == null || cageAlertTriggered) return;
        // Default empty/open cage must not look like an escape during normal chase/search.
        if (!targetCage.PendingEscapeSighting) return;

        Vector3 eye = transform.position + Vector3.up * 1.7f;

        Vector3 cageCenter = targetCage.transform.position;
        Collider triggerCol = targetCage.GetComponent<Collider>();
        if (triggerCol != null) cageCenter = triggerCol.bounds.center;
        else cageCenter += Vector3.up * 1.5f;

        Vector3 dirToCage = (cageCenter - eye).normalized;
        float distToCage = Vector3.Distance(eye, cageCenter);

        if (distToCage > maxViewDistance) return;

        if (Physics.Raycast(eye, dirToCage, out RaycastHit hit, maxViewDistance, ~ignoreLayer, QueryTriggerInteraction.Ignore))
        {
            Cage hitCage = hit.transform.GetComponentInParent<Cage>();

            if (hitCage != null && hitCage == targetCage)
            {
                bool isEmpty = (hitCage.isOccupied == false);
                bool isOpen = (hitCage.cageDoor != null && hitCage.cageDoor.objectOpen);

                if (isEmpty || isOpen)
                {
                    Debug.Log($"⚠️ NPC: Player Escaped! (isEmpty: {isEmpty}, isOpen: {isOpen})");

                    cageAlertTriggered = true;
                    hitCage.ConsumeEscapeSighting();

                    if (npc.currentState != NPCController.AIState.Chase
                        && npc.currentState != NPCController.AIState.Capture)
                    {
                        npc.fsm?.BeginCageEscapeReaction();
                    }
                }
            }
        }
    }

    public Transform PlayerTransform => player;
}
