using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FS_Atmo;

public class InteractionController : MonoBehaviour
{
    public NPCData data;
    public NavMeshAgent agent;

    private Coroutine interactionRoutine;

    private WeightedZonePlan currentZonePlan;
    private WeightedRoomPlan currentRoomPlan;
    private WeightedHotspot currentHotspot;
    private string lastZoneID = null;
    private string lastHotspotID = null;

    const string BasementExcludedRoom = "Basement_Room";

    [Tooltip("NPC routine will not pick the basement room until this many seconds after the scene starts.")]
    [SerializeField] float basementRoutineDelaySeconds = 30f;

    static float s_basementRoutineUnlockTime = -1f;
    static int s_basementDelaySceneHandle = -1;

    public float remainHomeFactor = 1f;

    public enum MovementState { Idle, Walking, Running }
    public MovementState currentMovementState;

    private Animator animator;
    private float animSpeed;
    public bool IsPerformingHotspotAction;

    [SerializeField]
    private Transform visualRoot;

    [Header("Interpolation Settings")]
    public AnimationCurve interpolationCurveEnter = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve interpolationCurveExit = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float interpolationDurationEnter;
    public float interpolationDurationExit;
    public float rotationDuration = 0.4f;

    private NPCController npc;
    private bool hasVisualOffset = false;
    private bool isCancelled = false;
    private Navigator navigator;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        npc = GetComponent<NPCController>();
        navigator = GetComponent<Navigator>();

        int sceneHandle = gameObject.scene.handle;
        if (s_basementDelaySceneHandle != sceneHandle)
        {
            s_basementDelaySceneHandle = sceneHandle;
            s_basementRoutineUnlockTime = Time.time + basementRoutineDelaySeconds;
        }
    }

    void Update()
    {
        SetCurrentMovementState();
        UpdateAnimation();
    }

    private void SetCurrentMovementState()
    {
        float velocity = agent.velocity.magnitude;

        if (agent.pathPending || agent.remainingDistance > 0.1f)
        {
            if (velocity > 2.6f)
                currentMovementState = MovementState.Running;
            else
                currentMovementState = MovementState.Walking;
        }
        else
        {
            currentMovementState = MovementState.Idle;
        }
    }

    private void UpdateAnimation()
    {
        if (animator == null || agent == null) return;

        float targetSpeed = IsPerformingHotspotAction ? 0f : agent.velocity.magnitude;
        animSpeed = Mathf.Lerp(animSpeed, targetSpeed, Time.deltaTime * 6f);

        if (animSpeed < 0.01f) animSpeed = 0f;
        animator.SetFloat("Speed", animSpeed);
    }

    public void StartNextRoutine()
    {
        PickNextWeightedRoutine();
    }

    private void PickNextWeightedRoutine()
    {
        List<WeightedZonePlan> boostedZones = new List<WeightedZonePlan>();

        foreach (var zone in data.weightedRoutinePlans)
        {
            float boostFactor = 1f;
            if (zone.zoneID == lastZoneID)
            {
                boostFactor = (zone.zoneID == data.homeID) ? remainHomeFactor : remainHomeFactor * 0.8f;
            }

            boostedZones.Add(new WeightedZonePlan
            {
                zoneID = zone.zoneID,
                weight = zone.weight * boostFactor,
                roomPlans = zone.roomPlans
            });
        }

        currentZonePlan = WeightedPicker.Pick(boostedZones, z => z.weight);

        List<WeightedRoomPlan> roomOptions = currentZonePlan.roomPlans;
        if (Time.time < s_basementRoutineUnlockTime)
        {
            var withoutBasement = roomOptions
                .Where(r => r.roomAnchorID != BasementExcludedRoom)
                .ToList();
            if (withoutBasement.Count > 0)
                roomOptions = withoutBasement;
        }

        currentRoomPlan = WeightedPicker.Pick(roomOptions, r => r.weight);

        // Avoid repeating the last hotspot if possible
        List<WeightedHotspot> possibleHotspots = currentRoomPlan.hotspots
            .Where(h => h.hotspotID != lastHotspotID)
            .ToList();

        if (possibleHotspots.Count == 0)
        {
            StartCoroutine(RetryPickNextRoutineDelayed());
            return;
        }

        currentHotspot = WeightedPicker.Pick(possibleHotspots, h => h.weight);

        lastZoneID = currentZonePlan.zoneID;
        lastHotspotID = currentHotspot.hotspotID;

        Transform hotspotTransform = NPCAnchorManager.Instance.GetAnchor(currentHotspot.hotspotID);

        if (hotspotTransform != null)
        {
            string parentName = hotspotTransform.parent != null ? hotspotTransform.parent.name : null;
            string destinationName = hotspotTransform.name;
            navigator.GoToPoint(hotspotTransform.position, destinationName, parentName);
        }

        if (interactionRoutine != null)
            StopCoroutine(interactionRoutine);

        interactionRoutine = StartCoroutine(WaitForArrival(hotspotTransform));
        //Debug.Log(hotspotTransform);
    }

    private IEnumerator RetryPickNextRoutineDelayed()
    {
        yield return null;
        PickNextWeightedRoutine();
    }

    private IEnumerator WaitForArrival(Transform hotspotTransform)
    {
        const float hotspotArriveRadius = 2f; // tweak if needed
        while (true)
        {
            if (!agent.pathPending &&   agent.remainingDistance <= agent.stoppingDistance)
            {
                // Only accept arrival if we're actually close to the hotspot,
                // not just some intermediate waypoint (door prepoint).
                if (hotspotTransform == null ||  Vector3.Distance(transform.position, hotspotTransform.position) <= hotspotArriveRadius)
                {
                    break;
                }
            }
            yield return null;
        }

        StartInteraction(currentHotspot);
    }


    private int interactionInstanceID = 0;
    public void StartInteraction(WeightedHotspot hotspot)
    {
        interactionInstanceID++;
        int thisInstance = interactionInstanceID;

        //Debug.Log($"[StartInteraction] ID={thisInstance}");
        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }
        interactionRoutine = StartCoroutine(PerformInteraction(hotspot, thisInstance));
    }

    private IEnumerator PerformInteraction(WeightedHotspot hotspot, int thisInstance)
    {
        isCancelled = false;
        IsPerformingHotspotAction = true;

        Transform anchor = NPCAnchorManager.Instance.GetAnchor(hotspot.hotspotID);
        Transform actionPoint = anchor != null ? anchor.GetChild(0) : null;

        if (hotspot.requiresVisualOffset && actionPoint != null && visualRoot != null)
        {
            hasVisualOffset = true;

            float t = 0f;
            Vector3 startPos = visualRoot.position;
            Vector3 targetPos = actionPoint.position;
            Quaternion startRot = transform.root.rotation;
            Quaternion targetRot = actionPoint.rotation;

            PlayHotspotAnimation(hotspot);

            while (t < interpolationDurationEnter)
            {
                if (isCancelled) yield break;

                t += Time.deltaTime;
                float lerpT = interpolationCurveEnter.Evaluate(t / interpolationDurationEnter);
                visualRoot.position = Vector3.Lerp(startPos, targetPos, lerpT);
                transform.root.rotation = Quaternion.Lerp(startRot, targetRot, t / rotationDuration);
                yield return null;
            }

            visualRoot.position = targetPos;
            transform.root.rotation = targetRot;
        }
        else
        {
            PlayHotspotAnimation(hotspot);
        }

        float remainingSitTime = Mathf.Max(0f, hotspot.duration - interpolationDurationEnter);
        float timer = 0f;

        while (timer < remainingSitTime)
        {
            if (isCancelled) yield break;
            timer += Time.deltaTime;
            yield return null;
        }

        if (!isCancelled && hotspot.requiresVisualOffset)
        {
            animator.SetTrigger("StandUp");
            yield return InterpolateBackToStandingPosition();
        }

        IsPerformingHotspotAction = false;
        animator.SetInteger("Behaviour", -1);
        interactionRoutine = null;

        StartNextRoutine();
        
    }

    private IEnumerator InterpolateBackToStandingPosition()
    {
        if (!hasVisualOffset) yield break;

        float t = 0f;
        Vector3 startPos = visualRoot.position;
        Vector3 endPos = transform.root.position;

        while (t < interpolationDurationExit)
        {
            t += Time.deltaTime;
            float lerpT = interpolationCurveExit.Evaluate(t / interpolationDurationExit);
            visualRoot.position = Vector3.Lerp(startPos, endPos, lerpT);
            yield return null;
        }

        visualRoot.position = transform.root.position;
        visualRoot.rotation = transform.root.rotation;
        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.identity;

        hasVisualOffset = false;
    }

    private IEnumerator GracefulExitRoutine(NPCController.AIState nextState)
    {
        if (hasVisualOffset)
        {
            animator.SetTrigger("StandUp");
            yield return InterpolateBackToStandingPosition();
        }

        interactionRoutine = null;
        IsPerformingHotspotAction = false;
        animator.SetInteger("Behaviour", -1);
        npc.fsm.SetState(nextState);
    }

    public void CancelAndExitGracefully(NPCController.AIState nextState)
    {
        if (IsPerformingHotspotAction && interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = StartCoroutine(GracefulExitRoutine(nextState));
        }
        else
        {
            CancelInteraction();
            npc.fsm.SetState(nextState);
        }
    }

    private void PlayHotspotAnimation(WeightedHotspot hotspot)
    {
        if (animator == null) return;

        int baseBehaviour = (int)hotspot.actionType;

        animator.SetInteger("Behaviour", baseBehaviour);
    }

    public bool IsBusyWithRoutine()
    {
        return interactionRoutine != null || IsPerformingHotspotAction;
    }

    public void CancelInteraction()
    {
        isCancelled = true;
        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }
        IsPerformingHotspotAction = false;
        animator.SetInteger("Behaviour", -1);
    }
}