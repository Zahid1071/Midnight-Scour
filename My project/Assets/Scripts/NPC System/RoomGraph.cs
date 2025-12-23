using System.Collections.Generic;
using UnityEngine;

namespace FS_Atmo
{
    [System.Serializable]
    public class DoorEdge
    {
        public RoomAnchor roomA;
        public RoomAnchor roomB;
        public DoorController door;
        public Transform prePointA;
        public Transform prePointB;
    }

    /// <summary>
    /// Manual room↔door graph. No runtime scanning. Author once in the Inspector.
    /// Provides door-aware routes as a sequence of prepoints ending at the target room anchor.
    /// </summary>
    public class RoomGraph : MonoBehaviour
    {
        public static RoomGraph Instance { get; private set; }

        [Tooltip("One element per physical door in the scene.")]
        public List<DoorEdge> edges = new();

        // adjacency per room
        private readonly Dictionary<RoomAnchor, List<DoorEdge>> _adj = new();
        // map prepoint -> edge (so Navigator can detect if a waypoint is a door side)
        private readonly Dictionary<Transform, DoorEdge> _prepointToEdge = new();

        void Awake()
        {
            Instance = this;
            BuildAdjacency();
        }

#if UNITY_EDITOR
        // Keep data fresh while editing
        void OnValidate()
        {
            if (!Application.isPlaying)
                BuildAdjacency();
        }
#endif

        private void BuildAdjacency()
        {
            _adj.Clear();
            _prepointToEdge.Clear();

            foreach (var e in edges)
            {
                if (!e?.roomA || !e.roomB || !e.door || !e.prePointA || !e.prePointB)
                    continue;

                if (!_adj.ContainsKey(e.roomA)) _adj[e.roomA] = new List<DoorEdge>();
                if (!_adj.ContainsKey(e.roomB)) _adj[e.roomB] = new List<DoorEdge>();
                _adj[e.roomA].Add(e);
                _adj[e.roomB].Add(e);

                _prepointToEdge[e.prePointA] = e;
                _prepointToEdge[e.prePointB] = e;
            }
        }

        /// <summary>
        /// Returns a door-aware route as a sequence of waypoints:
        /// [prePoint(from side), prePoint(to side), ... , targetRoom.anchor.transform]
        /// </summary>
        public bool TryGetRoute(RoomAnchor startRoom, RoomAnchor targetRoom, List<Transform> outWaypoints)
        {
            outWaypoints.Clear();
            if (!startRoom || !targetRoom) return false;
            if (startRoom == targetRoom)
            {
                outWaypoints.Add(targetRoom.transform);
                return true;
            }

            // BFS over rooms
            var prev = new Dictionary<RoomAnchor, (RoomAnchor prevRoom, DoorEdge via)>();
            var q    = new Queue<RoomAnchor>();
            var seen = new HashSet<RoomAnchor>();
            q.Enqueue(startRoom);
            seen.Add(startRoom);

            while (q.Count > 0)
            {
                var r = q.Dequeue();
                if (!_adj.TryGetValue(r, out var list)) continue;

                foreach (var edge in list)
                {
                    var nb = (edge.roomA == r) ? edge.roomB : edge.roomA;
                    if (!seen.Add(nb)) continue;

                    prev[nb] = (r, edge);
                    if (nb == targetRoom) goto BUILD;
                    q.Enqueue(nb);
                }
            }
            return false;

        BUILD:
            // unwind target -> start to collect door edges
            var seq = new List<(RoomAnchor from, DoorEdge edge, RoomAnchor to)>();
            var cur = targetRoom;
            while (cur != startRoom)
            {
                var (p, e) = prev[cur];
                seq.Add((p, e, cur));
                cur = p;
            }
            seq.Reverse();

            // convert to concrete prepoints + final target anchor
            foreach (var step in seq)
            {
                var e = step.edge;
                if (step.from == e.roomA)
                {
                    outWaypoints.Add(e.prePointA);
                    outWaypoints.Add(e.prePointB);
                }
                else
                {
                    outWaypoints.Add(e.prePointB);
                    outWaypoints.Add(e.prePointA);
                }
            }
            outWaypoints.Add(targetRoom.transform);
            return true;
        }

        public DoorEdge GetEdgeByPrepoint(Transform prepoint) =>
            prepoint && _prepointToEdge.TryGetValue(prepoint, out var e) ? e : null;

        // Optional: simple gizmos for authoring
        void OnDrawGizmosSelected()
        {
            if (edges == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            foreach (var e in edges)
            {
                if (!e?.roomA || !e.roomB) continue;
                Gizmos.DrawLine(e.roomA.transform.position + Vector3.up * 0.1f,
                                e.roomB.transform.position + Vector3.up * 0.1f);
                if (e.prePointA) Gizmos.DrawSphere(e.prePointA.position + Vector3.up * 0.05f, 0.05f);
                if (e.prePointB) Gizmos.DrawSphere(e.prePointB.position + Vector3.up * 0.05f, 0.05f);
            }
        }
    }
}
