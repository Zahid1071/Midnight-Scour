// Navigator.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FS_Atmo
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class Navigator : MonoBehaviour
    {
        [Header("Search radii (≤0 = unlimited)")]
        [SerializeField] float currentRoomSearchRadius = 0;   // was 10f
        [SerializeField] float targetRoomSearchRadius  = 0;   // was 30f

        [Header("Movement")]
        [SerializeField] float waypointTolerance = 0.2f;      // use >= agent.stoppingDistance
        [SerializeField] float stuckTimeout      = 3f;        // seconds without progress

        private NavMeshAgent agent;
        private InteractionController interaction;

        private readonly List<Transform> route = new();
        private readonly List<Transform> tempPoints = new();
        private int idx = -1;
        private Coroutine followCo;
        private RoomAnchor lastKnownRoom;

        // Door-transit lock: prevents replans while crossing
        private bool isDoorTransit;
        public  bool IsInDoorTransit => isDoorTransit;

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

        public void GoToRoom(RoomAnchor targetRoom, Vector3? finalPoint = null)
        {
            if (isDoorTransit) return;

            var startRoom = FindCurrentRoom();
            if (!startRoom || !targetRoom) return;

            route.Clear();

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
                route.Add(CreateTempPoint(finalPoint.Value));

            StartFollowing();
        }


        public void GoToPoint(Vector3 worldPoint)
        {
            if (isDoorTransit) return; // do not re-route mid-door

            var home = interaction?.data?.homeID;
            var targetAnchorT = NPCAnchorManager.Instance.FindClosestOwnedAnchor(
                worldPoint, RadiusOrHuge(targetRoomSearchRadius), home);

            // If even that fails, just go direct to be safe.
            if (!targetAnchorT)
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

            GoToRoom(targetAnchorT.GetComponent<RoomAnchor>(), worldPoint);
        }

        /// <summary>
        /// Optional utility if you still want timed chase replans elsewhere (not required).
        /// </summary>
        // public void Pursue(Transform target, float replanInterval = 0.25f)
        // {
        //     if (followCo != null) StopCoroutine(followCo);
        //     followCo = StartCoroutine(PursueCo(target, replanInterval));
        // }

        // === Internals ===

        private void StartFollowing()
        {
            if (followCo != null) StopCoroutine(followCo);
            idx = -1;
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

        private IEnumerator PursueCo(Transform target, float interval)
        {
            while (target != null)
            {
                if (!isDoorTransit) // respect transit lock
                    GoToPoint(target.position);

                yield return new WaitForSeconds(interval);
            }
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

            // 1) Check nearby doors first (cheap, helps when standing at a doorway)
            foreach (var edge in RoomGraph.Instance.edges)
            {
                if (!edge.prePointA || !edge.prePointB) continue;

                float distA = Vector3.Distance(transform.position, edge.prePointA.position);
                float distB = Vector3.Distance(transform.position, edge.prePointB.position);

                if (distA < 1.0f || distB < 1.0f)
                    return (distA < distB) ? edge.roomA : edge.roomB;
            }

            // 2) If not near any doorway, fall back to "closest owned anchor" logic
            var t = NPCAnchorManager.Instance.FindClosestOwnedAnchor(
                transform.position, RadiusOrHuge(currentRoomSearchRadius), home);

            var room = t ? t.GetComponent<RoomAnchor>() : null;
            if (room) lastKnownRoom = room;

            if (logPlannedRoute)
            {
                string roomName = room?.roomID ?? room?.name ?? "null";
                //Debug.Log($"[Navigator] FindCurrentRoom: {roomName}");
            }

            return room; // No fallback - system must work correctly
        }

        private float RadiusOrHuge(float r) => (r <= 0f ? 1e6f : r);

        private Transform CreateTempPoint(Vector3 position)
        {
            var go = new GameObject("NavTempPoint");
            go.transform.position = position;
            go.transform.SetParent(RoomGraph.Instance.transform, true);
            tempPoints.Add(go.transform);
            return go.transform;
        }
    }
}
