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

    [Tooltip("After the player leaves the cage, Natural routines wait this long before the butcher may pick/enter the basement. Chase from noise is not blocked.")]
    [SerializeField] float basementRoutineDelaySeconds = 30f;

    [Tooltip("After Natural picks the player's current room: once the butcher physically leaves that room, he cannot pick it again for this long.")]
    [SerializeField] float playerRoomPickCooldownSeconds = 30f;

    /// <summary>Room he Natural-picked while the player was there — cooldown starts when he leaves it.</summary>
    string _pendingLeaveCooldownRoomId;
    /// <summary>True after the butcher has entered <see cref="_pendingLeaveCooldownRoomId"/> (so we don't start cooldown while still walking there).</summary>
    bool _enteredPendingCooldownRoom;
    /// <summary>Room blocked until <see cref="_roomCooldownUntil"/>.</summary>
    string _cooldownRoomId;
    float _roomCooldownUntil = -1f;

    /// <summary>True until the player leaves the cage — countdown has not started yet.</summary>
    static bool s_basementGracePendingCageExit;
    /// <summary>
    /// Continue day-reset: keep basement blocked through the intro cinematic, then arm the
    /// normal leave-cage gate on unlock (intro must not eat the grace, and spawn-outside must
    /// not start the 30s early).
    /// </summary>
    static bool s_basementGraceHoldForManualStart;
    /// <summary>When the post-exit countdown ends. &lt;= 0 means not counting.</summary>
    static float s_basementRoutineUnlockTime = -1f;
    static float s_basementGraceDurationSeconds = 30f;
    static int s_basementDelaySceneHandle = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBasementGraceStatics()
    {
        s_basementGracePendingCageExit = false;
        s_basementGraceHoldForManualStart = false;
        s_basementRoutineUnlockTime = -1f;
        s_basementGraceDurationSeconds = 30f;
        s_basementDelaySceneHandle = -1;
    }

    /// <summary>
    /// True while the butcher must not choose / enter the basement:
    /// still in cage (timer not started), held for Continue intro, or within the post-exit grace window.
    /// </summary>
    public static bool IsBasementRoutineDelayed =>
        s_basementGracePendingCageExit
        || s_basementGraceHoldForManualStart
        || (s_basementRoutineUnlockTime > 0f && Time.time < s_basementRoutineUnlockTime);

    /// <summary>
    /// Arm basement block like thunder's cage gate: do not start the timer until the player
    /// leaves the cage. If they are already outside, the countdown begins immediately.
    /// </summary>
    public static void ArmBasementGraceUntilCageExit(float seconds)
    {
        s_basementGraceDurationSeconds = Mathf.Max(0f, seconds);
        s_basementGracePendingCageExit = true;
        s_basementGraceHoldForManualStart = false;
        s_basementRoutineUnlockTime = -1f;
        TryStartBasementGraceCountdownIfPlayerOutsideCage();
    }

    /// <summary>
    /// Continue: block basement through the replayed intro, then call
    /// <see cref="ArmBasementGraceAfterContinueIntro"/> when the cinematic unlocks
    /// (same leave-cage countdown as a fresh start).
    /// </summary>
    public static void HoldBasementGraceUntilManualStart(float seconds)
    {
        s_basementGraceDurationSeconds = Mathf.Max(0f, seconds);
        s_basementGracePendingCageExit = false;
        s_basementGraceHoldForManualStart = true;
        s_basementRoutineUnlockTime = -1f;
    }

    /// <summary>
    /// After Continue intro unlock: same as a new game — wait until the player leaves the cage,
    /// then start the countdown. If they are already outside, the countdown starts now.
    /// </summary>
    public static void ArmBasementGraceAfterContinueIntro()
    {
        if (!s_basementGraceHoldForManualStart)
            return;

        float seconds = s_basementGraceDurationSeconds;
        // Clears the Continue hold, then arms the normal cage-exit gate.
        ArmBasementGraceUntilCageExit(seconds);
    }

    /// <summary>Obsolete name — use <see cref="ArmBasementGraceAfterContinueIntro"/>.</summary>
    public static void TryBeginBasementGraceAfterContinueIntro() =>
        ArmBasementGraceAfterContinueIntro();

    /// <summary>Legacy name — same as <see cref="ArmBasementGraceUntilCageExit"/>.</summary>
    public static void RestartBasementRoutineDelay(float seconds) =>
        ArmBasementGraceUntilCageExit(seconds);

    /// <summary>Called when the player exits a cage trigger — starts the post-exit countdown once.</summary>
    public static void NotifyPlayerLeftCage()
    {
        // Continue intro owns the block until UnlockCinematic arms the normal cage-exit gate.
        if (s_basementGraceHoldForManualStart)
            return;

        if (!s_basementGracePendingCageExit)
            return;

        s_basementGracePendingCageExit = false;
        s_basementRoutineUnlockTime = Time.time + s_basementGraceDurationSeconds;
    }

    static void TryStartBasementGraceCountdownIfPlayerOutsideCage()
    {
        if (s_basementGraceHoldForManualStart)
            return;

        if (!s_basementGracePendingCageExit)
            return;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return;

        if (!Cage.IsPlayerInsideAnyCage(player.transform))
            NotifyPlayerLeftCage();
    }

    public float BasementRoutineDelaySeconds => basementRoutineDelaySeconds;

    bool TryGetPlayerRoomAnchorId(out string roomAnchorId)
    {
        roomAnchorId = null;
        if (data == null || NPCAnchorManager.Instance == null)
            return false;

        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
            return false;

        Transform anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(
            player.transform.position,
            data.homeID,
            NavMesh.AllAreas,
            isNpcSelf: false);
        if (anchor == null)
            return false;

        var room = anchor.GetComponent<RoomAnchor>();
        roomAnchorId = room != null
            ? (string.IsNullOrEmpty(room.roomID) ? room.name : room.roomID)
            : anchor.name;
        return !string.IsNullOrEmpty(roomAnchorId);
    }

    string GetActiveCooldownRoomId()
    {
        if (playerRoomPickCooldownSeconds <= 0f
            || string.IsNullOrEmpty(_cooldownRoomId)
            || Time.time >= _roomCooldownUntil)
            return null;
        return _cooldownRoomId;
    }

    static string RoomIdOf(RoomAnchor room)
    {
        if (room == null) return null;
        return string.IsNullOrEmpty(room.roomID) ? room.name : room.roomID;
    }

    void TickPlayerRoomLeaveCooldown()
    {
        if (playerRoomPickCooldownSeconds <= 0f || string.IsNullOrEmpty(_pendingLeaveCooldownRoomId))
            return;

        string butcherRoomId = RoomIdOf(navigator != null ? navigator.GetCurrentRoom() : null);
        if (string.IsNullOrEmpty(butcherRoomId))
            return;

        if (butcherRoomId == _pendingLeaveCooldownRoomId)
        {
            _enteredPendingCooldownRoom = true;
            return;
        }

        // Only start the timer after he actually entered that room, then left it.
        if (!_enteredPendingCooldownRoom)
            return;

        _cooldownRoomId = _pendingLeaveCooldownRoomId;
        _roomCooldownUntil = Time.time + playerRoomPickCooldownSeconds;
        _pendingLeaveCooldownRoomId = null;
        _enteredPendingCooldownRoom = false;
    }

    void MarkPendingLeaveCooldownIfPickedPlayerRoom(string pickedRoomId)
    {
        if (playerRoomPickCooldownSeconds <= 0f || string.IsNullOrEmpty(pickedRoomId))
            return;

        if (!TryGetPlayerRoomAnchorId(out string playerRoomId))
            return;

        if (pickedRoomId != playerRoomId)
            return;

        // He picked the room the player is in — cooldown starts when he physically leaves it.
        _pendingLeaveCooldownRoomId = pickedRoomId;
        // Already standing in that room (e.g. another hotspot) → leave can start immediately later.
        string butcherRoomId = RoomIdOf(navigator != null ? navigator.GetCurrentRoom() : null);
        _enteredPendingCooldownRoom = butcherRoomId == pickedRoomId;
    }

    /// <summary>Continue / new life — clear leave-pending and active room cooldown so the last run does not stick.</summary>
    public void ClearPlayerRoomPickCooldown()
    {
        _pendingLeaveCooldownRoomId = null;
        _enteredPendingCooldownRoom = false;
        _cooldownRoomId = null;
        _roomCooldownUntil = -1f;
    }

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
            ArmBasementGraceUntilCageExit(basementRoutineDelaySeconds);
        }
    }

    void Update()
    {
        // Intro / Continue can arm grace before the player tag exists; keep checking until exit.
        if (s_basementGracePendingCageExit)
            TryStartBasementGraceCountdownIfPlayerOutsideCage();

        TickPlayerRoomLeaveCooldown();
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
        isCancelled = false;
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

        // During basement grace, don't even roll a zone that only has basement rooms.
        if (IsBasementRoutineDelayed)
        {
            var zonesWithUpstairs = boostedZones
                .Where(z => z.roomPlans != null
                    && z.roomPlans.Any(r => r.roomAnchorID != BasementExcludedRoom))
                .ToList();
            if (zonesWithUpstairs.Count > 0)
                boostedZones = zonesWithUpstairs;
        }

        string blockedRoomId = GetActiveCooldownRoomId();
        if (!string.IsNullOrEmpty(blockedRoomId))
        {
            var zonesAwayFromBlocked = boostedZones
                .Where(z => z.roomPlans != null
                    && z.roomPlans.Any(r => r.roomAnchorID != blockedRoomId))
                .ToList();
            if (zonesAwayFromBlocked.Count > 0)
                boostedZones = zonesAwayFromBlocked;
        }

        currentZonePlan = WeightedPicker.Pick(boostedZones, z => z.weight);

        List<WeightedRoomPlan> roomOptions = currentZonePlan.roomPlans;
        if (IsBasementRoutineDelayed)
        {
            var withoutBasement = roomOptions
                .Where(r => r.roomAnchorID != BasementExcludedRoom)
                .ToList();
            if (withoutBasement.Count > 0)
                roomOptions = withoutBasement;
            else
            {
                // Avoid targeting basement during grace (navigator would abort and used to softlock).
                StartCoroutine(RetryPickNextRoutineDelayed());
                return;
            }
        }

        if (!string.IsNullOrEmpty(blockedRoomId))
        {
            var withoutBlocked = roomOptions
                .Where(r => r.roomAnchorID != blockedRoomId)
                .ToList();
            if (withoutBlocked.Count > 0)
                roomOptions = withoutBlocked;
            else
            {
                StartCoroutine(RetryPickNextRoutineDelayed());
                return;
            }
        }

        currentRoomPlan = WeightedPicker.Pick(roomOptions, r => r.weight);

        // Abandoned the player-room trip before arriving — no leave, no cooldown.
        if (!string.IsNullOrEmpty(_pendingLeaveCooldownRoomId)
            && !_enteredPendingCooldownRoom
            && currentRoomPlan.roomAnchorID != _pendingLeaveCooldownRoomId)
        {
            _pendingLeaveCooldownRoomId = null;
        }

        // Picked the player's room → cooldown starts when he physically leaves that room.
        MarkPendingLeaveCooldownIfPickedPlayerRoom(currentRoomPlan.roomAnchorID);

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

    /// <summary>
    /// Navigator aborted a Natural route (e.g. basement blocked during grace). Re-pick so he doesn't idle forever.
    /// </summary>
    public void NotifyRouteAbortedNeedNewRoutine()
    {
        if (npc != null && npc.currentState != NPCController.AIState.Natural)
            return;

        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        IsPerformingHotspotAction = false;
        isCancelled = false;
        StartNextRoutine();
    }

    private IEnumerator RetryPickNextRoutineDelayed()
    {
        yield return null;
        PickNextWeightedRoutine();
    }

    private IEnumerator WaitForArrival(Transform hotspotTransform)
    {
        // Sit anchors often sit on furniture off the NavMesh — landing can be >2m from the transform.
        const float hotspotArriveRadius = 3.25f;
        const float nearAndStillSeconds = 0.2f;
        const float pathAbortedFarSeconds = 4f;
        const float noProgressTimeoutSeconds = 25f;
        const float nudgeCooldownSeconds = 2.5f;

        float nearStillTimer = 0f;
        float stuckFarTimer = 0f;
        float noProgressTimer = 0f;
        float nudgeCooldown = 0f;
        float bestDist = float.PositiveInfinity;
        bool didNudge = false;

        while (true)
        {
            // Chase / capture / etc. took over — don't sit or softlock Natural arrival.
            if (npc != null && npc.currentState != NPCController.AIState.Natural)
                yield break;

            if (agent == null)
                yield break;

            if (nudgeCooldown > 0f)
                nudgeCooldown -= Time.deltaTime;

            float toHotspot = hotspotTransform != null
                ? Vector3.Distance(transform.position, hotspotTransform.position)
                : 0f;

            bool nearHotspot = toHotspot <= hotspotArriveRadius;
            float stopDist = Mathf.Max(0.15f, agent.stoppingDistance);
            bool pathSettled = !agent.pathPending && agent.remainingDistance <= stopDist;
            bool mostlyStill = agent.velocity.sqrMagnitude < 0.08f;

            // Prefer a real path settle near the hotspot (old reliable sit start).
            if (nearHotspot && pathSettled)
                break;

            // remainingDistance is unreliable near the goal — accept near + mostly still.
            if (nearHotspot && mostlyStill)
            {
                nearStillTimer += Time.deltaTime;
                if (nearStillTimer >= nearAndStillSeconds)
                    break;
            }
            else
            {
                nearStillTimer = 0f;
            }

            // True abort: path wiped / invalid while still far (door cancel, grace abort).
            // Do NOT treat "remainingDistance ~0" alone as abort — that is normal after
            // Navigator finishes the last waypoint, even when the sit transform is further away.
            bool pathReallyGone = !agent.pathPending
                && (!agent.hasPath || agent.pathStatus == NavMeshPathStatus.PathInvalid);

            // Finished walking but still outside sit radius — nudge onto the hotspot (furniture off-mesh).
            bool finishedButFar = !nearHotspot && pathSettled
                && hotspotTransform != null
                && nudgeCooldown <= 0f;

            if (finishedButFar)
            {
                nudgeCooldown = nudgeCooldownSeconds;
                agent.SetDestination(hotspotTransform.position);
                didNudge = true;
                stuckFarTimer = 0f;
            }
            else if (pathReallyGone && !nearHotspot)
            {
                if (!didNudge && hotspotTransform != null && nudgeCooldown <= 0f)
                {
                    didNudge = true;
                    nudgeCooldown = nudgeCooldownSeconds;
                    agent.SetDestination(hotspotTransform.position);
                    stuckFarTimer = 0f;
                }
                else
                {
                    stuckFarTimer += Time.deltaTime;
                    if (stuckFarTimer >= pathAbortedFarSeconds)
                    {
                        Debug.LogWarning(
                            $"[InteractionController] Arrival softlock ({toHotspot:0.0}m from hotspot) — re-picking Natural.");
                        interactionRoutine = null;
                        StartNextRoutine();
                        yield break;
                    }
                }
            }
            else
            {
                stuckFarTimer = 0f;
            }

            if (toHotspot < bestDist - 0.1f)
            {
                bestDist = toHotspot;
                noProgressTimer = 0f;
            }
            else
            {
                noProgressTimer += Time.deltaTime;
                if (noProgressTimer >= noProgressTimeoutSeconds)
                {
                    Debug.LogWarning("[InteractionController] Hotspot walk made no progress — re-picking Natural.");
                    interactionRoutine = null;
                    StartNextRoutine();
                    yield break;
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
                if (isCancelled)
                {
                    IsPerformingHotspotAction = false;
                    interactionRoutine = null;
                    yield break;
                }

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
            if (isCancelled)
            {
                IsPerformingHotspotAction = false;
                interactionRoutine = null;
                yield break;
            }
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

    /// <summary>
    /// Pending AI state while we abort a sit/hotspot and put the visual root back on the agent.
    /// Non-null means <see cref="StandUpFromHotspotThenSetState"/> is in progress.
    /// </summary>
    NPCController.AIState? _stateAfterStandUp;

    /// <summary>
    /// True while leaving a seated hotspot (stand-up anim + visual lerp) before the AI state changes.
    /// </summary>
    public bool IsStandingUpFromHotspot => _stateAfterStandUp.HasValue;

    /// <summary>
    /// Abort the current hotspot (bed/sofa/etc.), stand up and lerp the visual root back onto the
    /// NavMeshAgent, THEN change AI state. Use this instead of <see cref="CancelInteraction"/> +
    /// SetState when he might be sitting — otherwise the agent runs while the mesh stays in the
    /// sit pose (float-sit toward the player).
    /// </summary>
    /// <returns>True if stand-up is still running (do not start chase movement yet).</returns>
    public bool StandUpFromHotspotThenSetState(NPCController.AIState nextState)
    {
        if (_stateAfterStandUp.HasValue)
        {
            // Already standing up — keep the more urgent destination (e.g. Search → Chase).
            _stateAfterStandUp = PreferStateAfterStandUp(_stateAfterStandUp.Value, nextState);
            return true;
        }

        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }

        if (IsPerformingHotspotAction || hasVisualOffset)
        {
            isCancelled = true;
            _stateAfterStandUp = nextState;
            interactionRoutine = StartCoroutine(StandUpFromHotspotThenSetStateRoutine());
            return true;
        }

        CancelInteraction();
        npc.fsm.SetState(nextState);
        return false;
    }

    static NPCController.AIState PreferStateAfterStandUp(
        NPCController.AIState current, NPCController.AIState incoming)
    {
        int Rank(NPCController.AIState s)
        {
            switch (s)
            {
                case NPCController.AIState.Capture: return 3;
                case NPCController.AIState.Chase: return 2;
                case NPCController.AIState.Search: return 1;
                default: return 0;
            }
        }

        return Rank(incoming) >= Rank(current) ? incoming : current;
    }

    IEnumerator StandUpFromHotspotThenSetStateRoutine()
    {
        if (navigator != null)
            navigator.ClearRoute();

        if (agent != null)
        {
            agent.ResetPath();
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        if (hasVisualOffset)
        {
            if (animator != null)
                animator.SetTrigger("StandUp");
            yield return InterpolateBackToStandingPosition();
        }

        IsPerformingHotspotAction = false;
        if (animator != null)
            animator.SetInteger("Behaviour", -1);

        NPCController.AIState nextState = _stateAfterStandUp
            ?? NPCController.AIState.Natural;
        _stateAfterStandUp = null;
        interactionRoutine = null;
        npc.fsm.SetState(nextState);
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
        _stateAfterStandUp = null;
        if (interactionRoutine != null)
        {
            StopCoroutine(interactionRoutine);
            interactionRoutine = null;
        }
        IsPerformingHotspotAction = false;
        if (animator != null)
            animator.SetInteger("Behaviour", -1);

        // Hard cancel without stand-up left him offset on the seat forever in Natural.
        SnapVisualRootToAgent();
    }

    void SnapVisualRootToAgent()
    {
        if (!hasVisualOffset || visualRoot == null)
            return;

        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.identity;
        hasVisualOffset = false;
    }
}