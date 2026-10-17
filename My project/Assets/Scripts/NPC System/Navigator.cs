// Navigator.cs
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Atmo
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class Navigator : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] float waypointTolerance = 0.2f;      // use >= agent.stoppingDistance
        [SerializeField] float stuckTimeout      = 3f;        // seconds without progress
        [SerializeField] float doorOpenWaitTimeout = 2.5f;    // wait for carve to clear before crossing
        [SerializeField] float doorTransitTimeout = 12f;      // abort A↔B loop if crossing stalls
        [Tooltip("Room ID that grants brief player-footstep deafness when this NPC opens a door to enter it.")]
        [SerializeField] string footstepDeafnessEnterRoomId = "Basement_Main_Room";
        [Tooltip("Seconds to linger after the door is open when entering footstepDeafnessEnterRoomId (basement). Other doors are unaffected.")]
        [SerializeField] float basementDoorPostOpenHold = 2f;

        [Tooltip("For an NPC without NPCData: the zone (parent of the room anchors) it finds its way around.")]
        [SerializeField] string homeZoneID;

        [Header("DEBUG")]
        [SerializeField] bool logPlannedRoute = true;

        private NavMeshAgent agent;
        private InteractionController interaction;
        private RoomAnchor _cachedCurrentRoom;

        string HomeZoneID => interaction != null && interaction.data != null ? interaction.data.homeID : homeZoneID;

        public readonly List<Transform> route = new();
        private readonly List<Transform> tempPoints = new();
        private int idx = -1;
        private Coroutine followCo;

        // Door-transit lock: prevents replans while crossing
        private bool isDoorTransit;
        public bool IsInDoorTransit => isDoorTransit;
        /// <summary>True while a GoToRoom / GoToPoint walk is under way.</summary>
        public bool IsFollowingRoute => following;
        private bool following;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            interaction = GetComponent<InteractionController>();
            // A missing component comes back as a fake null in the editor, which the ?. calls below don't catch.
            if (interaction == null)
                interaction = null;
        }

        void Start()
        {
            // CRITICAL: Initialize current room at game start BEFORE any path planning
            var initialRoom = FindCurrentRoom();
            if (logPlannedRoute)
            {
                // if (initialRoom != null)
                //     Debug.Log($"[Navigator] Game Start - Initial room: {initialRoom.roomID ?? initialRoom.name}");
                // else
                //     Debug.LogWarning("[Navigator] Game Start - Could not determine initial room!");
            }
        }

        // === Public APIs ===

        /// <summary>Best current-room estimate (path-based room vote, cached).</summary>
        public RoomAnchor GetCurrentRoom() => FindCurrentRoom();

        public void GoToRoom(RoomAnchor targetRoom, Vector3? finalPoint = null, string destinationName = null, string parentName = null)
        {
            if (isDoorTransit) return;

            // CRITICAL: Find current room BEFORE planning any path
            var startRoom = FindCurrentRoom();
            if (!startRoom || !targetRoom)
            {
                if (logPlannedRoute)
                    Debug.LogWarning($"[Navigator] GoToRoom failed: startRoom={startRoom?.roomID ?? "null"}, targetRoom={targetRoom?.roomID ?? "null"}");

                // Same-room chase only: cached/known room matches destination — path inside room.
                if (startRoom == null && targetRoom != null && finalPoint.HasValue
                    && _cachedCurrentRoom == targetRoom)
                {
                    route.Clear();
                    route.Add(CreateTempPoint(finalPoint.Value, destinationName, parentName));
                    StartFollowing();
                }
                return;
            }

            route.Clear();

            string startRoomName = startRoom.roomID ?? startRoom.name;
            string targetRoomName = targetRoom.roomID ?? targetRoom.name;

            if (logPlannedRoute)
                Debug.Log($"{startRoomName}>{targetRoomName}");

            // 1) ODA AYNIYSA ve finalPoint VARSA: anchor waypointe hiç gerek yok → direkt final point
            if (startRoom == targetRoom && finalPoint.HasValue)
            {
                // if (logPlannedRoute)
                //     Debug.Log($"[Navigator] Same room, going directly to final point");
                route.Add(CreateTempPoint(finalPoint.Value, destinationName, parentName));
                StartFollowing();
                return;
            }

            // 2) Normal kapılı/kapısız rota
            if (!RoomGraph.Instance.TryGetRoute(startRoom, targetRoom, route))
            {
                // if (logPlannedRoute)
                //     Debug.LogWarning($"[Navigator] TryGetRoute FAILED: {startRoomName} -> {targetRoomName}");
                return;
            }

            if (finalPoint.HasValue)
                route.Add(CreateTempPoint(finalPoint.Value, destinationName, parentName));

            StartFollowing();
        }

        public void GoToPoint(Vector3 worldPoint, string destinationName = null, string parentName = null)
        {
            if (isDoorTransit) return; // do not re-route mid-door

            var home = HomeZoneID;
            Transform targetAnchorT = null;
            RoomAnchor targetRoom = null;

            // METHOD 1: If parentName is provided, try to find the room anchor directly by name
            // This is the most reliable method since hotspots are children of room anchors
            if (!string.IsNullOrEmpty(parentName))
            {
                var parentTransform = NPCAnchorManager.Instance.GetAnchor(parentName);
                if (parentTransform != null)
                {
                    targetRoom = parentTransform.GetComponent<RoomAnchor>();
                    if (targetRoom != null)
                    {
                        targetAnchorT = parentTransform;
                        // if (logPlannedRoute)
                        //     Debug.Log($"[Navigator] GoToPoint: Found target room by parent name: {targetRoom.roomID ?? targetRoom.name} for {destinationName ?? "point"}");
                    }
                }
            }

            // METHOD 2: Fallback to door-voting consensus if parent lookup failed
            if (targetRoom == null)
            {
                targetAnchorT = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(
                    worldPoint, home, NavMesh.AllAreas, isNpcSelf: false);

                if (targetAnchorT != null)
                {
                    targetRoom = targetAnchorT.GetComponent<RoomAnchor>();
                    // if (logPlannedRoute)
                    //     Debug.Log($"[Navigator] GoToPoint: Found target room by door-voting: {targetRoom?.roomID ?? targetRoom?.name ?? "null"} for {destinationName ?? "point"}");
                }
            }

            // If both methods failed, fall back to direct NavMesh
            if (targetRoom == null)
            {
                if (logPlannedRoute)
                    Debug.LogWarning($"[Navigator] GoToPoint: Failed to find target room for {destinationName ?? "point"}, falling back to direct NavMesh");
                if (followCo != null) StopCoroutine(followCo);
                isDoorTransit = false;
                following = true;
                followCo = StartCoroutine(MoveToReached(worldPoint, -1f, _ => following = false));
                return;
            }

            GoToRoom(targetRoom, worldPoint, destinationName, parentName);
        }

        // === Internals ===

        private void StartFollowing()
        {
            if (followCo != null) StopCoroutine(followCo);
            // StopCoroutine skips finally blocks — clear transit so chase can replan.
            isDoorTransit = false;
            idx = -1;

            if (logPlannedRoute)
                LogPlannedRoute();

            following = true;
            followCo = StartCoroutine(FollowRoute());
        }

        private IEnumerator FollowRoute()
        {
            while (true)
            {
                idx++;
                if (idx >= route.Count) break;

                Transform waypoint = route[idx];

                // Door prepoint?
                var edge = RoomGraph.Instance.GetEdgeByPrepoint(waypoint);
                if (edge != null)
                {
                    isDoorTransit = true;
                    bool crossed = false;

                    try
                    {
                        // 1) Approach entry prepoint
                        bool reachedEntry = false;
                        yield return MoveToReached(waypoint.position, -1f, ok => reachedEntry = ok);
                        if (!reachedEntry)
                        {
                            AbortRouteInPlace();
                            yield break;
                        }

                        // 2) Open and wait until NavMesh is actually free.
                        // Entering the basement: start footstep deafness when the open door SFX plays.
                        bool enteringBasement = IsEnteringRoom(edge, waypoint, footstepDeafnessEnterRoomId);

                        // Grace: Natural must not enter basement. Chase / Search / noise response may.
                        if (enteringBasement && InteractionController.IsBasementRoutineDelayed)
                        {
                            var npcCtrl = GetComponent<NPCController>();
                            if (npcCtrl == null || npcCtrl.currentState == NPCController.AIState.Natural)
                            {
                                AbortRouteInPlace();
                                yield break;
                            }
                        }

                        // Stand still at the entry prepoint so opening the carve can't slide him through.
                        if (agent != null)
                        {
                            agent.ResetPath();
                            agent.isStopped = true;
                            agent.velocity = Vector3.zero;
                        }

                        yield return EnsureDoorOpenForCrossing(edge.door, enteringBasement);

                        // Linger at the basement doorway before crossing (other doors skip this).
                        // Abort early if the player is actually visible — chase-from-noise still waits.
                        if (enteringBasement && basementDoorPostOpenHold > 0f)
                            yield return BasementDoorPostOpenHoldRoutine();

                        if (agent != null)
                            agent.isStopped = false;

                        // 3) Move to the opposite prepoint
                        int next = idx + 1;
                        if (next < route.Count && route[next] != null)
                            yield return MoveToReached(route[next].position, doorTransitTimeout, ok => crossed = ok);

                        // 4) Only seal the door if we actually reached the far side.
                        //    Closing after a failed cross is what locks the A↔B loop.
                        if (crossed && edge.door != null && edge.door.AICloseAfterPassing)
                            edge.door.RequestClose(bypassLock: true);

                        if (!crossed)
                        {
                            AbortRouteInPlace();
                            yield break;
                        }

                        // 5) Continue past the far prepoint
                        idx = next;
                    }
                    finally
                    {
                        isDoorTransit = false;
                    }

                    continue;
                }

                // Anchor waypoints are planning markers only — skip walking to them.
                if (waypoint.TryGetComponent<RoomAnchor>(out var roomAnchor) && roomAnchor != null)
                    continue;

                yield return MoveToReached(waypoint.position, -1f, _ => { });
            }

            CleanupTempPoints();
            following = false;
        }

        private IEnumerator EnsureDoorOpenForCrossing(DoorController door, bool grantFootstepDeafnessOnOpenSound = false)
        {
            if (door == null) yield break;

            var npc = grantFootstepDeafnessOnOpenSound ? GetComponent<NPCController>() : null;
            bool armed = npc != null && !door.IsOpen;
            bool granted = false;
            System.Action onOpenSound = null;

            if (armed)
            {
                onOpenSound = () =>
                {
                    if (granted) return;
                    granted = true;
                    npc.BeginIgnorePlayerFootsteps();
                };
                door.OnOpenSoundPlayed += onOpenSound;
            }

            try
            {
                float waited = 0f;
                int reRequests = 0;

                while (waited < doorOpenWaitTimeout)
                {
                    if (door.IsOpen)
                        break;

                    if (reRequests == 0 || waited > 0.4f * (reRequests + 1))
                    {
                        door.RequestOpen(bypassLock: true);
                        reRequests++;
                    }

                    waited += Time.deltaTime;
                    yield return null;
                }

                if (!door.IsOpen)
                    door.ForceOpenInstant();

                // Open SFX can fire after carve marks IsOpen (handle → open delay). Keep listening briefly.
                if (armed && !granted)
                {
                    float sfxWait = 0f;
                    const float sfxWaitTimeout = 1f;
                    while (!granted && sfxWait < sfxWaitTimeout)
                    {
                        sfxWait += Time.deltaTime;
                        yield return null;
                    }
                }
            }
            finally
            {
                if (onOpenSound != null)
                    door.OnOpenSoundPlayed -= onOpenSound;
            }
        }

        static bool IsEnteringRoom(DoorEdge edge, Transform entryPrepoint, string roomId)
        {
            if (edge == null || entryPrepoint == null || string.IsNullOrEmpty(roomId))
                return false;

            RoomAnchor destination = null;
            if (entryPrepoint == edge.prePointA)
                destination = edge.roomB;
            else if (entryPrepoint == edge.prePointB)
                destination = edge.roomA;

            return RoomMatchesId(destination, roomId);
        }

        static bool RoomMatchesId(RoomAnchor room, string roomId)
        {
            if (room == null || string.IsNullOrEmpty(roomId))
                return false;

            if (!string.IsNullOrEmpty(room.roomID) && room.roomID == roomId)
                return true;

            return room.name == roomId || room.gameObject.name == roomId;
        }

        IEnumerator BasementDoorPostOpenHoldRoutine()
        {
            if (agent != null)
            {
                agent.ResetPath();
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }

            float held = 0f;
            while (held < basementDoorPostOpenHold)
            {
                if (ShouldSkipBasementDoorHold())
                    yield break;

                held += Time.deltaTime;
                yield return null;
            }
        }

        bool ShouldSkipBasementDoorHold()
        {
            var npc = GetComponent<NPCController>();
            if (npc == null || npc.perception == null)
                return false;

            return npc.perception.CanSeePlayer;
        }

        /// <summary>
        /// Walk to pos. Invokes onFinished(true) only if the agent reached the goal;
        /// false on PathInvalid, stuck timeout, or maxSeconds exceeded.
        /// </summary>
        private IEnumerator MoveToReached(Vector3 pos, float maxSeconds, System.Action<bool> onFinished)
        {
            bool arrived = false;

            if (NavMesh.SamplePosition(pos, out var hit, 0.75f, NavMesh.AllAreas))
                pos = hit.position;

            agent.isStopped = false;
            agent.SetDestination(pos);

            float stuckTimer = 0f;
            float elapsed = 0f;
            float lastRemaining = Mathf.Infinity;
            float arriveDist = Mathf.Max(waypointTolerance, agent.stoppingDistance + 0.05f);

            while (true)
            {
                elapsed += Time.deltaTime;
                if (maxSeconds > 0f && elapsed >= maxSeconds)
                    break;

                if (agent.pathStatus == NavMeshPathStatus.PathInvalid)
                    break;

                if (!agent.pathPending && agent.remainingDistance <= arriveDist)
                {
                    arrived = true;
                    break;
                }

                if (!agent.pathPending)
                {
                    if (Mathf.Abs(agent.remainingDistance - lastRemaining) < 0.01f)
                        stuckTimer += Time.deltaTime;
                    else
                        stuckTimer = 0f;

                    lastRemaining = agent.remainingDistance;

                    if (stuckTimer > stuckTimeout)
                        break;
                }

                yield return null;
            }

            onFinished?.Invoke(arrived);
        }

        private void AbortRouteInPlace()
        {
            following = false;
            CleanupTempPoints();
            route.Clear();
            idx = -1;
            if (agent != null)
            {
                agent.isStopped = false;
                agent.ResetPath();
            }

            // Any aborted Natural walk (door fail, basement grace, stuck cross) used to leave
            // InteractionController.WaitForArrival spinning forever → no sit, no next hotspot.
            interaction?.NotifyRouteAbortedNeedNewRoutine();
        }

        private void CleanupTempPoints()
        {
            for (int i = 0; i < tempPoints.Count; i++)
                if (tempPoints[i]) Destroy(tempPoints[i].gameObject);
            tempPoints.Clear();
        }

        private RoomAnchor FindCurrentRoom()
        {
            string home = HomeZoneID ?? "";

            if (NPCAnchorManager.Instance == null)
                return _cachedCurrentRoom;

            // Use the refactored path-based room finding (checks all doors and prepoints)
            var t = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(
                transform.position, home, NavMesh.AllAreas, isNpcSelf: true);

            var room = t ? t.GetComponent<RoomAnchor>() : null;

            if (room != null)
                _cachedCurrentRoom = room;
            else if (_cachedCurrentRoom != null)
                room = _cachedCurrentRoom;

            if (logPlannedRoute)
            {
                string roomName = room?.roomID ?? room?.name ?? "null";
                //Debug.Log($"[Navigator] FindCurrentRoom: {roomName}");
            }
            return room;
        }


        private Transform CreateTempPoint(Vector3 position, string destinationName = null, string parentName = null)
        {
            string name;
            if (!string.IsNullOrEmpty(destinationName))
            {
                if (!string.IsNullOrEmpty(parentName))
                    name = $"{parentName}/{destinationName}";
                else
                    name = destinationName;
            }
            else
            {
                name = "NavTempPoint";
            }
            
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.SetParent(RoomGraph.Instance.transform, true);
            tempPoints.Add(go.transform);
            return go.transform;
        }

        private void LogPlannedRoute()
        {
            if (route == null || route.Count == 0)
            {
                Debug.Log("[Navigator] Planned route: EMPTY");
                return;
            }

            var summary = new System.Text.StringBuilder(256);
            summary.Append("<color=green>[Navigator] Route: ");

            DoorController lastDoor = null;
            bool firstToken = true;

            for (int i = 0; i < route.Count; i++)
            {
                Transform wp = route[i];
                if (!wp) continue;

                var edge = RoomGraph.Instance.GetEdgeByPrepoint(wp);

                // Kapı: aynı kapının iki prepoint'ini TEK kez yaz
                if (edge != null && edge.door != null)
                {
                    if (edge.door != lastDoor)
                    {
                        if (!firstToken) summary.Append(" > ");
                        summary.Append("DOOR:");
                        summary.Append(edge.door.name);
                        firstToken = false;
                        lastDoor = edge.door;
                    }
                    continue;
                }

                lastDoor = null;

                string token;

                if (wp.TryGetComponent<RoomAnchor>(out var room) && room != null)
                {
                    token = string.IsNullOrEmpty(room.roomID) ? wp.name : room.roomID;
                }
                else
                {
                    // FINAL / NavTempPoint / diğer noktalar
                    token = wp.name;
                }

                if (!firstToken) summary.Append(" > ");
                summary.Append(token);
                firstToken = false;
            }

            summary.Append("</color>");
            //Debug.Log(summary.ToString());
        }

        public void ClearRoute()
        {
            if (followCo != null)
            {
                StopCoroutine(followCo);
                followCo = null;
            }

            following = false;
            route.Clear();
            idx = -1;
            isDoorTransit = false;
            if (agent != null)
            {
                agent.isStopped = false;
                agent.ResetPath();
            }
        }

        /// <summary>
        /// Chase / urgent pursuit: no room graph, no door prepoints.
        /// </summary>
        public void SetDestinationDirect(Vector3 worldPoint)
        {
            ClearRoute();

            if (NavMesh.SamplePosition(worldPoint, out var hit, 1.5f, NavMesh.AllAreas))
                worldPoint = hit.position;

            agent.isStopped = false;
            agent.SetDestination(worldPoint);
        }

    }
    


}