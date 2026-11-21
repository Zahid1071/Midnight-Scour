using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Maps the shared NPC AI (Speed / Chase / Capture) onto the Zombie Dog animator
/// (Awake, Speed, Bark, Attack) without changing the butcher controller.
/// </summary>
[RequireComponent(typeof(NPCController))]
[DisallowMultipleComponent]
public class DogAnimatorBridge : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] NavMeshAgent agent;
    [SerializeField] NPCController npc;

    [Tooltip("Agent speed at/above this drives the Run blend (matches ZombieDog.controller).")]
    [SerializeField] float runSpeedThreshold = 3f;

    [SerializeField] float barkHoldSeconds = 1.25f;

    NPCController.AIState _lastState;
    float _barkUntil = -1f;
    bool _hasAwake;
    bool _hasBark;
    bool _hasAttack;
    bool _hasSpeed;

    void Awake()
    {
        if (!npc) npc = GetComponent<NPCController>();
        if (!agent) agent = GetComponent<NavMeshAgent>();
        if (!animator) animator = GetComponentInChildren<Animator>();

        CacheParams();
        _lastState = npc != null ? npc.currentState : NPCController.AIState.Natural;
    }

    void Start()
    {
        // Leave sleep as soon as AI owns the dog (Natural / Chase / etc.).
        if (_hasAwake)
            animator.SetBool("Awake", true);
    }

    void CacheParams()
    {
        _hasAwake = _hasBark = _hasAttack = _hasSpeed = false;
        if (animator == null) return;

        for (int i = 0; i < animator.parameterCount; i++)
        {
            string n = animator.GetParameter(i).name;
            if (n == "Awake") _hasAwake = true;
            else if (n == "Bark") _hasBark = true;
            else if (n == "Attack") _hasAttack = true;
            else if (n == "Speed") _hasSpeed = true;
        }
    }

    void LateUpdate()
    {
        if (animator == null || npc == null)
            return;

        var state = npc.currentState;
        if (state != _lastState)
        {
            OnStateChanged(_lastState, state);
            _lastState = state;
        }

        bool sleepingHotspot = npc.interaction != null
            && npc.interaction.IsPerformingHotspotAction
            && state == NPCController.AIState.Natural
            && animator.GetBool("Awake") == false;

        if (_hasAwake && !sleepingHotspot
            && state != NPCController.AIState.Dead)
        {
            // Keep awake while hunting / walking; InteractionController owns Nap → Awake false.
            if (state == NPCController.AIState.Chase
                || state == NPCController.AIState.Capture
                || state == NPCController.AIState.Search
                || state == NPCController.AIState.Suspicious
                || (agent != null && agent.velocity.sqrMagnitude > 0.05f))
            {
                animator.SetBool("Awake", true);
            }
        }

        if (_hasBark)
            animator.SetBool("Bark", Time.time < _barkUntil);

        // InteractionController also writes Speed; clamp run blend for the dog graph.
        if (_hasSpeed && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            float spd = agent.velocity.magnitude;
            if (state == NPCController.AIState.Chase || state == NPCController.AIState.Capture)
                spd = Mathf.Max(spd, runSpeedThreshold + 0.1f);
            animator.SetFloat("Speed", spd);
        }
    }

    void OnStateChanged(NPCController.AIState prev, NPCController.AIState next)
    {
        if (next == NPCController.AIState.Chase
            || (next == NPCController.AIState.Search && prev == NPCController.AIState.Natural))
        {
            if (_hasAwake) animator.SetBool("Awake", true);
            _barkUntil = Time.time + barkHoldSeconds;
        }

        if (next == NPCController.AIState.Capture && _hasAttack)
            animator.SetTrigger("Attack");
    }
}
