using UnityEngine;
using UnityEngine.AI;
using System.Collections;

public class NPCController : MonoBehaviour
{
    public enum AIState { Natural, Alerted, Suspicious, Search, Chase, Capture, Dead, Patrol, Ambush , Scripted}
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

    [Header("Chase Replan")]
    [Tooltip("Seconds between destination replans during chase.")]
    public float chaseReplanCooldown = 0.25f;
    [Tooltip("Replan if the target moved more than this distance since last plan.")]
    public float chaseReplanDist = 0.75f;

    [Header("Speeds (override from NPCData if present)")]
    [HideInInspector] public float walkSpeed = 2.5f;
    [HideInInspector] public float runSpeed  = 6.0f;

    // internal chase guards
    public Vector3 _lastChaseTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
    public float   _nextChaseReplanTime = 0f;

    // optional: expose for debug
    public Transform jumpscarePoint;

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

        // Perception -> State transitions (event-driven)
        if (perception != null)
        {
            perception.OnVisionAlerted   += HandleVisionAlerted;   // soft sight fill
            perception.OnPlayerSeen      += HandleVisionAlerted;   // alias
            perception.OnVisionConfirmed += HandleVisionConfirmed; // sustained sight -> Chase
        }

        if (suspicion != null)
            suspicion.OnSuspicionThresholdReached += HandleSuspicionReached;
    }

    void Start()
    {
        fsm.SetState(AIState.Scripted);
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
                poiLookDirector.CancelHoldAndClearLook();
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

    private void HandleVisionAlerted()
    {
        if (currentState == AIState.Chase || currentState == AIState.Capture || currentState == AIState.Dead) return;
        interaction?.CancelAndExitGracefully(AIState.Chase);
    }

    private void HandleSuspicionReached()
    {
        if (currentState == AIState.Chase || currentState == AIState.Capture || currentState == AIState.Dead) return;
        navigator.ClearRoute();
        interaction?.CancelAndExitGracefully(AIState.Chase);
    }

    private void HandleVisionConfirmed()
    {
        if (currentState == AIState.Capture || currentState == AIState.Dead || currentState == AIState.Scripted) return;

        // Leave any current activity and switch to Chase
        interaction?.CancelAndExitGracefully(AIState.Chase);

        // Ensure locomotion is correct for chase
        ApplyLocomotionForState(AIState.Chase);

        fsm.TickChase();
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
