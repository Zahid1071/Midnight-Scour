using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using FS_Atmo;
using System.Collections.Generic;
using KinoGlitch;


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

    Transform _lastAmbushSpot;
    int _consecutiveSameAmbushCount;

    Vector3 _ambushWaitPosition;
    bool _ambushAtPost;
    bool _ambushRustleArmed;
    bool _ambushRustlePlayed;
    float _ambushRustleDelay;
    float _ambushRustleElapsed;

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
        if (newState == npc.currentState)
            return;

        npc.interaction.CancelInteraction();
        var prev = npc.currentState;
        npc.currentState = newState;

        if (SoundManager.Instance != null)
        {
            bool tensionMusic =
                newState == NPCController.AIState.Chase ||
                newState == NPCController.AIState.Search;
            //SoundManager.Instance.SetChaseMusicActive(tensionMusic);
        }

        switch (newState)
        {
            case NPCController.AIState.Suspicious:
                Debug.Log("🤔 State: Suspicous");
                break;


            case NPCController.AIState.Search:
                StartSearchRoutine();
                break;

            case NPCController.AIState.Ambush:
                Debug.Log("😈 State: AMBUSH");
                StartAmbushRoutine();
                break;

            // case NPCController.AIState.Patrol:
            //     Debug.Log("🚶 State: PATROL");
            //     npc.StartCoroutine(PatrolRoutine());
            //     break;

            case NPCController.AIState.Natural:
                StopSearchRoutine();
                StopAmbushRoutine();
                if (agent != null)
                    agent.isStopped = false;
                npc.interaction.StartNextRoutine();
                Debug.Log("🌿 State: NATURAL");
                break;

            case NPCController.AIState.Chase:
                StopSearchRoutine();
                StopAmbushRoutine();
                nav?.ClearRoute();
                if (agent != null)
                    agent.isStopped = false;
                Debug.Log("🏃 State: CHASE");
                break;

            case NPCController.AIState.Capture:
                StopSearchRoutine();
                StopAmbushRoutine();
                StopAgentMovement();
                npc.poiLookDirector?.CancelHoldAndClearLook();
                Debug.Log("State: CAPTURE");
                npc.BeginCapture();
                break;

            case NPCController.AIState.Scripted:
                StopSearchRoutine();
                StopAmbushRoutine();
                npc.poiLookDirector?.CancelHoldAndClearLook();
                break;
        }
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
    }

    IEnumerator RunSearchRoutine()
    {
        yield return SearchRoutine();
        _searchRoutineCo = null;
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

    IEnumerator RunAmbushRoutine()
    {
        yield return AmbushRoutine();
        _ambushRoutineCo = null;
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

            case NPCController.AIState.Suspicious:
                agent.speed = runningSpeed;
                MoveToSuspiciousTarget();
                break;

            case NPCController.AIState.Search:
                agent.speed = walkingSpeed + ((runningSpeed - walkingSpeed) / 3f);
                break;

            case NPCController.AIState.Ambush:
                agent.speed = walkingSpeed;
                npc.poiLookDirector?.TickLockedPlayerLook();
                TickAmbushRustle();
                if (_ambushAtPost)
                    TickAmbushBodyRotationTowardsPlayer();
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
                StopAgentMovement();
                break;
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

        SoundManager.Instance.PlaySFXAt(SoundManager.Instance.tensionSFXBank[0], npc.transform.position);
        SoundManager.Instance.PlaySFXAt(SoundManager.Instance.tensionSFXBank[1], npc.transform.position);
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
                    npc.BeginChaseFromPerception();
                    yield break;
                }

                seenTimer += Time.deltaTime;
                if (seenTimer >= confirm)
                {
                    EndAmbushPresentation();
                    npc.BeginChaseFromPerception();
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
            SoundManager.Instance.PlaySFXAt(npc.AmbushMetalRustleSfx, npc.transform.position, npc.AmbushRustleVolume);

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
        yield return new WaitForSeconds(2);

        Transform roomAnchor = npc.perception.searchTarget;

        if (roomAnchor == null)
        {
            SetState(NPCController.AIState.Natural);
            yield break;
        }

        RegisterSearchExposureForHiddenPlayer(roomAnchor);

        // ensure we actually route to the room (door-aware)
        var room = roomAnchor.GetComponent<RoomAnchor>();
        if (nav != null && room != null) nav.GoToRoom(room);
        
        yield return new WaitUntil(() =>
            !npc.GetComponent<NavMeshAgent>().pathPending &&
            npc.GetComponent<NavMeshAgent>().remainingDistance <2f);

        var data = npc.interaction?.data;
        var allStops = roomAnchor.GetComponentsInChildren<HidingSpot>();
        var searchStops = PickSearchSpots(allStops, data?.searchExtraSpotChances);
        var lookDirector = npc.poiLookDirector;

        Debug.Log($"🔎 Searching {searchStops.Count}/{allStops.Length} spot(s) in {roomAnchor.name}");

        foreach (var spot in searchStops)
        {
            if (TryBreakSearchForChase())
                yield break;

            if (spot == null)
                continue;

            Transform searchPivot = spot.GetSearchPivot();
            if (searchPivot == null)
                continue;

            if (nav != null) nav.GoToPoint(searchPivot.position);
            else npc.GetComponent<NavMeshAgent>().SetDestination(searchPivot.position);

            yield return new WaitUntil(() =>
                !npc.GetComponent<NavMeshAgent>().pathPending &&
                npc.GetComponent<NavMeshAgent>().remainingDistance < 2f);

            if (TryBreakSearchForChase())
                yield break;

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

                ApplySearchGlitch(spot, data, elapsed, waitTime);

                if (lookDirector != null)
                {
                    lookDirector.SnapPOITo(searchPoi);
                    lookDirector.SnapPOIToRandomOrPlayer();
                }

                yield return new WaitForSeconds(checkEvery);
                elapsed += checkEvery;
            }

            ApplySearchGlitch(spot, data, waitTime, waitTime);

            if (lookDirector != null)
                lookDirector.CancelHoldAndClearLook();

            ResetGlitch();

            if (!spot.searchCheckpointOnly && data != null && spot.isOccupied)
            {
                float captureChance = spot.GetCaptureChance(data.captureChance);
                if (UnityEngine.Random.value < captureChance)
                {
                    Debug.Log($"🎯 Player Caught in: {spot.name}");
                    ResetGlitch();
                    SetState(NPCController.AIState.Capture);
                    yield break;
                }
            }
        }
        if (npc.currentState != NPCController.AIState.Capture)
        {
            if (!IsPlayerStealthHidden() && npc.perception != null && npc.perception.CanSeePlayer)
                npc.BeginChaseFromPerception();
            else
                TryAmbushOrNaturalAfterSearch();
        }
    }

    bool TryBreakSearchForChase()
    {
        if (!IsPlayerStealthHidden() && npc.perception != null && npc.perception.CanSeePlayer)
        {
            ResetGlitch();
            SetState(NPCController.AIState.Chase);
            return true;
        }

        return false;
    }

    const float MaxSearchGlitch = 0.05f;

    static void ApplySearchGlitch(HidingSpot spot, NPCData data, float elapsed, float waitTime)
    {
        if (spot == null || !spot.isOccupied || spot.searchCheckpointOnly)
        {
            ResetGlitch();
            return;
        }

        float baseChance = data != null ? data.captureChance : 0f;
        float captureChance = spot.GetCaptureChance(baseChance);
        float chanceScale = Mathf.Lerp(1f, 2f, Mathf.Clamp01(captureChance));
        float maxGlitch = MaxSearchGlitch * chanceScale;

        float progress = waitTime > 0f ? Mathf.Clamp01(elapsed / waitTime) : 1f;
        SetGlitch(Mathf.Lerp(0f, maxGlitch, progress));

        float audioIntensity = progress * (chanceScale / 2f);
        SoundManager.Instance?.SetSearchTension(audioIntensity);
    }

    static void ResetGlitch()
    {
        SetGlitch(0f);
        SoundManager.Instance?.StopSearchTension();
    }

    static void SetGlitch(float value)
    {
        var glitch = Camera.main != null ? Camera.main.GetComponent<AnalogGlitchController>() : null;
        if (glitch == null)
            return;

        glitch.ScanLineJitter = value;
        glitch.HorizontalShake = value;
        glitch.ColorDrift = value;
        glitch.HorizontalRipple = value;
        glitch.VerticalJump = 0f;
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
        float total = 0f;
        for (int i = 0; i < pool.Count; i++)
            total += pool[i].GetSearchPickWeight();

        if (total <= 0f)
            return Random.Range(0, pool.Count);

        float roll = Random.value * total;
        float acc = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += pool[i].GetSearchPickWeight();
            if (roll <= acc)
                return i;
        }

        return pool.Count - 1;
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

        int first = PickWeightedSearchSpotIndex(pool);
        picked.Add(pool[first]);
        pool.RemoveAt(first);

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

        // 4. Hareket — door-aware routing (wait at gate, open, cross). Skip replans mid-door.
        if (nav.IsInDoorTransit)
            return;

        float now = Time.time;
        bool targetMovedEnough =
            (npc._lastChaseTarget.x == float.PositiveInfinity) ||
            (targetPosition - npc._lastChaseTarget).sqrMagnitude > (npc.chaseReplanDist * npc.chaseReplanDist);

        if (targetMovedEnough || now >= npc._nextChaseReplanTime)
        {
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