using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using FS_Atmo;

public class FSMController
{
    private NPCController npc;
    private Animator animator;
    private Navigator nav; // <-- door-aware pathing

    NavMeshAgent agent;

    float walkingSpeed = 2.5f;   // cached from SO at first Update
    float runningSpeed = 6f;
    bool locomotionCached;

    
    
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

    public void SetState(NPCController.AIState newState)
    {

        
        npc.interaction.CancelInteraction();
        var prev = npc.currentState;
        npc.currentState = newState;

        switch (newState)
        {
            case NPCController.AIState.Alerted:
                Debug.Log("⚠️ State: ALERTED");
                npc.StartCoroutine(AlertedRoutine());
                if (prev != NPCController.AIState.Alerted)  // play once on entry
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.sfxBank[3]);
                break;
            case NPCController.AIState.Suspicious:
                Debug.Log("🤔 State: Suspicous");
                npc.StartCoroutine(SearchRoutine());
                break;


            case NPCController.AIState.Search:
                npc.StartCoroutine(SearchRoutine());
                break;

            // case NPCController.AIState.Ambush:
            //     Debug.Log("😈 State: AMBUSH");
            //     npc.StartCoroutine(AmbushRoutine());
            //     break;

            // case NPCController.AIState.Patrol:
            //     Debug.Log("🚶 State: PATROL");
            //     npc.StartCoroutine(PatrolRoutine());
            //     break;

            case NPCController.AIState.Natural:
                SoundManager.Instance.PlayMusic(SoundManager.Instance.musicBank[0]);
                if (npc.ShouldImmediatelyChasePlayer())
                    npc.BeginChaseFromPerception();
                else
                {
                    npc.interaction.StartNextRoutine();
                    Debug.Log("🌿 State: NATURAL");
                }
                break;

            case NPCController.AIState.Chase:
                nav?.ClearRoute();
                Debug.Log("🏃 State: CHASE");
                // if (SoundManager.Instance.CurrentMusicClip != SoundManager.Instance.musicBank[1])
                //     SoundManager.Instance.PlayMusic(SoundManager.Instance.musicBank[1]);
                break;

            case NPCController.AIState.Capture:
                npc.poiLookDirector?.CancelHoldAndClearLook();
                Debug.Log("State: CAPTURE");
                npc.BeginCapture();
                break;
        }
    }

    public void Update()
    {
        EnsureLocomotion();

        // Explicitly drive look during chase/capture (no autonomous look loop).
        var lookDirector = npc != null ? npc.poiLookDirector : null;
        
        switch (npc.currentState)
        {
            case NPCController.AIState.Natural:
                
                agent.speed = walkingSpeed;
                break;

            case NPCController.AIState.Alerted:
                agent.speed = walkingSpeed;
                break;

            case NPCController.AIState.Suspicious:
                agent.speed = runningSpeed;
                MoveToSuspiciousTarget();
                break;

            case NPCController.AIState.Search:
                agent.speed = walkingSpeed + ((runningSpeed - walkingSpeed) / 3f);
                break;

            case NPCController.AIState.Chase:
                agent.speed = runningSpeed;
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
                break;
        }
    }

    // === ALERTED ===
    private IEnumerator AlertedRoutine()
    {
        var agent = npc.interaction.agent;

        if (agent != null)
        {
            agent.ResetPath();
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
            // keep updateRotation untouched; we rotate manually
        }

        const float losLostToSuspicious = 0.75f; // tolerance before downgrade
        float losLostTimer = 0f;

        try
        {
            while (npc.currentState == NPCController.AIState.Alerted)
            {
                if (npc.perception.CanSeePlayer)
                {
                    losLostTimer = 0f;

                    Vector3 lookTarget = (npc.perception.PlayerTransform != null)
                        ? npc.perception.PlayerTransform.position
                        : npc.perception.LastKnownPlayerPosition;

                    Vector3 dir = lookTarget - npc.transform.position;
                    dir.y = 0f;

                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        Quaternion look = Quaternion.LookRotation(dir);
                        npc.transform.rotation = Quaternion.Slerp(
                            npc.transform.rotation, look, Time.deltaTime * 5f);
                    }
                }
                else
                {
                    losLostTimer += Time.deltaTime;

                    if (losLostTimer >= losLostToSuspicious &&
                        npc.currentState == NPCController.AIState.Alerted)
                    {
                        float roll = UnityEngine.Random.value;
                        npc.fsm.SetState(
                            roll < npc.interaction.data.ambushChance
                                ? NPCController.AIState.Ambush
                                : NPCController.AIState.Suspicious
                        );
                        yield break;
                    }
                }

                yield return null;
            }
        }
        finally
        {
            if (agent != null)
            {
                agent.isStopped = false;
                agent.updateRotation = true; // safety
            }
        }
    }



    
    
    public IEnumerator CapturePlayerRoutine()
    {
        npc.poiLookDirector?.CancelHoldAndClearLook();

        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
        {
            npc.OnCaptureRoutineFinished();
            yield break;
        }

        var playerT = npc.perception != null ? npc.perception.PlayerTransform : null;
        if (playerT != null)
        {
            var hider = playerT.GetComponent<PlayerHider>();
            if (hider != null && hider.isHidden && hider.CurrentHidingSpot != null)
            {
                hider.CurrentHidingSpot.RevealPlayerForCapture(playerT.gameObject);
                float wait = Mathf.Max(0f, npc.hideExitSecondsBeforeCapture);
                if (wait > 0f)
                    yield return new WaitForSeconds(wait);
            }
        }

        DoCaptureNow();
        npc.OnCaptureRoutineFinished();
    }

    void DoCaptureNow()
    {
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

        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured)) return;

        SoundManager.Instance.PlaySFX(SoundManager.Instance.tensionSFXBank[0], 0.5f);
        SoundManager.Instance.PlaySFXAt(SoundManager.Instance.tensionSFXBank[0], npc.transform.position);
        PlayerControlGate.Acquire(PlayerControlGate.ReasonCaptured);

        Debug.Log("Player Caught!");

        if (npc.poiLookDirector != null)
            npc.poiLookDirector.CancelHoldAndClearLook();

        animator.SetTrigger("Jumpscare");

        JumpscareManager.Instance.TriggerJumpscare(npc.jumpscarePoint, npc.transform);
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

    // === AMBUSH ===
    // private IEnumerator AmbushRoutine()
    // {
    //     string zoneID = npc.interaction.data.homeID;

    //     // prefer searchTarget, then last known, then self
    //     Vector3 center =
    //         npc.perception.searchTarget != null ? npc.perception.searchTarget.position :
    //         (npc.perception.LastKnownPlayerPosition != Vector3.zero ? npc.perception.LastKnownPlayerPosition :
    //         npc.transform.position);

    //     // progressively widen the search for ambush_position children
    //     List<Transform> ambushSpots = new List<Transform>();
    //     int[] batches = { 3, 8, 16, 32, 64 }; // widen until we find any (cap to something sane)
    //     foreach (int count in batches)
    //     {
    //         var rooms = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(zoneID, center, count);
    //         ambushSpots.Clear();

    //         foreach (var room in rooms)
    //         {
    //             if (!room) continue;

    //             for (int i = 0; i < room.childCount; i++)
    //             {
    //                 var child = room.GetChild(i);
    //                 if (!child) continue;

    //                 string n = child.name;
    //                 if (!string.IsNullOrEmpty(n) &&
    //                     n.IndexOf("ambush_position", System.StringComparison.OrdinalIgnoreCase) >= 0)
    //                 {
    //                     ambushSpots.Add(child);
    //                 }
    //             }
    //         }

    //         if (ambushSpots.Count > 0)
    //             break; // found at least one; stop widening
    //     }

    //     if (ambushSpots.Count == 0)
    //     {
    //         SetState(NPCController.AIState.Search);
    //         yield break;
    //     }

    //     if (npc.perception.searchTarget != null)
    //         ambushSpots.RemoveAll(t => t == npc.perception.searchTarget);

    //     Transform ambushPoint = ambushSpots[UnityEngine.Random.Range(0, ambushSpots.Count)];

    //     var agent = npc.interaction.agent ?? npc.GetComponent<NavMeshAgent>();
    //     if (nav != null) nav.GoToPoint(ambushPoint.position);
    //     else if (agent != null) agent.SetDestination(ambushPoint.position);

    //     yield return new WaitUntil(() =>
    //         !agent.pathPending && agent.remainingDistance < 0.3f);

    //     float waitTime = UnityEngine.Random.Range(
    //         npc.interaction.data.ambushWaitRange.x,
    //         npc.interaction.data.ambushWaitRange.y
    //     );

    //     float confirm = Mathf.Max(0f, npc.interaction.data.vision.confirmSeconds / 2f);
    //     float seenTimer = 0f;

    //     float timer = 0f;
    //     Transform target = npc.perception.PlayerTransform;

    //     while (timer < waitTime)
    //     {
    //         timer += Time.deltaTime;

    //         if (target != null)
    //         {
    //             Vector3 dir = target.position - npc.transform.position;
    //             dir.y = 0f;
    //             if (dir.sqrMagnitude > 0.0001f)
    //             {
    //                 var lookRot = Quaternion.LookRotation(dir);
    //                 npc.transform.rotation = Quaternion.Slerp(npc.transform.rotation, lookRot, Time.deltaTime * 5f);
    //             }
    //         }

    //         if (npc.perception.CanSeePlayer)
    //         {
    //             seenTimer += Time.deltaTime;
    //             if (seenTimer >= confirm)   
    //             {
    //                 SetState(NPCController.AIState.Chase);
    //                 yield break;
    //             }
    //         }
    //         else
    //         {
    //             seenTimer = 0f;
    //         }

    //         yield return null;
    //     }

    //     SetState(NPCController.AIState.Search);
    // }

    // === SEARCH ===
    private IEnumerator SearchRoutine()
    {
        Debug.Log("🔎 State: SEARCH");
        yield return new WaitForSeconds(2);

        Transform roomAnchor = npc.perception.searchTarget;

        if (roomAnchor == null)
        {
            SetState(NPCController.AIState.Natural);
            yield break;
        }

        // ensure we actually route to the room (door-aware)
        var room = roomAnchor.GetComponent<RoomAnchor>();
        if (nav != null && room != null) nav.GoToRoom(room);
        
        yield return new WaitUntil(() =>
            !npc.GetComponent<NavMeshAgent>().pathPending &&
            npc.GetComponent<NavMeshAgent>().remainingDistance <2f);

        var hidingSpots = roomAnchor.GetComponentsInChildren<HidingSpot>();
        var lookDirector = npc.poiLookDirector;

        foreach (var spot in hidingSpots)
        {
            if (!IsPlayerStealthHidden() && npc.perception.CanSeePlayer)
            {
                SetState(NPCController.AIState.Chase);
                yield break;
            }

            if (nav != null) nav.GoToPoint(spot.PivotForNPCSearchRoutine.transform.position);
            else npc.GetComponent<NavMeshAgent>().SetDestination(spot.PivotForNPCSearchRoutine.transform.position);

            yield return new WaitUntil(() =>
                !npc.GetComponent<NavMeshAgent>().pathPending &&
                npc.GetComponent<NavMeshAgent>().remainingDistance < 2f);

            // Look at this hiding spot's POI during the wait window
            Transform hidingPoi = null;
            var tfs = spot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < tfs.Length; i++)
            {
                if (tfs[i] != null && (tfs[i].name == "HidingPOI" || tfs[i].name == "hidingPOI"))
                {
                    hidingPoi = tfs[i];
                    break;
                }
            }
            if (hidingPoi == null)
                hidingPoi = spot.PivotForNPCSearchRoutine;

            if (lookDirector != null)
            {
                // One-shot "look at this hiding spot" (do not pin for the whole wait).
                lookDirector.SnapPOITo(hidingPoi);

                // Optional: briefly engage look rig (keeps it routine-driven).
                lookDirector.TriggerLookHold();

                // During the search wait, allow occasional scary snaps to the player.
                float t = 0f;
                const float window = 4f;
                const float checkEvery = 0.25f;
                while (t < window)
                {
                    // Keep baseline target as the hiding spot unless the random roll snaps elsewhere.
                    lookDirector.SnapPOITo(hidingPoi);
                    lookDirector.SnapPOIToRandomOrPlayer();

                    yield return new WaitForSeconds(checkEvery);
                    t += checkEvery;
                }

                lookDirector.CancelHoldAndClearLook();
            }
            else
            {
                yield return new WaitForSeconds(4f);
            }

            bool willSearch = UnityEngine.Random.value < npc.interaction.data.searchChance;
            if (willSearch && spot.isOccupied)
            {
                Debug.Log($"🎯 Player Caught in: {spot.name}");
                SetState(NPCController.AIState.Capture);
                yield break;
            }
        }
        if (npc.currentState != NPCController.AIState.Capture)
        {
            if (!IsPlayerStealthHidden() && npc.perception != null && npc.perception.CanSeePlayer)
                npc.BeginChaseFromPerception();
            else
                SetState(NPCController.AIState.Natural);
        }
            

        // if (UnityEngine.Random.value < npc.interaction.data.patrolChance)
        //     SetState(NPCController.AIState.Patrol);
        // else
        //     SetState(NPCController.AIState.Natural);
            
    }

    static bool IsPlayerStealthHidden(Transform playerTransform)
    {
        if (playerTransform == null) return false;
        if (playerTransform.gameObject.layer == 7) return true;
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

        // 1. Durum Tespiti — locker hide stays valid even if NPC walks up later
        bool isHidden = IsPlayerStealthHidden();
        bool canSee   = !isHidden && npc.perception.CanSeePlayer;

        // 2. Yakalama Kontrolü (Sadece Görüyor ve Yakınsa)
        if (canSee && npc.perception.PlayerTransform != null)
        {
            float dist = Vector3.Distance(npc.transform.position, npc.perception.PlayerTransform.position);
            if (dist <= npc.captureDistance)
            {
                SetState(NPCController.AIState.Capture); 
                agent.ResetPath();
                return;
            }
        }

        // 3. Hedef Belirleme
        Vector3 targetPosition;

        if (canSee)
        {
            // Görüyorum: Direkt üzerine koş (Canlı Takip)
            targetPosition = npc.perception.PlayerTransform.position;
        }
        else
        {
            // Görmüyorum (Ya saklandı ya da kaçtı): Son bilinen yere git
            targetPosition = npc.perception.LastKnownPlayerPosition;

            // --- VAZGEÇME MANTIĞI (Burada birleştirdik) ---
            // Eğer oyuncuyu göremiyorsam VE gitmem gereken yere (LSP) vardıysam:
            float distToLSP = Vector3.Distance(npc.transform.position, targetPosition);

            if (distToLSP < 1.5f)
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

        // 4. Hareket (Navigation)
        float now = Time.time;
        bool targetMovedEnough =
            (npc._lastChaseTarget.x == float.PositiveInfinity) ||
            (targetPosition - npc._lastChaseTarget).sqrMagnitude > (npc.chaseReplanDist * npc.chaseReplanDist);

        if (targetMovedEnough || now >= npc._nextChaseReplanTime)
        {
            if (canSee)
                nav.SetDestinationDirect(targetPosition);
            else
                nav.GoToPoint(targetPosition);

            npc._lastChaseTarget = targetPosition;
            npc._nextChaseReplanTime = now + npc.chaseReplanCooldown;
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