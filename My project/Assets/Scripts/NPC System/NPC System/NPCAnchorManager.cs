using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using FS_Atmo;

public class NPCAnchorManager : MonoBehaviour
{
    public static NPCAnchorManager Instance { get; private set; }

    [Header("Assign anchor roots here (e.g., RoomAnchors or Zone roots):")]
    [SerializeField] private Transform[] anchorRoots;

    [Header("Player room vote")]
    [Tooltip("Player/target votes only: ignore room anchors whose Y differs from the grounded floor by more than this. Separates Basement≈-2.6 from Corridor-Kitchen≈0.")]
    [SerializeField] float maxFloorYDelta = 1.0f;

    [Header("Door crossing")]
    [Tooltip("A path segment only counts as passing a doorway when its height comes within this of the doorway's PrePoints. Keeps outdoor paths over the basement from 'crossing' basement doors below them.")]
    [SerializeField] float doorCrossMaxYDelta = 1.5f;
    [Tooltip("Half the width of a doorway: a path crossing a door's plane closer than this to the doorway centre went through the door.")]
    [SerializeField] float doorwayHalfWidth = 1.25f;

    [Header("DEBUG")]
    [SerializeField] bool logRoomVoteResult = true;

    private readonly Dictionary<string, Transform> anchorMap = new();
    private readonly Dictionary<string, List<RoomAnchor>> roomAnchorsByZone = new();

    Transform _lastRoomNpc;
    Transform _lastRoomPlayer;

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
        _lastRoomNpc = null;
        _lastRoomPlayer = null;

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
    /// Immediate = NavMesh path completes, does not pass the opposite PrePoint on that door,
    /// and does not cross any other door opening. Room comes from RoomGraph edge sides.
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

        // Player/target only: grounded collision floor. Open doors must not let an upstairs
        // room win just because a through-door NavMesh path became complete.
        // Not applied to the butcher — doorway Y is ambiguous and caused open/close thrash.
        float playerFloorY = 0f;
        bool clampToPlayerFloor = !isNpcSelf;
        if (clampToPlayerFloor)
            playerFloorY = ProjectToFloor(fromPosition).y;

        if (!NavMesh.SamplePosition(fromPosition, out var startHit, 1.0f, areaMask))
            return LogVoteResult(null, isNpcSelf);
        fromPosition = startHit.position;

        RoomAnchor bestRoom = null;
        float bestPathLength = float.PositiveInfinity;

        foreach (var edge in RoomGraph.Instance.edges)
        {
            if (!edge.prePointA || !edge.prePointB) continue;

            // PrePointA → roomA
            if (edge.roomA != null && edge.roomA.zoneID == ownerZoneID
                && (!clampToPlayerFloor || IsSameFloor(edge.roomA.transform.position.y, playerFloorY)))
            {
                if (TryImmediatePrepointPath(fromPosition, edge.prePointA, edge.roomA, areaMask, path, crossThreshold, out float len)
                    && len < bestPathLength)
                {
                    bestPathLength = len;
                    bestRoom = edge.roomA;
                }
            }

            // PrePointB → roomB
            if (edge.roomB != null && edge.roomB.zoneID == ownerZoneID
                && (!clampToPlayerFloor || IsSameFloor(edge.roomB.transform.position.y, playerFloorY)))
            {
                if (TryImmediatePrepointPath(fromPosition, edge.prePointB, edge.roomB, areaMask, path, crossThreshold, out float len)
                    && len < bestPathLength)
                {
                    bestPathLength = len;
                    bestRoom = edge.roomB;
                }
            }
        }

        // Isolated outdoor rooms (e.g. Dog Pen) have no door edges — vote by nearest room in zone.
        if (bestRoom == null
            && roomAnchorsByZone.TryGetValue(ownerZoneID, out var zoneRooms)
            && zoneRooms != null && zoneRooms.Count > 0)
        {
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < zoneRooms.Count; i++)
            {
                var room = zoneRooms[i];
                if (room == null) continue;
                if (clampToPlayerFloor && !IsSameFloor(room.transform.position.y, playerFloorY))
                    continue;

                float d = (room.transform.position - fromPosition).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestRoom = room;
                }
            }
        }

        // A failed target vote falls back to the last result, which any caller may have written
        // (search spots, ambush points). Never hand back a room from another floor — over the
        // basement that sent the chase down the basement stairs.
        if (bestRoom == null && clampToPlayerFloor
            && _lastRoomPlayer != null && !IsSameFloor(_lastRoomPlayer.position.y, playerFloorY))
            return null;

        return LogVoteResult(bestRoom != null ? bestRoom.transform : null, isNpcSelf);
    }

    static Vector3 ProjectToFloor(Vector3 origin)
    {
        return Physics.Raycast(origin + Vector3.up * 0.3f, Vector3.down, out var hit, 5f,
                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point
            : origin;
    }

    bool IsSameFloor(float a, float b) => Mathf.Abs(a - b) <= maxFloorYDelta;

    /// <summary>
    /// Immediate = complete path to this prepoint without going through its opposite PrePoint
    /// (same door) or any other doorway, and already on this prepoint's side. Shortest wins.
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

        // Must be clearly on this prepoint's side of its door. Open doors used to make the
        // far-side room look immediate (path only crossed the skipped target door).
        if (!IsClearlySameSideOfPrepointDoor(from, prepoint))
            return false;

        // The side test above is a plane through the whole map: outdoors, everywhere past the basement
        // door's line reads as "basement side", and with the door open the path just walks round and in.
        // A path that goes through this prepoint's own doorway started in the other room.
        if (PathGoesThroughOwnDoorway(path, prepoint))
            return false;

        // Same door: path that goes past the opposite PrePoint means we crossed the doorway
        // (e.g. basement → Cage PrePointB via PrePointA). Do NOT use the A–B door line —
        // every path ends on that line and would reject all candidates.
        if (PathPassesNearOppositePrepoint(path, prepoint, crossThreshold))
            return false;

        // Goes through another room's doorway → not immediate.
        if (PathPassesThroughOtherPrepoint(path, prepoint, crossThreshold, candidateRoom))
            return false;

        pathLength = GetPathLength(path);
        return true;
    }

    /// <summary>
    /// True if the path comes within <paramref name="threshold"/> of the other PrePoint on the
    /// same door edge. That means reaching the target required crossing through the doorway.
    /// </summary>
    bool PathPassesNearOppositePrepoint(NavMeshPath path, Transform targetPrepoint, float threshold)
    {
        if (path.corners == null || path.corners.Length < 2)
            return false;
        if (RoomGraph.Instance == null || RoomGraph.Instance.edges == null)
            return false;

        Transform opposite = null;
        foreach (var edge in RoomGraph.Instance.edges)
        {
            if (edge.prePointA == targetPrepoint)
            {
                opposite = edge.prePointB;
                break;
            }
            if (edge.prePointB == targetPrepoint)
            {
                opposite = edge.prePointA;
                break;
            }
        }

        if (!opposite)
            return false;

        Vector2 opp = new Vector2(opposite.position.x, opposite.position.z);

        // Skip the final corner (the target). Only earlier geometry counts as "passing through".
        for (int i = 0; i < path.corners.Length - 1; i++)
        {
            if (!SegmentNearHeight(path.corners[i], path.corners[i + 1], opposite.position.y))
                continue;

            Vector2 a = new Vector2(path.corners[i].x, path.corners[i].z);
            Vector2 b = new Vector2(path.corners[i + 1].x, path.corners[i + 1].z);

            // Last segment ends at the target — still check it, but the opposite is not the end.
            if (PointToLineSegmentDistance(opp, a, b) < threshold)
                return true;
        }

        return false;
    }

    /// <summary>
    /// preA/preB sit on opposite sides of a door; (preB-preA) points through the doorway.
    /// True only when <paramref name="from"/> is strictly on the prepoint's room-side
    /// (opposite signs or Dot≈0 on the door plane both fail).
    /// </summary>
    static bool IsClearlySameSideOfPrepointDoor(Vector3 from, Transform prepoint)
    {
        if (RoomGraph.Instance == null || RoomGraph.Instance.edges == null)
            return true;

        DoorEdge edge = null;
        foreach (var e in RoomGraph.Instance.edges)
        {
            if (e.prePointA == prepoint || e.prePointB == prepoint)
            {
                edge = e;
                break;
            }
        }

        if (edge == null || !edge.prePointA || !edge.prePointB)
            return true;

        Vector3 a = edge.prePointA.position;
        Vector3 b = edge.prePointB.position;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 through = b - a;
        if (through.sqrMagnitude < 0.0001f)
            return true;

        float sideFrom = Vector3.Dot(from - mid, through);
        float sidePrep = Vector3.Dot(prepoint.position - mid, through);
        return sideFrom * sidePrep > 0f;
    }

    /// <summary>
    /// True if a path segment crosses the door plane of <paramref name="prepoint"/>'s edge within
    /// <see cref="doorwayHalfWidth"/> of the doorway centre, at the doorway's height.
    /// </summary>
    bool PathGoesThroughOwnDoorway(NavMeshPath path, Transform prepoint)
    {
        if (path.corners == null || path.corners.Length < 2)
            return false;
        if (RoomGraph.Instance == null || RoomGraph.Instance.edges == null)
            return false;

        DoorEdge edge = null;
        foreach (var e in RoomGraph.Instance.edges)
        {
            if (e.prePointA == prepoint || e.prePointB == prepoint)
            {
                edge = e;
                break;
            }
        }
        if (edge == null || !edge.prePointA || !edge.prePointB)
            return false;

        Vector3 mid = (edge.prePointA.position + edge.prePointB.position) * 0.5f;
        Vector3 through = edge.prePointB.position - edge.prePointA.position;
        through.y = 0f;
        if (through.sqrMagnitude < 0.0001f)
            return false;
        through.Normalize();

        var corners = path.corners;
        for (int i = 0; i < corners.Length - 1; i++)
        {
            float sp = Vector3.Dot(corners[i] - mid, through);
            float sq = Vector3.Dot(corners[i + 1] - mid, through);
            if (sp * sq >= 0f)
                continue;

            Vector3 hit = Vector3.Lerp(corners[i], corners[i + 1], sp / (sp - sq));
            Vector3 lateral = hit - mid;
            lateral.y = 0f;
            if (lateral.magnitude <= doorwayHalfWidth && Mathf.Abs(hit.y - mid.y) <= doorCrossMaxYDelta)
                return true;
        }

        return false;
    }

    Transform LogVoteResult(Transform roomT, bool isNpcSelf)
    {
        if (roomT != null)
        {
            if (isNpcSelf) _lastRoomNpc = roomT;
            else _lastRoomPlayer = roomT;
        }
        else
        {
            roomT = isNpcSelf ? _lastRoomNpc : _lastRoomPlayer;
        }

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
            //Debug.Log($"<color={color}>{who}: {name}</color>");
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

                if (!SegmentNearHeight(segmentStart, segmentEnd, (doorStart.y + doorEnd.y) * 0.5f))
                    continue;

                if (LineSegmentsIntersect(segmentStart, segmentEnd, doorStart, doorEnd, threshold))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when the segment's height range comes within <see cref="doorCrossMaxYDelta"/> of <paramref name="y"/>.
    /// The crossing tests below are XZ-only; this keeps them to doorways on the path's own floor.
    /// </summary>
    bool SegmentNearHeight(Vector3 a, Vector3 b, float y)
    {
        float lo = Mathf.Min(a.y, b.y) - doorCrossMaxYDelta;
        float hi = Mathf.Max(a.y, b.y) + doorCrossMaxYDelta;
        return y >= lo && y <= hi;
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