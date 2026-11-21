using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using FS_Atmo;
using System.Collections.Generic;


public class FSMController
{
    private NPCController npc;
    private Animator animator;
    private Navigator nav; // <-- door-aware pathing

    NavMeshAgent agent;

    float walkingSpeed = 2.5f;   // cached from SO at first Update
    float runningSpeed = 6f;
    bool locomotionCached;

    Coroutine _searchRoutineCo;
    Coroutine _ambushRoutineCo;
    Coroutine _witnessedHideCaptureCo;
    Coroutine _cageEscapeReactionCo;
    HidingSpot _witnessedHideSpot;
    bool _relentlessViseMissHunt;
    bool _skipNextSearchStartupDelay;
    bool _urgentSearchRun;
    float _chaseLostStillTimer;

    public bool IsRelentlessViseMissHunt => _relentlessViseMissHunt;

    /// <summary>
    /// Chase/Search bed stays on while ANY NPC is hunting — avoids cutting music when one
    /// butcher drops chase while another is still on you.
    /// </summary>
    public static void RefreshChaseMusicFromAllNpcs()
    {
        if (SoundManager.Instance == null)
            return;

        bool tension = false;
#if UNITY_2023_1_OR_NEWER
        var npcs = Object.FindObjectsByType<NPCController>(FindObjectsSortMode.None);
#else
        var npcs = Object.FindObjectsOfType<NPCController>();
#endif
        for (int i = 0; i < npcs.Length; i++)
        {
            var n = npcs[i];
            if (n == null)
                continue;
            var s = n.currentState;
            if (s == NPCController.AIState.Chase || s == NPCController.AIState.Search)
            {
                tension = true;
                break;
            }
        }

        SoundManager.EnsureClipReady(SoundManager.Instance.chaseMusic);
        SoundManager.Instance.SetChaseMusicActive(tension);
    }

    Transform _lastAmbushSpot;
    int _consecutiveSameAmbushCount;

    Vector3 _ambushWaitPosition;
    bool _ambushAtPost;
    bool _ambushRustleArmed;
    bool _ambushRustlePlayed;
    float _ambushRustleDelay;
    float _ambushRustleElapsed;

    /// <summary>
    /// Room he committed to for this ambush. Sticky from Ambush enter until he leaves that room
    /// (even after Ambush → Natural), so captures in that window read as ambush — not noise.
    /// </summary>
    RoomAnchor _ambushThreatRoom;

    public FSMController(NPCController controller)
    {
        npc = controller;
        animator = npc.transform.root.GetComponentInChildren<Animator>();
        nav = npc.GetComponent<Navigator>();
        agent = npc.GetComponent<NavMeshAgent>();
    }

    void EnsureLocomotion()
    {
        if (locomotionCached) return;
        var data = npc.interaction?.data;
        if (data != null)
        {
            walkingSpeed = data.locomotion.walkingSpeed;
            runningSpeed = data.locomotion.runningSpeed;
            locomotionCached = true;
        }
    }

    void StopAgentMovement()
    {
        nav?.ClearRoute();

        if (agent == null)
            return;

        agent.ResetPath();
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }

    public void SetState(NPCController.AIState newState)
    {
        // Continue intro owns the butcher — no hunt/patrol transitions until UnlockCinematic.
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic)
            && (newState == NPCController.AIState.Chase
                || newState == NPCController.AIState.Search
                || newState == NPCController.AIState.Ambush
                || newState == NPCController.AIState.Suspicious
                || newState == NPCController.AIState.Capture))
            return;

        // Mistimed hammer hunt ignores Search / Natural until Capture clears the flag.
        if (_relentlessViseMissHunt
            && (newState == NPCController.AIState.Search
                || newState == NPCController.AIState.Natural
                || newState == NPCController.AIState.Ambush
                || newState == NPCController.AIState.Suspicious))
        {
            newState = NPCController.AIState.Chase;
        }

        if (newState == npc.currentState)
            return;

        npc.interaction.CancelInteraction();
        var prev = npc.currentState;
        npc.currentState = newState;

        RefreshChaseMusicFromAllNpcs();

        switch (newState)
        {
            case NPCController.AIState.Suspicious:
                Debug.Log("🤔 State: Suspicous");
                break;


            case NPCController.AIState.Search:
                if (prev == NPCController.AIState.Chase)
                    CaptureContext.ClearIf(CaptureReason.Spotted);
                ClearWitnessedHideCapture();
                StartSearchRoutine();
                break;

            case NPCController.AIState.Ambush:
                Debug.Log("😈 State: AMBUSH");
                ClearWitnessedHideCapture();
                StartAmbushRoutine();
                break;

            // case NPCController.AIState.Patrol:
            //     Debug.Log("🚶 State: PATROL");
            //     npc.StartCoroutine(PatrolRoutine());
            //     break;

            case NPCController.AIState.Natural:
                if (prev == NPCController.AIState.Chase)
                    CaptureContext.ClearIf(CaptureReason.Spotted);
                StopSearchRoutine();
                StopAmbushRoutine();
                ClearWitnessedHideCapture();
                if (agent != null)
                    agent.isStopped = false;
                npc.interaction.StartNextRoutine();
                //Debug.Log("🌿 State: NATURAL");
                break;

            case NPCController.AIState.Chase:
                StopSearchRoutine();
                StopAmbushRoutine();
                _chaseLostStillTimer = 0f;
                // Witnessed-hide capture owns chase movement when active — don't clear it here.
                if (_witnessedHideSpot == null)
                    nav?.ClearRoute();
                if (agent != null)
                    agent.isStopped = false;
                // Pig sfx only when he breaks from idle/ambush into chase — not Search→Chase
                // (mic / suspicion redirects) which would spam it.
                if (prev == NPCController.AIState.Ambush
                    || prev == NPCController.AIState.Natural)
                    TryPlayInvestigateSfx();
                //Debug.Log("🏃 State: CHASE");
                break;

            case NPCController.AIState.Capture:
                // Keep _relentlessViseMissHunt until DoCaptureNow — aborting Capture to
                // run to a hide spot must not drop back into a normal chase/search.
                StopSearchRoutine();
                StopAmbushRoutine();
                ClearWitnessedHideCapture();
                StopAgentMovement();
                npc.poiLookDirector?.CancelHoldAndClearLook();
                //Debug.Log("State: CAPTURE");
                npc.BeginCapture();
                break;

            case NPCController.AIState.Scripted:
                StopSearchRoutine();
                StopAmbushRoutine();
                ClearWitnessedHideCapture();
                npc.poiLookDirector?.CancelHoldAndClearLook();
                break;
        }
    }

    void PlayChaseEnterSfx()
    {
        if (npc.ChaseEnterSfx == null || SoundManager.Instance == null)
            return;

        SoundManager.Instance.PlaySFXFollowing(
            npc.ChaseEnterSfx,
            npc.transform,
            npc.ChaseEnterVolume,
            offset: NPCController.SfxEmitOffset);
    }

    float _lastInvestigateSfxTime = -999f;
    const float InvestigateSfxCooldown = 4f;

    /// <summary>
    /// Pig / chase-enter bark when he breaks from idle to investigate a loud noise.
    /// Cooldown prevents double-fire if Chase enter also plays right after.
    /// </summary>
    public void TryPlayInvestigateSfx()
    {
        float clipLength = npc.ChaseEnterSfx != null ? npc.ChaseEnterSfx.length : 0f;
        if (Time.time - _lastInvestigateSfxTime < Mathf.Max(InvestigateSfxCooldown, clipLength))
            return;

        PlaySpottedScream();
    }

    /// <summary>Plays the pig scream regardless of cooldown and returns its length in seconds.</summary>
    public float PlaySpottedScream()
    {
        _lastInvestigateSfxTime = Time.time;
        PlayChaseEnterSfx();
        return npc.ChaseEnterSfx != null ? npc.ChaseEnterSfx.length : 0f;
    }

    /// <summary>Stop search/ambush routines and movement without changing state (spotted scream).</summary>
    public void HaltForSpottedReaction()
    {
        StopSearchRoutine();
        StopAmbushRoutine();
        StopAgentMovement();
    }

    /// <summary>
    /// Next Search skips the opening stand-still and uses run speed (window-scare handoff).
    /// </summary>
    public void ArmUrgentSearch(bool run = true)
    {
        _skipNextSearchStartupDelay = true;
        _urgentSearchRun = run;
    }

    /// <summary>
    /// First time he sees the cage after a real escape: pig bark, then (re)start search.
    /// </summary>
    public void BeginCageEscapeReaction()
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            return;
        if (_cageEscapeReactionCo != null)
            return;

        _cageEscapeReactionCo = npc.StartCoroutine(CageEscapeReactionRoutine());
    }

    IEnumerator CageEscapeReactionRoutine()
    {
        TryPlayInvestigateSfx();

        StopSearchRoutine();
        StopAmbushRoutine();
        nav?.ClearRoute();
        StopAgentMovement();

        _cageEscapeReactionCo = null;

        if (npc.currentState == NPCController.AIState.Chase
            || npc.currentState == NPCController.AIState.Capture)
            yield break;

        // SetState(Search) no-ops when already Searching — that left him frozen after
        // StopSearchRoutine + StopAgentMovement. Restart search in place instead.
        if (npc.currentState == NPCController.AIState.Search)
        {
            if (agent != null)
                agent.isStopped = false;
            StartSearchRoutine();
            yield break;
        }

        // Stand up if mid-sit, then Search. SearchRoutine already waits 2s at the start.
        // SetState alone CancelInteraction()s and leaves him floating in the sit pose.
        if (npc.interaction != null)
            npc.interaction.StandUpFromHotspotThenSetState(NPCController.AIState.Search);
        else
            SetState(NPCController.AIState.Search);

        yield break;
    }

    void StartSearchRoutine()
    {
        StopSearchRoutine();
        _searchRoutineCo = npc.StartCoroutine(RunSearchRoutine());
    }

    void StopSearchRoutine()
    {
        if (_searchRoutineCo == null)
            return;

        npc.StopCoroutine(_searchRoutineCo);
        _searchRoutineCo = null;
        _urgentSearchRun = false;
    }

    IEnumerator RunSearchRoutine()
    {
        yield return SearchRoutine();
        _searchRoutineCo = null;
        _urgentSearchRun = false;
    }

    void StartAmbushRoutine()
    {
        StopAmbushRoutine();
        _ambushRoutineCo = npc.StartCoroutine(RunAmbushRoutine());
    }

    void StopAmbushRoutine()
    {
        if (_ambushRoutineCo == null)
            return;

        npc.StopCoroutine(_ambushRoutineCo);
        _ambushRoutineCo = null;
        EndAmbushPresentation();
    }

    public void ClearWitnessedHideCapture()
    {
        if (_witnessedHideCaptureCo != null)
        {
            npc.StopCoroutine(_witnessedHideCaptureCo);
            _witnessedHideCaptureCo = null;
        }

        _witnessedHideSpot = null;
    }

    /// <summary>
    /// Hard day-reset after lockpick Continue: leave Capture cleanly (animator + AI + meters).
    /// Parks in Natural idle — does NOT start patrol until cinematic unlock kicks Natural.
    /// </summary>
    public void RecoverFromCaptureContinue()
    {
        _relentlessViseMissHunt = false;
        _ambushThreatRoom = null;
        ClearWitnessedHideCapture();
        StopSearchRoutine();
        StopAmbushRoutine();
        if (_cageEscapeReactionCo != null)
        {
            npc.StopCoroutine(_cageEscapeReactionCo);
            _cageEscapeReactionCo = null;
        }

        nav?.ClearRoute();
        npc.interaction?.CancelInteraction();

        if (animator != null)
        {
            if (!animator.enabled)
                animator.enabled = true;
            animator.ResetTrigger("Jumpscare");
            // Full graph rebuild — kills mid-jumpscare pose that SetState alone cannot clear.
            animator.Rebind();
            animator.Update(0f);
            if (animator.parameters != null)
            {
                for (int i = 0; i < animator.parameterCount; i++)
                {
                    var p = animator.parameters[i];
                    if (p.type == AnimatorControllerParameterType.Trigger)
                        animator.ResetTrigger(p.name);
                }
            }
            animator.SetFloat("Speed", 0f);
            animator.SetInteger("Behaviour", -1);
        }

        npc.GetComponent<SuspicionSystem>()?.ResetMeters();
        npc.poiLookDirector?.CancelHoldAndClearLook();
        npc.poiLookDirector?.UnlockLookFromPlayer();

        if (npc.perception != null)
        {
            npc.perception.searchTarget = null;
            npc.perception.ResetCageAlert();
            npc.perception.RedirectLastKnownPosition(npc.transform.position);
            npc.perception.ClearVisionState();
        }

        EnsureAgentReadyAfterCapture();
        if (agent != null)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            // Keep agent runnable — Unlock kicks Natural; isStopped=true softlocks patrol.
            agent.isStopped = false;
        }

        // Natural idle without StartNextRoutine — UnlockCinematic starts patrol after intro.
        npc.currentState = NPCController.AIState.Natural;
        if (agent != null)
            agent.speed = walkingSpeed;
    }

    void EnsureAgentReadyAfterCapture()
    {
        if (agent == null)
            return;

        if (!agent.enabled)
            agent.enabled = true;

        agent.updatePosition = true;
        agent.updateRotation = true;

        Vector3 pos = npc.transform.position;
        if (!agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                npc.transform.position = hit.position;
            }
        }

        if (agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            agent.isStopped = false;
            EnsureLocomotion();
            agent.speed = walkingSpeed;
        }
        else
        {
            Debug.LogWarning($"[FSMController] {npc.name} off NavMesh after Continue recovery at {pos}");
        }
    }

    IEnumerator KickNaturalRoutineNextFrame()
    {
        yield return null;
        if (npc == null || npc.currentState != NPCController.AIState.Natural)
            yield break;
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            yield break;

        EnsureAgentReadyAfterCapture();
        npc.EnsureNaturalRoutineStarted();
    }

    /// <summary>
    /// Path to a hide spot the NPC witnessed the player enter, then capture if still occupied.
    /// Arms the hide-spot lock immediately so Chase ticks cannot capture mid-path.
    /// </summary>
    public void BeginWitnessedHideCapture(HidingSpot spot)
    {
        if (spot == null)
            return;

        // Already committed to this spot — don't restart.
        if (_witnessedHideCaptureCo != null && _witnessedHideSpot == spot)
            return;

        ClearWitnessedHideCapture();
        _witnessedHideSpot = spot;

        Vector3 dest = spot.GetSearchPivot() != null
            ? spot.GetSearchPivot().position
            : spot.transform.position;

        if (npc.perception != null)
            npc.perception.RedirectLastKnownPosition(dest);

        EnsureLocomotion();
        _witnessedHideCaptureCo = npc.StartCoroutine(WitnessedHideCaptureRoutine(spot, dest));
    }

    public bool IsHideSpotCheckActive => _witnessedHideSpot != null;

    IEnumerator WitnessedHideCaptureRoutine(HidingSpot spot, Vector3 dest)
    {
        // Mic / witnessed-hide can fire while he's still on a sit hotspot. Stand-up must finish
        // and Chase must be active before we path — otherwise we "arrive" at the nearby hide
        // while still Natural, exit early, and leave _witnessedHideSpot set which blocks TickChase forever.
        float standDeadline = Time.time + 12f;
        while (Time.time < standDeadline)
        {
            if (_witnessedHideSpot != spot)
            {
                _witnessedHideCaptureCo = null;
                yield break;
            }

            bool standingUp = npc.interaction != null && npc.interaction.IsStandingUpFromHotspot;
            if (npc.currentState == NPCController.AIState.Chase && !standingUp)
                break;

            yield return null;
        }

        if (npc.currentState != NPCController.AIState.Chase || _witnessedHideSpot != spot)
        {
            _witnessedHideCaptureCo = null;
            if (_witnessedHideSpot == spot)
                _witnessedHideSpot = null;
            yield break;
        }

        if (agent != null)
        {
            agent.isStopped = false;
            agent.speed = runningSpeed;
        }

        // Retry pathing if Navigator is mid-door and ignores the first GoToPoint.
        float pathStart = Time.time;
        float startDist = agent != null
            ? Vector3.Distance(agent.transform.position, dest)
            : float.MaxValue;

        while (Time.time - pathStart < 2f)
        {
            if (nav != null && !nav.IsInDoorTransit)
                nav.GoToPoint(dest);
            else if (nav == null && agent != null)
                agent.SetDestination(dest);

            yield return null;

            if (nav == null || !nav.IsInDoorTransit)
                break;
        }

        if (nav != null)
            nav.GoToPoint(dest);
        else if (agent != null)
            agent.SetDestination(dest);

        // Longer timeout — door routes to another floor can take a while; stuck must NEVER capture.
        bool arrived = false;
        yield return WaitForAgentArrivalOrGiveUp(
            agent, dest, SearchArriveDistance, WitnessedHideStuckTimeout, ok => arrived = ok);

        if (npc.currentState != NPCController.AIState.Chase
            || _witnessedHideSpot != spot)
        {
            _witnessedHideCaptureCo = null;
            if (_witnessedHideSpot == spot)
                _witnessedHideSpot = null;
            yield break;
        }

        // Final hard gate: must be at the spot right now. Never capture from a stale/false arrive.
        bool reallyThere = false;
        if (arrived && agent != null)
        {
            Vector3 pos = agent.transform.position;
            float dist = Vector3.Distance(pos, dest);
            float yDelta = Mathf.Abs(pos.y - dest.y);
            reallyThere = dist <= SearchArriveDistance + ArriveRecheckSlack
                && yDelta <= SearchArriveMaxYDelta;

            // Started far away but "arrived" too fast to have walked it — reject.
            if (reallyThere && startDist > SearchArriveDistance + 1.5f)
            {
                float elapsed = Time.time - pathStart;
                float minWalkTime = (startDist - SearchArriveDistance) / Mathf.Max(runningSpeed, 1f);
                if (elapsed < minWalkTime * 0.4f)
                    reallyThere = false;
            }
        }

        var lookDirector = npc.poiLookDirector;
        Transform searchPoi = spot != null ? spot.ResolveSearchLookTarget() : null;
        if (lookDirector != null && searchPoi != null)
        {
            lookDirector.SnapPOITo(searchPoi);
            lookDirector.TriggerLookHold();
        }

        bool stillOccupied = spot != null && spot.isOccupied && !spot.searchCheckpointOnly;

        if (reallyThere && stillOccupied)
        {
            Debug.Log($"🎯 Player caught after witnessed hide enter: {spot.name}");
            if (CaptureContext.Pending == CaptureReason.Unknown)
                CaptureContext.Set(CaptureReason.WitnessedHide);
            // Keep _witnessedHideSpot set until Capture state takes over (blocks TickChase).
            _witnessedHideCaptureCo = null;
            SetState(NPCController.AIState.Capture);
            yield break;
        }

        _witnessedHideCaptureCo = null;
        _witnessedHideSpot = null;

        if (lookDirector != null)
            lookDirector.CancelHoldAndClearLook();

        // Mistimed hammer hunt: don't drop into Search — keep chasing until capture.
        if (_relentlessViseMissHunt)
        {
            if (npc.currentState != NPCController.AIState.Chase)
                SetState(NPCController.AIState.Chase);
            yield break;
        }

        // Player left, or we never reached the spot — search the room around last known.
        string homeID = npc.interaction != null && npc.interaction.data != null
            ? npc.interaction.data.homeID
            : string.Empty;
        var anchor = NPCAnchorManager.Instance != null
            ? NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(dest, homeID)
            : null;
        if (anchor != null && npc.perception != null)
            npc.perception.searchTarget = anchor;

        SetState(NPCController.AIState.Search);
    }

    void ResetAmbushRustleState()
    {
        _ambushRustleArmed = false;
        _ambushRustlePlayed = false;
        _ambushRustleDelay = 0f;
        _ambushRustleElapsed = 0f;
    }

    void EndAmbushPresentation()
    {
        _ambushAtPost = false;
        ResetAmbushRustleState();
        npc.poiLookDirector?.UnlockLookFromPlayer();
    }

    void BeginAmbushPresentation()
    {
        ResetAmbushRustleState();
        npc.poiLookDirector?.LockLookToPlayer();
    }

    void ArmAmbushThreatRoom(Transform ambushPoint)
    {
        RoomAnchor room = null;
        if (ambushPoint != null)
            room = ambushPoint.GetComponentInParent<RoomAnchor>();
        if (room == null)
            room = npc.GetCurrentRoom();
        _ambushThreatRoom = room;
    }

    void ClearAmbushThreatRoom()
    {
        _ambushThreatRoom = null;
    }

    /// <summary>
    /// Keep ambush death-copy until he physically leaves the ambush room.
    /// While still in Ambush state, never clear (pathing / room flicker mid-walk).
    /// </summary>
    void TickAmbushThreatRoom()
    {
        if (_ambushThreatRoom == null)
            return;
        if (npc.currentState == NPCController.AIState.Ambush)
            return;

        RoomAnchor current = npc.GetCurrentRoom();
        if (current == null || current != _ambushThreatRoom)
            ClearAmbushThreatRoom();
    }

    IEnumerator RunAmbushRoutine()
    {
        yield return AmbushRoutine();
        _ambushRoutineCo = null;
    }

    public void Update()
    {
        EnsureLocomotion();
        TickAmbushThreatRoom();

        // Explicitly drive look during chase/capture (no autonomous look loop).
        var lookDirector = npc != null ? npc.poiLookDirector : null;
        
        switch (npc.currentState)
        {
            case NPCController.AIState.Natural:
                
                agent.speed = walkingSpeed;
                break;

            case NPCController.AIState.Suspicious:
                agent.speed = runningSpeed;
                MoveToSuspiciousTarget();
                break;

            case NPCController.AIState.Search:
                agent.speed = _urgentSearchRun
                    ? runningSpeed
                    : walkingSpeed + ((runningSpeed - walkingSpeed) / 3f);
                break;

            case NPCController.AIState.Ambush:
                agent.speed = walkingSpeed;
                npc.poiLookDirector?.TickLockedPlayerLook();
                TickAmbushRustle();
                if (_ambushAtPost)
                    TickAmbushBodyRotationTowardsPlayer();
                break;

            case NPCController.AIState.Chase:
                // Visible player during chase: push harder. Lost LoS falls back to normal run.
                bool chaseCanSee = !IsPlayerStealthHidden()
                    && npc.perception != null
                    && npc.perception.CanSeePlayer;
                float chaseMult = Mathf.Max(1f, npc.chaseVisibleSpeedMultiplier);
                agent.speed = chaseCanSee ? runningSpeed * chaseMult : runningSpeed;
                if (lookDirector != null)
                {
                    lookDirector.SetLookWeight(1f);
                    lookDirector.SnapPOIToPlayer();
                }
                TickChase();
                break;

            case NPCController.AIState.Patrol:
                agent.speed = walkingSpeed;
                break;

            case NPCController.AIState.Capture:
                lookDirector?.CancelHoldAndClearLook();
                StopAgentMovement();
                break;
        }
    }

    public IEnumerator CapturePlayerRoutine()
    {
        npc.poiLookDirector?.CancelHoldAndClearLook();

        // Only skip if jumpscare already started. ReasonCaptured alone is wrong —
        // BeginCaptureUi holds that gate during the grab, and a failed grab can leave it stuck.
        if (JumpscareManager.Instance != null && JumpscareManager.Instance.IsJumpscarePlaying)
        {
            npc.OnCaptureRoutineFinished();
            yield break;
        }

        // Recover sticky capture UI/gate from a grab that never reached TriggerJumpscare.
        if (JumpscareManager.CaptureUiActive
            || PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
        {
            JumpscareManager.CancelCaptureUi();
            if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
                PlayerControlGate.Release(PlayerControlGate.ReasonCaptured);
        }

        // Hide HUD/cursor immediately — hide-exit wait happens before DoCaptureNow/jumpscare,
        // and exiting the locker would otherwise flash crosshair UI.
        JumpscareManager.BeginCaptureUi();

        // Mid window-climb: drop climb cam/gate instantly so jumpscare takes over with no blend delay.
        WindowClimbThrough.AbortForCapture();

        var playerT = npc.perception != null ? npc.perception.PlayerTransform : null;
        if (playerT != null)
        {
            var hider = playerT.GetComponent<PlayerHider>();
            if (hider != null && hider.isHidden && hider.CurrentHidingSpot != null)
            {
                var spot = hider.CurrentHidingSpot;
                Vector3 spotPos = spot.GetSearchPivot() != null
                    ? spot.GetSearchPivot().position
                    : spot.transform.position;

                Vector3 npcPos = npc.transform.position;
                float distToSpot = Vector3.Distance(npcPos, spotPos);
                float yDelta = Mathf.Abs(npcPos.y - spotPos.y);

                // HARD RULE: a hidden player can only be grabbed when the NPC is physically at
                // the hiding spot. Anywhere else — cancel this capture, run there first, and
                // the arrival re-triggers capture properly.
                if (distToSpot > SearchArriveDistance + ArriveRecheckSlack || yDelta > SearchArriveMaxYDelta)
                {
                    Debug.Log($"🚫 Capture blocked — {distToSpot:0.0}m from {spot.name}. Running there first.");
                    JumpscareManager.CancelCaptureUi();
                    npc.OnCaptureRoutineFinished();
                    SetState(NPCController.AIState.Chase);
                    // Relentless hunt paths to the hide itself each TickChase — don't arm the
                    // witnessed-hide coroutine (it can softlock and steal chase ownership).
                    if (!_relentlessViseMissHunt)
                        BeginWitnessedHideCapture(spot);
                    yield break;
                }

                hider.CurrentHidingSpot.RevealPlayerForCapture(playerT.gameObject);
                float wait = Mathf.Max(0f, npc.hideExitSecondsBeforeCapture);
                if (wait > 0f)
                    yield return new WaitForSeconds(wait);
            }
        }

        // Guard against any failure inside the capture sequence (missing SFX/manager refs, etc.)
        // permanently freezing the NPC in AIState.Capture and leaking the PlayerControlGate lock.
        try
        {
            DoCaptureNow();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FSMController] DoCaptureNow threw, forcing a safe recovery: {e}");
            _relentlessViseMissHunt = false;
            JumpscareManager.CancelCaptureUi();
            PlayerControlGate.Release(PlayerControlGate.ReasonCaptured);
            SetState(NPCController.AIState.Natural);
        }

        npc.OnCaptureRoutineFinished();
    }

    /// <summary>
    /// Perfect-knowledge chase until capture (vise miss, watched cage-door slam, etc.).
    /// If they hide, uses the same witnessed-hide capture path as seeing them enter a locker.
    /// </summary>
    public void BeginRelentlessViseMissHunt() => BeginRelentlessHunt(CaptureReason.HammerTimingFail);

    /// <summary>Dev: drop any hunt (relentless included) and send him back to his normal day.</summary>
    public void CallOffHunt()
    {
        _relentlessViseMissHunt = false;
        CaptureContext.ClearIf(CaptureReason.Spotted);
        SetState(NPCController.AIState.Natural);
    }

    public void BeginRelentlessHunt(CaptureReason reason)
    {
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            return;
        if (npc.currentState == NPCController.AIState.Dead
            || npc.currentState == NPCController.AIState.Scripted
            || npc.currentState == NPCController.AIState.Capture)
            return;

        _relentlessViseMissHunt = true;
        CaptureContext.Set(reason);
        StopSearchRoutine();
        StopAmbushRoutine();

        Transform playerT = npc.perception != null ? npc.perception.PlayerTransform : null;
        if (playerT == null)
        {
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
                playerT = playerGo.transform;
        }

        if (playerT == null)
            return;

        var hider = playerT.GetComponent<PlayerHider>();
        bool hiddenInSpot = hider != null && hider.isHidden && hider.CurrentHidingSpot != null
            && !hider.CurrentHidingSpot.searchCheckpointOnly;

        ClearWitnessedHideCapture();

        npc.perception?.RedirectLastKnownPosition(
            hiddenInSpot
                ? (hider.CurrentHidingSpot.GetSearchPivot() != null
                    ? hider.CurrentHidingSpot.GetSearchPivot().position
                    : hider.CurrentHidingSpot.transform.position)
                : playerT.position);

        if (agent != null)
        {
            agent.isStopped = false;
            agent.speed = runningSpeed;
        }

        npc._lastChaseTarget = new Vector3(float.PositiveInfinity, 0f, 0f);
        npc._nextChaseReplanTime = 0f;

        if (npc.poiLookDirector != null)
        {
            npc.poiLookDirector.UnlockLookFromPlayer();
            npc.poiLookDirector.CancelHoldAndClearLook();
        }

        nav?.ClearRoute();

        if (npc.interaction != null && npc.interaction.IsStandingUpFromHotspot)
        {
            npc.interaction.StandUpFromHotspotThenSetState(NPCController.AIState.Chase);
            return;
        }

        if (npc.interaction != null
            && npc.interaction.StandUpFromHotspotThenSetState(NPCController.AIState.Chase))
            return;

        if (npc.currentState != NPCController.AIState.Chase)
            SetState(NPCController.AIState.Chase);
        else
        {
            EnsureLocomotion();
            if (agent != null)
                agent.speed = runningSpeed;
            TickChase();
        }
    }

    void DoCaptureNow()
    {
        _relentlessViseMissHunt = false;
        HidingSpot.EndHidingCameraForCapture();

        nav.ClearRoute();

        var playerT = npc.perception != null ? npc.perception.PlayerTransform : null;
        if (playerT != null)
        {
            var interactor = playerT.GetComponent<PlayerInteractor>();
            if (interactor != null)
            {
                var rhythm = interactor.GetComponent<PlayerFootstepRhythm>();
                if (rhythm != null) rhythm.enabled = false;
            }
        }

        // Do NOT early-out on ReasonCaptured here.
        // BeginCaptureUi() acquires that gate before this runs (to block inventory during
        // hide-exit). Treating it as "already captured" skipped TriggerJumpscare entirely.

        // Still in the ambush room (or mid-Ambush): this catch is an ambush, not "too much noise".
        if (_ambushThreatRoom != null)
            CaptureContext.Set(CaptureReason.Ambush);
        else if (CaptureContext.Pending == CaptureReason.Unknown)
            CaptureContext.Set(CaptureReason.CaughtInOpen);

        if (SoundManager.Instance != null && SoundManager.Instance.tensionSFXBank != null)
        {
            var bank = SoundManager.Instance.tensionSFXBank;
            if (bank.Length > 0) SoundManager.Instance.PlaySFXAt(bank[0], npc.SfxEmitPosition);
            if (bank.Length > 1) SoundManager.Instance.PlaySFXAt(bank[1], npc.SfxEmitPosition);
        }

        // BeginCaptureUi already holds this for hide-exit; open-world capture acquires here.
        if (!PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            PlayerControlGate.Acquire(PlayerControlGate.ReasonCaptured);

        //Debug.Log("Player Caught!");

        if (npc.poiLookDirector != null)
            npc.poiLookDirector.CancelHoldAndClearLook();

        if (animator != null)
            animator.SetTrigger("Jumpscare");

        if (JumpscareManager.Instance != null)
            JumpscareManager.Instance.TriggerJumpscare(npc.jumpscarePoint, npc.transform);
        else
            Debug.LogError("[FSMController] JumpscareManager.Instance is null - cannot trigger jumpscare/scene reload.");

        npc.SetState(NPCController.AIState.Capture);
    }
    
    // === SUSPICIOUS → move to LKP ===
    private void MoveToSuspiciousTarget()
    {
        Vector3 targetPosition = npc.perception.LastKnownPlayerPosition;

        if (nav != null) nav.GoToPoint(targetPosition);
        else npc.GetComponent<NavMeshAgent>().SetDestination(targetPosition);

        if (!npc.GetComponent<NavMeshAgent>().pathPending &&
            npc.GetComponent<NavMeshAgent>().remainingDistance < 0.3f)
        {
            string homeZone = npc.interaction.data.homeID;

            Transform anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(targetPosition, homeZone);

            if (anchor != null)
            {
                npc.perception.searchTarget = anchor;
                SetState(NPCController.AIState.Search);
            }
            else
            {
                SetState(NPCController.AIState.Natural);
            }
        }
    }

    private IEnumerator AmbushRoutine()
    {
        var data = npc.interaction?.data;
        if (data == null || NPCAnchorManager.Instance == null)
        {
            SetState(NPCController.AIState.Natural);
            yield break;
        }

        BeginAmbushPresentation();

        string zoneID = data.homeID;

        Vector3 center =
            npc.perception.searchTarget != null ? npc.perception.searchTarget.position :
            (npc.perception.LastKnownPlayerPosition != Vector3.zero ? npc.perception.LastKnownPlayerPosition :
            npc.transform.position);

        var zoneRooms = new List<Transform>();
        NPCAnchorManager.Instance.GetRoomAnchorsInZone(zoneID, zoneRooms);
        zoneRooms.Sort((a, b) =>
            Vector3.SqrMagnitude(a.position - center).CompareTo(Vector3.SqrMagnitude(b.position - center)));

        Transform ambushPoint = PickAmbushPoint(zoneRooms);
        if (ambushPoint == null)
        {
            EndAmbushPresentation();
            SetState(NPCController.AIState.Natural);
            yield break;
        }

        ArmAmbushThreatRoom(ambushPoint);

        NavMeshAgent moveAgent = npc.interaction.agent ?? agent;
        if (moveAgent != null)
            moveAgent.isStopped = false;

        if (nav != null)
            nav.GoToPoint(ambushPoint.position);
        else if (moveAgent != null)
            moveAgent.SetDestination(ambushPoint.position);

        yield return WaitUntilNavArrived(moveAgent, .4f);

        if (npc.currentState != NPCController.AIState.Ambush)
        {
            EndAmbushPresentation();
            yield break;
        }

        StopAgentMovement();

        float alignDuration = npc.interaction != null ? npc.interaction.rotationDuration : 0.4f;
        yield return AlignRootToTransform(npc.transform, ambushPoint, alignDuration);

        _ambushWaitPosition = ambushPoint.position;
        _ambushAtPost = true;

        float waitTime = Random.Range(data.ambushWaitRange.x, data.ambushWaitRange.y);
        float confirm = Mathf.Max(0f, data.vision.confirmSeconds * 0.5f);
        float seenTimer = 0f;
        float timer = 0f;

        while (timer < waitTime)
        {
            if (npc.currentState != NPCController.AIState.Ambush)
            {
                EndAmbushPresentation();
                yield break;
            }

            timer += Time.deltaTime;

            if (!IsPlayerStealthHidden() && npc.perception.CanSeePlayer)
            {
                if (npc.perception.CanInstantConfirmVisiblePlayer())
                {
                    EndAmbushPresentation();
                    npc.BeginSpottedChase();
                    yield break;
                }

                seenTimer += Time.deltaTime;
                if (seenTimer >= confirm)
                {
                    EndAmbushPresentation();
                    npc.BeginSpottedChase();
                    yield break;
                }
            }
            else
            {
                seenTimer = 0f;
            }

            yield return null;
        }

        EndAmbushPresentation();
        SetState(NPCController.AIState.Natural);
    }

    void TickAmbushBodyRotationTowardsPlayer()
    {
        Transform player = npc.perception != null ? npc.perception.PlayerTransform : null;
        if (player == null)
            return;

        Transform root = npc.transform;
        Vector3 toPlayer = player.position - root.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f)
            return;

        Vector3 forward = root.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f)
            return;

        float angle = Vector3.Angle(forward.normalized, toPlayer.normalized);
        if (angle <= npc.AmbushTurnAngleThreshold)
            return;

        Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        float step = npc.AmbushTurnSpeed * Time.deltaTime;
        root.rotation = Quaternion.RotateTowards(root.rotation, targetRot, step);
    }

    void TickAmbushRustle()
    {
        if (_ambushRustlePlayed || npc.AmbushMetalRustleSfx == null)
            return;

        if (!_ambushRustleArmed)
        {
            if (!IsPlayerOnPlayerLayer())
                return;

            _ambushRustleArmed = true;
            _ambushRustleDelay = Random.Range(1f, 3f);
            _ambushRustleElapsed = 0f;
        }

        _ambushRustleElapsed += Time.deltaTime;
        if (_ambushRustleElapsed < _ambushRustleDelay)
            return;

        if (npc.currentState != NPCController.AIState.Ambush)
            return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXAt(npc.AmbushMetalRustleSfx, npc.SfxEmitPosition, npc.AmbushRustleVolume);

        _ambushRustlePlayed = true;
    }

    bool IsAtAmbushPost()
    {
        if (!_ambushAtPost)
            return false;

        return Vector3.Distance(npc.transform.position, _ambushWaitPosition) <= npc.AmbushPositionTolerance;
    }

    static bool IsPlayerOnPlayerLayer()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null)
            return false;

        return player.layer == LayerMask.NameToLayer("Player");
    }

    Transform PickAmbushPoint(List<Transform> zoneRooms)
    {
        if (zoneRooms == null || zoneRooms.Count == 0)
            return null;

        var roomSpots = new List<Transform>();

        for (int r = 0; r < zoneRooms.Count; r++)
        {
            roomSpots.Clear();
            CollectAmbushSpotsInRoom(zoneRooms[r], roomSpots);
            if (roomSpots.Count == 0)
                continue;

            var candidates = roomSpots;
            if (_lastAmbushSpot != null && _consecutiveSameAmbushCount >= 3 && roomSpots.Count > 1)
            {
                candidates = new List<Transform>(roomSpots.Count - 1);
                for (int i = 0; i < roomSpots.Count; i++)
                {
                    if (roomSpots[i] != _lastAmbushSpot)
                        candidates.Add(roomSpots[i]);
                }

                if (candidates.Count == 0)
                    candidates = roomSpots;
            }

            Transform picked = candidates[Random.Range(0, candidates.Count)];
            if (picked == _lastAmbushSpot)
                _consecutiveSameAmbushCount++;
            else
            {
                _lastAmbushSpot = picked;
                _consecutiveSameAmbushCount = 1;
            }

            return picked;
        }

        return null;
    }

    static IEnumerator AlignRootToTransform(Transform root, Transform target, float duration)
    {
        if (root == null || target == null)
            yield break;

        Quaternion startRot = root.rotation;
        Quaternion targetRot = target.rotation;
        float alignDuration = Mathf.Max(0.01f, duration);
        float t = 0f;

        while (t < alignDuration)
        {
            t += Time.deltaTime;
            root.rotation = Quaternion.Slerp(startRot, targetRot, t / alignDuration);
            yield return null;
        }

        root.rotation = targetRot;
    }

    static void CollectAmbushSpotsInRoom(Transform room, List<Transform> results)
    {
        if (room == null)
            return;

        var transforms = room.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null)
                continue;

            string n = t.name;
            if (!string.IsNullOrEmpty(n) &&
                n.IndexOf("ambush_position", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                results.Add(t);
            }
        }
    }

    static IEnumerator WaitUntilNavArrived(NavMeshAgent moveAgent, float distanceThreshold)
    {
        if (moveAgent == null)
            yield break;

        while (moveAgent.pathPending || moveAgent.remainingDistance > distanceThreshold)
            yield return null;
    }

    void TryAmbushOrNaturalAfterSearch()
    {
        var data = npc.interaction?.data;
        if (data != null && Random.value < data.ambushChance)
            SetState(NPCController.AIState.Ambush);
        else
            SetState(NPCController.AIState.Natural);
    }

    // === SEARCH ===
    private IEnumerator SearchRoutine()
    {
        Debug.Log("🔎 State: SEARCH");
        if (_skipNextSearchStartupDelay)
            _skipNextSearchStartupDelay = false;
        else
            yield return new WaitForSeconds(2);

        Transform roomAnchor = npc.perception.searchTarget;

        if (roomAnchor == null)
        {
            SetState(NPCController.AIState.Natural);
            yield break;
        }

        // The cage exists purely to hold a captured player - it has no hiding spots and isn't a
        // real search destination. If a noise/last-known-position resolved to it anyway, skip
        // straight to the post-search decision instead of pathing into it and "searching" nothing.
        if (roomAnchor.GetComponent<Cage>() != null)
        {
            TryAmbushOrNaturalAfterSearch();
            yield break;
        }

        // Raise pick bias for the occupied hide in this room (capture odds still only rise on a real spot check).
        RegisterSearchExposureForHiddenPlayer(roomAnchor);

        // Already standing at the hear/LKP near this room — don't freeze on a room-anchor wait.
        Vector3 roomPos = roomAnchor.position;
        float distToRoom = agent != null
            ? Vector3.Distance(agent.transform.position, roomPos)
            : float.MaxValue;
        float yDeltaToRoom = agent != null
            ? Mathf.Abs(agent.transform.position.y - roomPos.y)
            : 0f;
        bool alreadyAtRoom = distToRoom <= SearchArriveDistance + ArriveRecheckSlack
            && yDeltaToRoom <= SearchArriveMaxYDelta;

        if (!alreadyAtRoom)
        {
            var room = roomAnchor.GetComponent<RoomAnchor>();
            // Prefer last-known as the final step (e.g. car in Outside_Room) so door-aware
            // routing is used instead of a broken direct GoToPoint to an outdoor mesh island.
            Vector3 lkp = npc.perception != null ? npc.perception.LastKnownPlayerPosition : Vector3.zero;
            Vector3? finalPoint = null;
            Vector3 waitPos = roomPos;
            if (lkp != Vector3.zero && (lkp - roomPos).sqrMagnitude > 1f)
            {
                finalPoint = lkp;
                waitPos = lkp;
            }
            else
            {
                // Same-room with no LKP: pass room anchor so GoToRoom actually moves.
                finalPoint = roomPos;
            }

            if (nav != null && room != null)
                nav.GoToRoom(room, finalPoint);
            else if (agent != null)
                agent.SetDestination(waitPos);

            yield return WaitForAgentArrivalOrGiveUp(
                agent, waitPos, SearchArriveDistance, SearchStuckTimeout);
        }

        var data = npc.interaction?.data;
        var allStops = roomAnchor.GetComponentsInChildren<HidingSpot>();
        var searchStops = PickSearchSpots(allStops, data?.searchExtraSpotChances);
        var lookDirector = npc.poiLookDirector;

        Debug.Log($"🔎 Searching {searchStops.Count}/{allStops.Length} spot(s) in {roomAnchor.name}");

        // Outside (and any room with no hide spots): poke around last-known instead of bailing.
        if (searchStops.Count == 0)
        {
            yield return PatrolAroundLastKnown();
            if (npc.currentState != NPCController.AIState.Capture
                && npc.currentState != NPCController.AIState.Chase)
            {
                if (!IsPlayerStealthHidden() && npc.perception != null && npc.perception.CanSeePlayer)
                    npc.BeginSpottedChase();
                else
                    TryAmbushOrNaturalAfterSearch();
            }
            yield break;
        }

        foreach (var spot in searchStops)
        {
            if (TryBreakSearchForChase())
                yield break;

            if (spot == null)
                continue;

            Transform searchPivot = spot.GetSearchPivot();
            if (searchPivot == null)
                continue;

            Vector3 spotDest = searchPivot.position;
            if (nav != null) nav.GoToPoint(spotDest);
            else agent.SetDestination(spotDest);

            bool reachedSpot = false;
            yield return WaitForAgentArrivalOrGiveUp(
                agent, spotDest, SearchArriveDistance, SearchStuckTimeout, ok => reachedSpot = ok);

            if (TryBreakSearchForChase())
                yield break;

            // Stuck / unreachable — do not "check" (and possibly capture) from across the map.
            if (!reachedSpot)
                continue;

            Transform searchPoi = spot.ResolveSearchLookTarget();
            float waitTime = spot.GetSearchWaitSeconds(data);
            const float checkEvery = 0.25f;

            if (lookDirector != null)
            {
                lookDirector.SnapPOITo(searchPoi);
                lookDirector.TriggerLookHold();
            }

            float elapsed = 0f;
            while (elapsed < waitTime)
            {
                if (TryBreakSearchForChase())
                    yield break;

                if (lookDirector != null)
                {
                    lookDirector.SnapPOITo(searchPoi);
                    lookDirector.SnapPOIToRandomOrPlayer();
                }

                yield return new WaitForSeconds(checkEvery);
                elapsed += checkEvery;
            }

            if (lookDirector != null)
                lookDirector.CancelHoldAndClearLook();

            if (!spot.searchCheckpointOnly && data != null && spot.isOccupied)
            {
                var occupyingHider = spot.ResolveOccupyingHider();
                // Count this check of the occupied spot (same-hide ladder: 0/0/25/50/100).
                occupyingHider?.NotifyOccupiedSpotSearched(spot);

                float captureChance = spot.GetCaptureChance(data.captureChance);
                bool micForced = occupyingHider != null
                    && occupyingHider.TryGetMicCaptureOverrideSpot(out var micSpot)
                    && micSpot == spot;
                int exposure = occupyingHider != null ? occupyingHider.GetSearchExposureCount(spot) : 0;
                occupyingHider?.ClearMicCaptureOverride();

                if (UnityEngine.Random.value < captureChance)
                {
                    if (micForced)
                        CaptureContext.Set(CaptureReason.HideNoise);
                    else
                        CaptureContext.Set(CaptureReason.SameHideSpot);

                    Debug.Log($"🎯 Player Caught in: {spot.name} (exposure={exposure}, chance={captureChance:0.##})");
                    SetState(NPCController.AIState.Capture);
                    yield break;
                }
            }
        }
        if (npc.currentState != NPCController.AIState.Capture)
        {
            if (!IsPlayerStealthHidden() && npc.perception != null && npc.perception.CanSeePlayer)
                npc.BeginSpottedChase();
            else
                TryAmbushOrNaturalAfterSearch();
        }
    }

    // SINGLE SOURCE OF TRUTH for "am I at the spot" — used by every arrival wait,
    // arrival re-check, and the hidden-player capture gate.
    const float SearchArriveDistance = 1f;
    // Tiny tolerance for re-checks that happen a few frames after the arrival wait
    // (the agent can drift slightly while decelerating). Never used to arrive early.
    const float ArriveRecheckSlack = 0.35f;
    const float SearchStuckTimeout = 5f;
    // Above the worst door wait (open timeout + SFX wait + basement hold ≈ 5.5s).
    const float WitnessedHideStuckTimeout = 8f;
    const float SearchArriveMaxYDelta = 1.5f;
    // Progress slower than this counts toward the stuck timeout (frame-rate independent).
    const float StuckProgressSpeed = 0.25f;

    const float ChaseLostStillSpeed = 0.2f;
    const float ChaseLostStillSeconds = 1f;

    const float LastSeenPatrolRadius = 7f;
    const float LastSeenPatrolMinSeparation = 2.25f;
    const int LastSeenPatrolMinStops = 2;
    const int LastSeenPatrolMaxStops = 3;

    /// <summary>
    /// When a search room has no hiding spots (e.g. Outside): walk 2–3 pathable points
    /// around last-known, wait 1–2s at each, then resume the normal post-search decision.
    /// </summary>
    IEnumerator PatrolAroundLastKnown()
    {
        Vector3 center = npc.perception != null
            ? npc.perception.LastKnownPlayerPosition
            : npc.transform.position;
        if (center == Vector3.zero)
            center = npc.transform.position;

        int mask = agent != null ? agent.areaMask : NavMesh.AllAreas;
        if (!NavMesh.SamplePosition(center, out NavMeshHit centerHit, 4f, mask))
            yield break;
        center = centerHit.position;

        int want = Random.Range(LastSeenPatrolMinStops, LastSeenPatrolMaxStops + 1);
        var points = new List<Vector3>(want);
        Vector3 pathFrom = agent != null ? agent.transform.position : center;

        for (int attempt = 0; attempt < 28 && points.Count < want; attempt++)
        {
            Vector2 disk = Random.insideUnitCircle * LastSeenPatrolRadius;
            if (disk.sqrMagnitude < 2.25f)
                continue;

            Vector3 probe = center + new Vector3(disk.x, 0f, disk.y);
            if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 2.5f, mask))
                continue;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(pathFrom, hit.position, mask, path)
                || path.corners == null
                || path.corners.Length == 0)
                continue;

            Vector3 dest = hit.position;
            if (path.status == NavMeshPathStatus.PathPartial)
                dest = path.corners[path.corners.Length - 1];
            else if (path.status != NavMeshPathStatus.PathComplete)
                continue;

            if ((dest - center).sqrMagnitude < 1.5f * 1.5f)
                continue;

            bool tooClose = false;
            for (int i = 0; i < points.Count; i++)
            {
                if ((points[i] - dest).sqrMagnitude < LastSeenPatrolMinSeparation * LastSeenPatrolMinSeparation)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose)
                continue;

            points.Add(dest);
        }

        if (points.Count == 0)
            yield break;

        Debug.Log($"🔎 Last-known patrol: {points.Count} stop(s) around {center}");

        for (int i = 0; i < points.Count; i++)
        {
            if (TryBreakSearchForChase())
                yield break;

            Vector3 dest = points[i];
            if (nav != null)
                nav.GoToPoint(dest);
            else if (agent != null)
                agent.SetDestination(dest);

            yield return WaitForAgentArrivalOrGiveUp(
                agent, dest, SearchArriveDistance, SearchStuckTimeout);

            if (TryBreakSearchForChase())
                yield break;

            float wait = Random.Range(1f, 2f);
            float elapsed = 0f;
            while (elapsed < wait)
            {
                if (TryBreakSearchForChase())
                    yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }

            pathFrom = agent != null ? agent.transform.position : dest;
        }
    }

    /// <summary>
    /// Waits until the agent is within <paramref name="arriveDistance"/> of <paramref name="dest"/>
    /// on roughly the same floor. Gives up after <paramref name="stuckTimeout"/> of no progress
    /// (or invalid path). <paramref name="onFinished"/> gets true only on a real arrive — never
    /// on give-up — so callers must not capture unless that flag is true.
    /// </summary>
    static IEnumerator WaitForAgentArrivalOrGiveUp(
        NavMeshAgent agent,
        Vector3 dest,
        float arriveDistance,
        float stuckTimeout,
        System.Action<bool> onFinished = null)
    {
        if (agent == null)
        {
            onFinished?.Invoke(false);
            yield break;
        }

        // Let Navigator/SetDestination start before we sample distance.
        yield return null;
        yield return null;

        float stuckTimer = 0f;
        float lastDist = Vector3.Distance(agent.transform.position, dest);

        while (true)
        {
            if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
            {
                onFinished?.Invoke(false);
                yield break;
            }

            Vector3 pos = agent.transform.position;
            float dist = Vector3.Distance(pos, dest);
            float yDelta = Mathf.Abs(pos.y - dest.y);

            // World arrive on the same floor — remainingDistance alone can false-trigger on a
            // leftover path, and raw 3D distance can pass between stacked floors.
            if (dist <= arriveDistance && yDelta <= SearchArriveMaxYDelta)
            {
                onFinished?.Invoke(true);
                yield break;
            }

            if (Mathf.Abs(dist - lastDist) < StuckProgressSpeed * Time.deltaTime)
                stuckTimer += Time.deltaTime;
            else
                stuckTimer = 0f;

            lastDist = dist;

            if (stuckTimer >= stuckTimeout)
            {
                onFinished?.Invoke(false);
                yield break;
            }

            yield return null;
        }
    }

    bool TryBreakSearchForChase()
    {
        if (!IsPlayerStealthHidden() && npc.perception != null && npc.perception.CanSeePlayer)
        {
            npc.BeginSpottedChase();
            return true;
        }

        return false;
    }

    static void RegisterSearchExposureForHiddenPlayer(Transform roomAnchor)
    {
        if (roomAnchor == null)
            return;

        var player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        var hider = player.GetComponent<PlayerHider>();
        if (hider == null)
            return;

        hider.NotifySearchWhileHidden(roomAnchor);
    }

    static int PickWeightedSearchSpotIndex(List<HidingSpot> pool)
    {
        if (pool == null || pool.Count == 0)
            return 0;
        if (pool.Count == 1)
            return 0;

        float total = 0f;
        for (int i = 0; i < pool.Count; i++)
            total += Mathf.Max(0f, pool[i].GetSearchPickWeight());

        // Default / equal weights: pure uniform — no hierarchy-order bias.
        if (total <= 0f)
            return Random.Range(0, pool.Count);

        float first = Mathf.Max(0f, pool[0].GetSearchPickWeight());
        bool allEqual = true;
        for (int i = 1; i < pool.Count; i++)
        {
            if (!Mathf.Approximately(Mathf.Max(0f, pool[i].GetSearchPickWeight()), first))
            {
                allEqual = false;
                break;
            }
        }

        if (allEqual)
            return Random.Range(0, pool.Count);

        // Unity float Range is inclusive on max — keep roll in [0, total).
        float roll = Random.Range(0f, total);
        if (roll >= total)
            roll = total * 0.999999f;

        float acc = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += Mathf.Max(0f, pool[i].GetSearchPickWeight());
            if (roll < acc)
                return i;
        }

        return pool.Count - 1;
    }

    static void ShuffleHidingSpots(List<HidingSpot> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static List<HidingSpot> PickSearchSpots(HidingSpot[] allStops, float[] extraSpotChances)
    {
        var picked = new List<HidingSpot>();
        if (allStops == null || allStops.Length == 0)
            return picked;

        var pool = new List<HidingSpot>(allStops.Length);
        for (int i = 0; i < allStops.Length; i++)
        {
            if (allStops[i] != null)
                pool.Add(allStops[i]);
        }

        if (pool.Count == 0)
            return picked;

        // Break hierarchy / GetComponentsInChildren order so searches don't feel sequential.
        ShuffleHidingSpots(pool);

        // Mic-alerted hide spot should be checked first so the override capture roll can fire.
        var player = GameObject.FindWithTag("Player");
        var hider = player != null ? player.GetComponent<PlayerHider>() : null;
        if (hider != null && hider.TryGetMicCaptureOverrideSpot(out var micSpot))
        {
            int micIdx = pool.IndexOf(micSpot);
            if (micIdx >= 0)
            {
                picked.Add(pool[micIdx]);
                pool.RemoveAt(micIdx);
            }
        }

        if (picked.Count == 0)
        {
            int first = PickWeightedSearchSpotIndex(pool);
            picked.Add(pool[first]);
            pool.RemoveAt(first);
        }

        if (extraSpotChances == null)
            return picked;

        for (int i = 0; i < extraSpotChances.Length && pool.Count > 0; i++)
        {
            if (Random.value >= extraSpotChances[i])
                break;

            int next = PickWeightedSearchSpotIndex(pool);
            picked.Add(pool[next]);
            pool.RemoveAt(next);
        }

        return picked;
    }

    static bool IsPlayerStealthHidden(Transform playerTransform)
    {
        if (playerTransform == null) return false;
        // 7 = HiddenPlayer, 12 = Captive (closed cage) — must match PerceptionController.
        int layer = playerTransform.gameObject.layer;
        if (layer == 7 || layer == 12) return true;
        var hider = playerTransform.GetComponent<PlayerHider>();
        return hider != null && hider.isHidden;
    }

    bool IsPlayerStealthHidden()
    {
        return IsPlayerStealthHidden(npc.perception?.PlayerTransform);
    }

   // ========== Chase tick (no coroutines) ==========

    public void TickChase()
    {
        var agent = npc.interaction.agent;
        if (npc.perception == null || nav == null || agent == null) return;

        // Mistimed hammer hunt owns pathing completely — never let witnessed-hide softlock it.
        if (_relentlessViseMissHunt)
        {
            TickRelentlessViseMissChase(agent);
            return;
        }

        // Stale hide lock with no running coroutine freezes chase forever.
        if (_witnessedHideSpot != null && _witnessedHideCaptureCo == null)
            _witnessedHideSpot = null;

        // Witnessed hide-enter capture owns pathing until arrival — never proximity-capture.
        if (_witnessedHideSpot != null || _witnessedHideCaptureCo != null)
            return;

        // 1. Durum Tespiti — locker hide stays valid even if NPC walks up later
        bool isHidden = IsPlayerStealthHidden();
        bool canSee   = !isHidden && npc.perception.CanSeePlayer;
        // First half of window climb: body still in the start-side room (colliders off, so LOS may fail).
        bool climbCatchable = !isHidden && WindowClimbThrough.IsCatchablePhase;

        // 2. Yakalama Kontrolü (visible + close, or mid climb-through first half — never while hidden)
        if ((canSee || climbCatchable) && npc.perception.PlayerTransform != null)
        {
            float dist = Vector3.Distance(npc.transform.position, npc.perception.PlayerTransform.position);
            if (dist <= npc.captureDistance)
            {
                CaptureContext.Set(CaptureReason.CaughtInOpen);
                SetState(NPCController.AIState.Capture); 
                agent.ResetPath();
                return;
            }
        }

        // 3. Hedef Belirleme
        Vector3 targetPosition;

        if (canSee || climbCatchable)
        {
            // Visible, or mid climb first-half: live-track the body (still in the start-side room).
            targetPosition = npc.perception.PlayerTransform.position;
            _chaseLostStillTimer = 0f;
        }
        else
        {
            // Görmüyorum (Ya saklandı ya da kaçtı): Son bilinen yere git
            targetPosition = npc.perception.LastKnownPlayerPosition;
            if (Cage.ContainsWorldPoint(targetPosition))
            {
                SetState(NPCController.AIState.Search);
                return;
            }

            // --- VAZGEÇME MANTIĞI (Burada birleştirdik) ---
            // Eğer oyuncuyu göremiyorsam VE gitmem gereken yere (LSP) vardıysam:
            float distToLSP = Vector3.Distance(npc.transform.position, targetPosition);
            float distToPlayer = npc.perception.PlayerTransform != null
                ? Vector3.Distance(npc.transform.position, npc.perception.PlayerTransform.position)
                : float.MaxValue;

            // Closed cage / locker: agent often cannot reach the LSP/player point.
            // Standing at the bars counts as "arrived" so Chase does not softlock.
            float blockedArriveDist = Mathf.Max(2.5f, npc.captureDistance + 1.25f);
            bool arrived = distToLSP < 1.5f
                || (isHidden && distToPlayer < blockedArriveDist)
                || TickChaseLostStall(agent);

            if (arrived)
            {
                Debug.Log("Chase bitti: İzini kaybettim. Odayı aramaya başlıyorum.");

                // Bulunduğumuz (veya LSP'nin olduğu) odayı arama hedefi yap
                string homeID = npc.interaction.data.homeID;
                var anchor = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(targetPosition, homeID);
                
                if (anchor != null) npc.perception.searchTarget = anchor;

                // Modu değiştir
                SetState(NPCController.AIState.Search);
                return; // Chase'den çık
            }
        }

        // 4. Hareket — door-aware routing (wait at gate, open, cross). Skip replans mid-door.
        ReplanChasePath(targetPosition);
    }

    /// <summary>
    /// Last-known is the player's collider center, often off the NavMesh (behind furniture, at a
    /// locker door) so the agent parks short of the 1.5m arrive radius and replans forever.
    /// Standing still outside a door transit for a moment counts as arrived.
    /// </summary>
    bool TickChaseLostStall(NavMeshAgent agent)
    {
        bool still = agent.velocity.sqrMagnitude < ChaseLostStillSpeed * ChaseLostStillSpeed
            && !agent.pathPending
            && !nav.IsInDoorTransit;

        _chaseLostStillTimer = still ? _chaseLostStillTimer + Time.deltaTime : 0f;
        return _chaseLostStillTimer >= ChaseLostStillSeconds;
    }

    void TickRelentlessViseMissChase(NavMeshAgent agent)
    {
        // Drop any witnessed-hide coroutine so it cannot own pathing / softlock this hunt.
        if (_witnessedHideSpot != null || _witnessedHideCaptureCo != null)
            ClearWitnessedHideCapture();

        Transform playerT = npc.perception.PlayerTransform;
        if (playerT == null)
        {
            var playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo != null)
                playerT = playerGo.transform;
        }

        if (playerT == null)
            return;

        var hider = playerT.GetComponent<PlayerHider>();
        bool hiddenInSpot = hider != null && hider.isHidden && hider.CurrentHidingSpot != null
            && !hider.CurrentHidingSpot.searchCheckpointOnly;

        Vector3 targetPosition;
        if (hiddenInSpot)
        {
            var spot = hider.CurrentHidingSpot;
            targetPosition = spot.GetSearchPivot() != null
                ? spot.GetSearchPivot().position
                : spot.transform.position;
        }
        else
        {
            targetPosition = playerT.position;
        }

        npc.perception.RedirectLastKnownPosition(targetPosition);

        float dist = Vector3.Distance(npc.transform.position, targetPosition);
        float yDelta = Mathf.Abs(npc.transform.position.y - targetPosition.y);

        // Hidden: grab only when actually at the spot. Visible: normal capture range.
        float captureRange = hiddenInSpot
            ? SearchArriveDistance + ArriveRecheckSlack
            : npc.captureDistance;
        float maxY = hiddenInSpot ? SearchArriveMaxYDelta : 3f;

        if (dist <= captureRange && yDelta <= maxY)
        {
            SetState(NPCController.AIState.Capture);
            agent.ResetPath();
            return;
        }

        if (agent.isStopped)
            agent.isStopped = false;

        // Hunt the live player / occupied hide every tick — never park on a stale LKP.
        ReplanChasePath(targetPosition, force: true);
    }

    void ReplanChasePath(Vector3 targetPosition, bool force = false)
    {
        if (nav.IsInDoorTransit)
            return;

        float now = Time.time;
        bool targetMovedEnough =
            (npc._lastChaseTarget.x == float.PositiveInfinity) ||
            (targetPosition - npc._lastChaseTarget).sqrMagnitude > (npc.chaseReplanDist * npc.chaseReplanDist);

        if (force || targetMovedEnough || now >= npc._nextChaseReplanTime)
        {
            nav.GoToPoint(targetPosition);

            npc._lastChaseTarget = targetPosition;
            npc._nextChaseReplanTime = now + (force ? Mathf.Min(0.15f, npc.chaseReplanCooldown) : npc.chaseReplanCooldown);
        }
    }


    // === PATROL ===
    // private IEnumerator PatrolRoutine()
    // {
    //     Debug.Log("📍 Starting Patrol");

    //     string zoneID = npc.interaction.data.homeID;
    //     Vector3 center = npc.perception.LastKnownPlayerPosition;

    //     List<Transform> patrolPoints = NPCAnchorManager.Instance.GetClosestAnchorsInZone(zoneID, center, 3);

    //     if (patrolPoints.Count == 0)
    //     {
    //         Debug.LogWarning("⚠️ No patrol points found.");
    //         SetState(NPCController.AIState.Natural);
    //         yield break;
    //     }

    //     Transform lastVisited = null;
    //     float endTime = Time.time + npc.interaction.data.patrolDuration;

    //     while (Time.time < endTime)
    //     {
    //         if (npc.perception.CanSeePlayer)
    //         {
    //             SetState(NPCController.AIState.Alerted);
    //             yield break;
    //         }

    //         List<Transform> options = patrolPoints.Where(p => p != lastVisited).ToList();

    //         if (options.Count == 0)
    //         {
    //             Debug.LogWarning("⚠️ Only one patrol room available, forcing repeat.");
    //             options = patrolPoints;
    //         }

    //         Transform nextPoint = options[UnityEngine.Random.Range(0, options.Count)];
    //         lastVisited = nextPoint;

    //         if (nav != null) nav.GoToPoint(nextPoint.position);

    //         var agent = npc.interaction.agent ?? npc.GetComponent<UnityEngine.AI.NavMeshAgent>();

    //         int settled = 0;
    //         while (true)
    //         {
    //             bool arrived = !agent.pathPending &&
    //                         agent.remainingDistance <= Mathf.Max(0.3f, agent.stoppingDistance + 0.05f);

    //             // avoid one-frame flicker at intermediate waypoints
    //             settled = arrived ? settled + 1 : 0;

    //             // also ensure we’re not mid-door step
    //             if (settled >= 3 && (nav == null || !nav.IsInDoorTransit))
    //                 break;

    //             yield return null;
    //         }
            
    //         Debug.Log($"🕵️ Patrolling to: {nextPoint.name}");

    //         yield return new WaitUntil(() =>
    //             !npc.GetComponent<NavMeshAgent>().pathPending &&
    //             npc.GetComponent<NavMeshAgent>().remainingDistance < 0.3f);

    //         yield return new WaitForSeconds(UnityEngine.Random.Range(1f, 2f));
    //     }

    //     Debug.Log("✅ Finished Patrol");
    //     SetState(NPCController.AIState.Natural);
    // }
}