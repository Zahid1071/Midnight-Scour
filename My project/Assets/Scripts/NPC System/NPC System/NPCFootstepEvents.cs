using UnityEngine;
using UnityEngine.AI;

public class NPCFootstepEvent : MonoBehaviour
{
    public FootstepController footstepController;

    [Tooltip("Velocity above this uses run clips/volume/reverb.")]
    [Min(0f)]
    [SerializeField] private float runSpeedThreshold = 3.25f;

    [Tooltip("When true, Chase AI state always uses run footstep treatment.")]
    [SerializeField] private bool forceRunClipsDuringChase = true;

    NavMeshAgent _agent;
    NPCController _npc;

    void Awake()
    {
        _agent = GetComponentInParent<NavMeshAgent>();
        _npc = GetComponentInParent<NPCController>();
    }

    public void PlayFootstepAnimEvent()
    {
        if (footstepController == null)
            return;

        bool isRunning = IsRunningLocomotion();
        float volume = isRunning ? footstepController.runVolume : footstepController.walkVolume;
        footstepController.PlayFootstep(volume, isRunning);
    }

    bool IsRunningLocomotion()
    {
        if (forceRunClipsDuringChase && _npc != null && _npc.currentState == NPCController.AIState.Chase)
            return true;

        if (_agent == null)
            _agent = GetComponentInParent<NavMeshAgent>();

        return _agent != null && _agent.velocity.magnitude > runSpeedThreshold;
    }
}
