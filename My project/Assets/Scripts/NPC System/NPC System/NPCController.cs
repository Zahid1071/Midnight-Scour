using UnityEngine;
using System;
using UnityEngine.AI;

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

    [Header("Capture")]
    [Tooltip("Distance at which the NPC instantly captures the player while chasing.")]
    public float captureDistance = 1.8f;

    [Header("Chase Replan")]
    [Tooltip("Seconds between destination replans during chase.")]
    public float chaseReplanCooldown = 0.25f;
    [Tooltip("Replan if the target moved more than this distance since last plan.")]
    public float chaseReplanDist = 0.75f;

    [Header("Speeds (override from NPCData if present)")]
    public float walkSpeed = 2.5f;
    public float runSpeed  = 6.0f;

    // internal chase guards
    public Vector3 _lastChaseTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
    public float   _nextChaseReplanTime = 0f;

    // optional: expose for debug
    public Transform jumpscarePoint;

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
