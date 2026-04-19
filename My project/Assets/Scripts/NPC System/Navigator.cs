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

        [Header("DEBUG")]
        [SerializeField] bool logPlannedRoute = true;

        private NavMeshAgent agent;
        private InteractionController interaction;

        private readonly List<Transform> route = new();
        private readonly List<Transform> tempPoints = new();
        private int idx = -1;
        private Coroutine followCo;

        // Door-transit lock: prevents replans while crossing
        private bool isDoorTransit;
        public bool IsInDoorTransit => isDoorTransit;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            interaction = GetComponent<InteractionController>();
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

        public void GoToRoom(RoomAnchor targetRoom, Vector3? finalPoint = null, string destinationName = null, string parentName = null)
        {
            if (isDoorTransit) return;

            // CRITICAL: Find current room BEFORE planning any path
            var startRoom = FindCurrentRoom();
            if (!startRoom || !targetRoom)
            {
                if (logPlannedRoute)
                    Debug.LogWarning($"[Navigator] GoToRoom failed: startRoom={startRoom?.roomID ?? "null"}, targetRoom={targetRoom?.roomID ?? "null"}");
                return;
            }

            route.Clear();

            string startRoomName = startRoom.roomID ?? startRoom.name;
            string targetRoomName = targetRoom.roomID ?? targetRoom.name;

            if (logPlannedRoute)
                //Debug.Log($"[Navigator] GoToRoom: {startRoomName} -> {targetRoomName}");

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

            if (logPlannedRoute)
                //Debug.Log($"[Navigator] TryGetRoute SUCCESS: route has {route.Count} waypoints before adding final point");

            if (finalPoint.HasValue)
                route.Add(CreateTempPoint(finalPoint.Value, destinationName, parentName));

            StartFollowing();
        }

        public void GoToPoint(Vector3 worldPoint, string destinationName = null, string parentName = null)
        {
            if (isDoorTransit) return; // do not re-route mid-door

            var home = interaction?.data?.homeID;
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
                    worldPoint, home);

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
                followCo = StartCoroutine(MoveTo(worldPoint));
                return;
            }

            GoToRoom(targetRoom, worldPoint, destinationName, parentName);
        }

        // === Internals ===

        private void StartFollowing()
        {
            if (followCo != null) StopCoroutine(followCo);
            idx = -1;

            if (logPlannedRoute)
                LogPlannedRoute();

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

                    // 1) Approach entry prepoint
                    yield return MoveTo(waypoint.position);

                    // 2) Open and wait until actually open (avoids race with carve delay)
                    if (!edge.door.IsOpen && !edge.door.isLocked)
                    {
                        bool opened = false;
                        edge.door.RequestOpen(ok => opened = ok);

                        float wait = 0f;
                        while (!opened && wait < 0.2f) // safety cap
                        {
                            wait += Time.deltaTime;
                            yield return null;
                        }
                    }

                    // 3) Move to the opposite prepoint (next element)
                    int next = idx + 1;
                    if (next < route.Count)
                        yield return MoveTo(route[next].position);

                    // 4) Continue from whatever follows the far prepoint
                    idx = next;
                    isDoorTransit = false;
                    continue;
                }

                // ✅ Anchor kontrolü - anchor'lara gitme, sadece prepoint'ler ve final point'e git
                if (waypoint.TryGetComponent<RoomAnchor>(out var roomAnchor) && roomAnchor != null)
                {
                    // // Bu bir anchor, atla (anchor'lar sadece route planlama için kullanılıyor)
                    // if (logPlannedRoute)
                    //     Debug.Log($"[Navigator] Skipping anchor waypoint: {roomAnchor.roomID ?? roomAnchor.name}");
                    continue;
                }

                // Normal waypoint (prepoint veya final point)
                yield return MoveTo(waypoint.position);
            }

            // Cleanup temp points created for this plan
            for (int i = 0; i < tempPoints.Count; i++)
                if (tempPoints[i]) Destroy(tempPoints[i].gameObject);
            tempPoints.Clear();
        }

        private IEnumerator MoveTo(Vector3 pos)
        {
            // Sample onto NavMesh to avoid invalid goals at doorway edges
            if (NavMesh.SamplePosition(pos, out var hit, 0.75f, NavMesh.AllAreas))
                pos = hit.position;

            agent.SetDestination(pos);

            float timer = 0f;
            float lastRemaining = Mathf.Infinity;

            while (true)
            {
                if (agent.pathStatus == NavMeshPathStatus.PathInvalid) yield break;

                if (!agent.pathPending &&
                    agent.remainingDistance <= Mathf.Max(waypointTolerance, agent.stoppingDistance + 0.05f))
                    yield break;

                // Simple stuck detection
                if (!agent.pathPending)
                {
                    if (Mathf.Abs(agent.remainingDistance - lastRemaining) < 0.01f)
                        timer += Time.deltaTime;
                    else
                        timer = 0f;

                    lastRemaining = agent.remainingDistance;

                    if (timer > stuckTimeout) yield break; // give up; caller re-plans soon
                }

                yield return null;
            }
        }

        private RoomAnchor FindCurrentRoom()
        {
            string home = interaction?.data?.homeID ?? "";

            // Use the refactored path-based room finding (checks all doors and prepoints)
            var t = NPCAnchorManager.Instance.FindClosestOwnedAnchorByPath(
                transform.position, home);

            var room = t ? t.GetComponent<RoomAnchor>() : null;

            if (logPlannedRoute)
            {
                string roomName = room?.roomID ?? room?.name ?? "null";
                //Debug.Log($"[Navigator] FindCurrentRoom: {roomName}");
            }

            return room; // No fallback - system must work correctly
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
            Debug.Log(summary.ToString());
        }

    }


}