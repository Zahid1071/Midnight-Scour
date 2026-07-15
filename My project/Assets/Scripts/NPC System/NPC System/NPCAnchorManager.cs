using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using FS_Atmo;

public class NPCAnchorManager : MonoBehaviour
{
    public static NPCAnchorManager Instance { get; private set; }

    [Header("Assign anchor roots here (e.g., RoomAnchors or Zone roots):")]
    [SerializeField] private Transform[] anchorRoots;

    [Header("DEBUG")]
    [SerializeField] bool logRoomVoteResult = true;

    private readonly Dictionary<string, Transform> anchorMap = new();
    private readonly Dictionary<string, List<RoomAnchor>> roomAnchorsByZone = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        anchorMap.Clear();
        roomAnchorsByZone.Clear();

        foreach (var root in anchorRoots)
            RegisterHierarchy(root);
    }

    private void RegisterHierarchy(Transform t)
    {
        if (t == null) return;
        if (t.hideFlags != HideFlags.None || !t.gameObject.scene.IsValid()) return;

        if (!anchorMap.ContainsKey(t.name))
            anchorMap[t.name] = t;

        // Cache RoomAnchors by zone (so we don't GetComponent in loops later)
        if (t.TryGetComponent<RoomAnchor>(out var room))
        {
            if (!roomAnchorsByZone.TryGetValue(room.zoneID, out var list))
            {
                list = new List<RoomAnchor>(32);
                roomAnchorsByZone.Add(room.zoneID, list);
            }
            list.Add(room);
        }

        foreach (Transform child in t)
            RegisterHierarchy(child);
    }

    public Transform GetAnchor(string id)
    {
        anchorMap.TryGetValue(id, out var result);
        return result;
    }

    /// <summary>All room anchor transforms registered for a zone (for ambush / patrol spot scans).</summary>
    public void GetRoomAnchorsInZone(string zoneID, List<Transform> results)
    {
        results.Clear();
        if (string.IsNullOrEmpty(zoneID) || !roomAnchorsByZone.TryGetValue(zoneID, out var rooms))
            return;

        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i] != null)
                results.Add(rooms[i].transform);
        }
    }

    /// <summary>
    /// Room identity = room of the closest IMMEDIATE prepoint.
    /// Immediate = NavMesh path completes AND does not cross any other door opening
    /// (excludes middleman/transit through other rooms). Room comes from RoomGraph edge sides.
    /// </summary>
    /// <param name="isNpcSelf">
    /// True when voting the NPC's own position (Butcher) — logs red.
    /// False when voting a target/player/sound position — logs green.
    /// </param>
    public Transform FindClosestOwnedAnchorByPath(
        Vector3 fromPosition,
        string ownerZoneID,
        int areaMask = NavMesh.AllAreas,
        bool isNpcSelf = false)
    {
        if (string.IsNullOrEmpty(ownerZoneID))
            return LogVoteResult(null, isNpcSelf);

        if (RoomGraph.Instance == null || RoomGraph.Instance.edges == null)
            return LogVoteResult(null, isNpcSelf);

        var path = new NavMeshPath();
        const float crossThreshold = 0.5f;

        if (!NavMesh.SamplePosition(fromPosition, out var startHit, 1.0f, areaMask))
            return LogVoteResult(null, isNpcSelf);
        fromPosition = startHit.position;

        RoomAnchor bestRoom = null;
        float bestPathLength = float.PositiveInfinity;

        foreach (var edge in RoomGraph.Instance.edges)
        {
            if (!edge.prePointA || !edge.prePointB || !edge.door) continue;

            // PrePointA → roomA
            if (edge.roomA != null && edge.roomA.zoneID == ownerZoneID)
            {
                if (TryImmediatePrepointPath(fromPosition, edge.prePointA, edge.roomA, areaMask, path, crossThreshold, out float len)
                    && len < bestPathLength)
                {
                    bestPathLength = len;
                    bestRoom = edge.roomA;
                }
            }

            // PrePointB → roomB
            if (edge.roomB != null && edge.roomB.zoneID == ownerZoneID)
            {
                if (TryImmediatePrepointPath(fromPosition, edge.prePointB, edge.roomB, areaMask, path, crossThreshold, out float len)
                    && len < bestPathLength)
                {
                    bestPathLength = len;
                    bestRoom = edge.roomB;
                }
            }
        }

        return LogVoteResult(bestRoom != null ? bestRoom.transform : null, isNpcSelf);
    }

    /// <summary>
    /// True if we can path to this prepoint without crossing another door (middleman filter).
    /// </summary>
    bool TryImmediatePrepointPath(
        Vector3 from,
        Transform prepoint,
        RoomAnchor candidateRoom,
        int areaMask,
        NavMeshPath path,
        float crossThreshold,
        out float pathLength)
    {
        pathLength = float.PositiveInfinity;
        if (!prepoint) return false;

        Vector3 goal = prepoint.position;
        if (!NavMesh.SamplePosition(goal, out var hit, 1.0f, areaMask))
            return false;
        goal = hit.position;

        if (!NavMesh.CalculatePath(from, goal, areaMask, path))
            return false;
        if (path.status != NavMeshPathStatus.PathComplete)
            return false;

        // Goes through another room's doorway → not immediate.
        if (PathPassesThroughOtherPrepoint(path, prepoint, crossThreshold, candidateRoom))
            return false;

        pathLength = GetPathLength(path);
        return true;
    }

    Transform LogVoteResult(Transform roomT, bool isNpcSelf)
    {
        if (logRoomVoteResult)
        {
            string name = "?";
            if (roomT != null)
            {
                var room = roomT.GetComponent<RoomAnchor>();
                name = room != null
                    ? (string.IsNullOrEmpty(room.roomID) ? room.name : room.roomID)
                    : roomT.name;
            }
            // Butcher (NPC self) = red, player/target/sound = green
            string color = isNpcSelf ? "red" : "green";
            string who = isNpcSelf ? "Butcher" : "Player";
            Debug.Log($"<color={color}>{who}: {name}</color>");
        }
        return roomT;
    }

    /// <summary>
    /// Checks if a NavMesh path passes through another door's opening (line between prepoints).
    /// Only doors that are NOT connected to <paramref name="candidateRoom"/> count — sibling
    /// doors of the same room (e.g. cage + corridor both opening into basement) must not
    /// disqualify paths that simply cross the open floor between them.
    /// </summary>
    private bool PathPassesThroughOtherPrepoint(
        NavMeshPath path,
        Transform targetPrepoint,
        float threshold,
        RoomAnchor candidateRoom)
    {
        if (path.corners == null || path.corners.Length < 2)
            return false;

        DoorEdge targetEdge = null;
        foreach (var edge in RoomGraph.Instance.edges)
        {
            if (edge.prePointA == targetPrepoint || edge.prePointB == targetPrepoint)
            {
                targetEdge = edge;
                break;
            }
        }

        for (int i = 0; i < path.corners.Length - 1; i++)
        {
            Vector3 segmentStart = path.corners[i];
            Vector3 segmentEnd = path.corners[i + 1];

            foreach (var edge in RoomGraph.Instance.edges)
            {
                if (edge == targetEdge) continue;
                if (!edge.prePointA || !edge.prePointB) continue;

                // Another immediate door of the same room — not evidence we left that room.
                if (candidateRoom != null
                    && (edge.roomA == candidateRoom || edge.roomB == candidateRoom))
                    continue;

                Vector3 doorStart = edge.prePointA.position;
                Vector3 doorEnd = edge.prePointB.position;

                if (LineSegmentsIntersect(segmentStart, segmentEnd, doorStart, doorEnd, threshold))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if two line segments intersect (with threshold for near-misses).
    /// Projects to XZ plane (ignores Y-axis for door crossings).
    /// </summary>
    private bool LineSegmentsIntersect(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4, float threshold)
    {
        // Project to XZ plane (ignore Y for door crossings)
        Vector2 a = new Vector2(p1.x, p1.z);
        Vector2 b = new Vector2(p2.x, p2.z);
        Vector2 c = new Vector2(p3.x, p3.z);
        Vector2 d = new Vector2(p4.x, p4.z);
        
        // Check if segments intersect or are very close
        return LineSegmentsIntersect2D(a, b, c, d, threshold);
    }

    /// <summary>
    /// Checks if two 2D line segments intersect or are within threshold distance of each other.
    /// </summary>
    private bool LineSegmentsIntersect2D(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4, float threshold)
    {
        // Check if any point on segment p1-p2 is within threshold of segment p3-p4
        float dist1 = PointToLineSegmentDistance(p1, p3, p4);
        float dist2 = PointToLineSegmentDistance(p2, p3, p4);
        float dist3 = PointToLineSegmentDistance(p3, p1, p2);
        float dist4 = PointToLineSegmentDistance(p4, p1, p2);
        
        return Mathf.Min(dist1, dist2, dist3, dist4) < threshold;
    }

    /// <summary>
    /// Calculates the distance from a point to a line segment.
    /// </summary>
    private float PointToLineSegmentDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        Vector2 line = lineEnd - lineStart;
        float lineLength = line.magnitude;
        if (lineLength < 0.001f) return Vector2.Distance(point, lineStart);
        
        Vector2 lineNorm = line / lineLength;
        Vector2 toPoint = point - lineStart;
        float projection = Vector2.Dot(toPoint, lineNorm);
        projection = Mathf.Clamp(projection, 0f, lineLength);
        
        Vector2 closestPoint = lineStart + lineNorm * projection;
        return Vector2.Distance(point, closestPoint);
    }

    private static float GetPathLength(NavMeshPath path)
    {
        var corners = path.corners;
        if (corners == null || corners.Length < 2) return float.PositiveInfinity;

        float sum = 0f;
        for (int i = 1; i < corners.Length; i++)
            sum += Vector3.Distance(corners[i - 1], corners[i]);
        return sum;
    }
}