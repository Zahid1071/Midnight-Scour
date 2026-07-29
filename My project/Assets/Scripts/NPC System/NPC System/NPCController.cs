using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class NPCController : MonoBehaviour
{
    public enum AIState { Natural, Suspicious, Search, Chase, Capture, Dead, Patrol, Ambush, Scripted }
    public AIState currentState;

    [HideInInspector] public FSMController fsm;
    [HideInInspector] public InteractionController interaction;
    [HideInInspector] public PerceptionController perception;
    private SuspicionSystem suspicion;
    private FS_Atmo.Navigator navigator;
    private NavMeshAgent agent;

    [Header("Look / POI")]
    [Tooltip("Optional. Assign the scene's POI look director (can live anywhere; does NOT need to be a child).")]
    public POILookDirector poiLookDirector;

    [Header("Capture")]
    [Tooltip("Distance at which the NPC instantly captures the player while chasing.")]
    public float captureDistance = 1.8f;
    [Tooltip("Seconds to wait after starting hide exit before jumpscare (when captured while hiding).")]
    public float hideExitSecondsBeforeCapture = 1.5f;

    [Header("Ambush")]
    [SerializeField] AudioClip ambushMetalRustleSfx;
    [SerializeField] float ambushRustleVolume = 1f;
    [SerializeField] float ambushPositionTolerance = 0.75f;
    [SerializeField] float ambushTurnSpeed = 120f;
    [SerializeField] float ambushTurnAngleThreshold = 45f;

    public AudioClip AmbushMetalRustleSfx => ambushMetalRustleSfx;
    public float AmbushRustleVolume => ambushRustleVolume;
    public float AmbushPositionTolerance => ambushPositionTolerance;
    public float AmbushTurnSpeed => ambushTurnSpeed;
    public float AmbushTurnAngleThreshold => ambushTurnAngleThreshold;

    [Header("Chase SFX")]
    [Tooltip("3D one-shot played at the butcher when entering Chase.")]
    [SerializeField] AudioClip chaseEnterSfx;
    [SerializeField] float chaseEnterVolume = 1f;

    public AudioClip ChaseEnterSfx => chaseEnterSfx;
    public float ChaseEnterVolume => chaseEnterVolume;

    [Header("Hearing")]
    [Tooltip("After entering the basement (door open SFX), ignore player footsteps for this long. Other noises still register.")]
    [SerializeField] float ignorePlayerFootstepsDuration = 2f;
    float _ignorePlayerFootstepsUntil = -1f;

    public bool IsIgnoringPlayerFootsteps => Time.time < _ignorePlayerFootstepsUntil;

    /// <summary>Best current-room estimate from the navigator's path-based room vote.</summary>
    public RoomAnchor GetCurrentRoom() => navigator != null ? navigator.GetCurrentRoom() : null;

    /// <summary>Mute player footsteps only for a short window (other world noises still heard).</summary>
    public void BeginIgnorePlayerFootsteps(float durationSeconds = -1f)
    {
        float duration = durationSeconds >= 0f ? durationSeconds : ignorePlayerFootstepsDuration;
        if (duration <= 0f)
            return;

        _ignorePlayerFootstepsUntil = Time.time + duration;
        suspicion?.ClearFootstepSuspicion();
    }

    [Header("Chase Replan")]
    [Tooltip("Seconds between destination replans during chase.")]
    public float chaseReplanCooldown = 0.25f;
    [Tooltip("Replan if the target moved more than this distance since last plan.")]
    public float chaseReplanDist = 0.75f;
    [Tooltip("NavMeshAgent speed multiplier while chasing AND seeing the player (1 = normal run).")]
    [Min(1f)]
    public float chaseVisibleSpeedMultiplier = 1.25f;

    [Header("Speeds (override from NPCData if present)")]
    [HideInInspector] public float walkSpeed = 2.5f;
    [HideInInspector] public float runSpeed  = 6.0f;

    // internal chase guards
    public Vector3 _lastChaseTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
    public float   _nextChaseReplanTime = 0f;

    // optional: expose for debug
    public Transform jumpscarePoint;

    public bool HearingDisabled { get; private set; }

    Coroutine _captureRoutine;

    public void BeginCapture()
    {
        if (_captureRoutine != null) return;
        if (currentState == AIState.Capture && PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            return;
        _captureRoutine = StartCoroutine(fsm.CapturePlayerRoutine());
    }

    internal void OnCaptureRoutineFinished()
    {
        _captureRoutine = null;
    }

    void Awake()
    {
        fsm          = new FSMController(this);
        interaction  = GetComponent<InteractionController>();
        perception   = GetComponent<PerceptionController>();
        suspicion    = GetComponent<SuspicionSystem>();
        navigator    = GetComponent<FS_Atmo.Navigator>();
        agent        = GetComponent<NavMeshAgent>();

        // Pull sane defaults from NPCData if present
        var data = interaction != null ? interaction.data : null;
        if (data != null)
        {
            walkSpeed = Mathf.Max(0.01f, data.locomotion.walkingSpeed);
            runSpeed  = Mathf.Max(walkSpeed, data.locomotion.runningSpeed);
        }

        if (perception != null)
            perception.OnVisionConfirmed += HandleVisionConfirmed;

        if (suspicion != null)
            suspicion.OnSuspicionThresholdReached += HandleSuspicionReached;
    }

    void Start()
    {
        // Prefab default is already Natural (enum 0). SetState early-outs on same state and
        // would skip StartNextRoutine — so kick the natural loop explicitly in that case.
        if (currentState == AIState.Natural)
            interaction?.StartNextRoutine();
        else
            fsm.SetState(AIState.Natural);

        ApplyLocomotionForState(currentState);
    }

    void Update()
    {
        fsm?.Update();
        TickNaturalPoiLook();
    }

    private Coroutine _naturalPoiRoutine;

    void TickNaturalPoiLook()
    {
        float interval = GetNaturalPoiIntervalSeconds();
        bool shouldRun =
            currentState == AIState.Natural &&
            poiLookDirector != null &&
            interval > 0f;

        if (shouldRun)
        {
            if (_naturalPoiRoutine == null)
                _naturalPoiRoutine = StartCoroutine(NaturalPoiLookRoutine());
        }
        else
        {
            if (_naturalPoiRoutine != null)
            {
                StopCoroutine(_naturalPoiRoutine);
                _naturalPoiRoutine = null;
            }

            // Ensure we don't leave the rig weight up when leaving Natural.
            if (poiLookDirector != null && currentState != AIState.Chase && currentState != AIState.Capture)
            {
                if (!poiLookDirector.IsLockedToPlayer)
                    poiLookDirector.CancelHoldAndClearLook();
            }
        }
    }

    IEnumerator NaturalPoiLookRoutine()
    {
        while (currentState == AIState.Natural && poiLookDirector != null)
        {
            poiLookDirector.SnapPOIToRandomOrPlayer();

            if (Random.value < GetNaturalLookHoldChance())
                poiLookDirector.TriggerLookHold();

            yield return new WaitForSeconds(GetNaturalPoiIntervalSeconds());
        }

        _naturalPoiRoutine = null;
    }

    float GetNaturalPoiIntervalSeconds()
    {
        var data = interaction != null ? interaction.data : null;
        return data != null ? Mathf.Max(0.01f, data.poiLook.naturalPoiIntervalSeconds) : 5f;
    }

    float GetNaturalLookHoldChance()
    {
        var data = interaction != null ? interaction.data : null;
        return data != null ? Mathf.Clamp01(data.poiLook.naturalLookHoldChance) : 0.30f;
    }

    // ========== State helpers ==========

    public void SetState(AIState s)
    {
        if (currentState == s) return;
        currentState = s;
        ApplyLocomotionForState(s);
    }

    private void ApplyLocomotionForState(AIState s)
    {
        if (!agent) return;

        switch (s)
        {
            case AIState.Chase:
                agent.speed = runSpeed;
                agent.acceleration = Mathf.Max(agent.acceleration, runSpeed * 4f);
                break;

            default:
                agent.speed = walkSpeed;
                break;
        }
    }

    // ========== Perception event handlers ==========

    public void SetHearingDisabled(bool disabled)
    {
        HearingDisabled = disabled;
        if (disabled)
            suspicion?.ResetMeters();
    }

    /// <summary>
    /// Drop routine, door routes, and POI — run straight at the player.
    /// Stands up from a hotspot first so he doesn't float-sit toward the player.
    /// </summary>
    public void BeginChaseFromPerception()
    {
        if (currentState == AIState.Chase || currentState == AIState.Capture || currentState == AIState.Dead)
            return;

        // Mid stand-up already headed to Chase (or upgrading Search → Chase).
        if (interaction != null && interaction.IsStandingUpFromHotspot)
        {
            interaction.StandUpFromHotspotThenSetState(AIState.Chase);
            return;
        }

        fsm?.ClearWitnessedHideCapture();
        navigator?.ClearRoute();

        if (poiLookDirector != null)
        {
            poiLookDirector.UnlockLookFromPlayer();
            poiLookDirector.CancelHoldAndClearLook();
        }

        _lastChaseTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
        _nextChaseReplanTime = 0f;

        if (interaction != null
            && interaction.StandUpFromHotspotThenSetState(AIState.Chase))
            return; // Still standing up — chase starts after SetState(Chase).

        if (currentState != AIState.Chase)
            fsm.SetState(AIState.Chase);

        ApplyLocomotionForState(AIState.Chase);
        fsm.TickChase();
    }

    /// <summary>
    /// NPC saw the player enter this hide spot — path there and capture (same as a successful search roll).
    /// Also used by hide-mic alerts in the same room.
    /// </summary>
    public void BeginWitnessedHideCapture(HidingSpot spot)
    {
        if (spot == null)
            return;
        if (currentState == AIState.Capture || currentState == AIState.Dead || currentState == AIState.Scripted)
            return;

        navigator?.ClearRoute();

        if (poiLookDirector != null)
        {
            poiLookDirector.UnlockLookFromPlayer();
            poiLookDirector.CancelHoldAndClearLook();
        }

        // Arm hide-spot lock BEFORE Chase so TickChase cannot proximity-capture mid-path.
        // Pathing waits until Chase + stand-up finish (see WitnessedHideCaptureRoutine).
        fsm.BeginWitnessedHideCapture(spot);

        if (interaction != null
            && interaction.StandUpFromHotspotThenSetState(AIState.Chase))
            return;

        if (currentState != AIState.Chase)
            fsm.SetState(AIState.Chase);

        ApplyLocomotionForState(AIState.Chase);
    }

    private void HandleSuspicionReached()
    {
        if (HearingDisabled) return;
        if (currentState == AIState.Chase || currentState == AIState.Capture || currentState == AIState.Dead) return;
        navigator.ClearRoute();
        interaction?.StandUpFromHotspotThenSetState(AIState.Chase);
    }

    private void HandleVisionConfirmed()
    {
        if (currentState == AIState.Chase || currentState == AIState.Capture
            || currentState == AIState.Dead || currentState == AIState.Scripted)
            return;

        BeginChaseFromPerception();
    }



    // ========== Animation footstep hook (you already had this) ==========
    private float lastFootstepTime = -1f;
    public float footstepCooldown = 0.18f;
    public void PlayNPCFootstep()
    {
        if (Time.time - lastFootstepTime < footstepCooldown) return;
        lastFootstepTime = Time.time;
        // Hook your actual footstep system here if needed
    }
}
