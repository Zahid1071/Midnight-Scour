using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class NPCAnchorManager : MonoBehaviour
{
    public static NPCAnchorManager Instance { get; private set; }

    [Header("Assign anchor roots here (e.g., RoomAnchors or Zone roots):")]
    [SerializeField] private Transform[] anchorRoots;

    private Dictionary<string, Transform> anchorMap = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        anchorMap.Clear();

        foreach (var root in anchorRoots)
            RegisterHierarchy(root);

        // NOTE: modern API (not deprecated)
    }

    private void RegisterHierarchy(Transform t)
    {
        if (t == null) return;
        if (t.hideFlags != HideFlags.None || !t.gameObject.scene.IsValid()) return;

        if (!anchorMap.ContainsKey(t.name))
            anchorMap[t.name] = t;

        foreach (Transform child in t)
            RegisterHierarchy(child);
    }

    public Transform FindClosestOwnedAnchor(Vector3 position, float maxDistance, string ownerZoneID)
    {
        Transform closest = null;
        float closestDistance = maxDistance;

        foreach (var kvp in anchorMap)
        {
            Transform t = kvp.Value;
            RoomAnchor anchor = t.GetComponent<RoomAnchor>();
            if (anchor == null) continue;
            if (!anchor.zoneID.Equals(ownerZoneID)) continue;

            float dist = Vector3.Distance(position, t.position);
            if (dist < closestDistance)
            {
                closest = t;
                closestDistance = dist;
            }
        }
        return closest;
    }

    public Transform GetAnchor(string id)
    {
        anchorMap.TryGetValue(id, out var result);
        return result;
    }

    public List<Transform> GetClosestAnchorsInZone(string zoneID, Vector3 center, int maxCount)
    {
        return anchorMap.Values
            .Where(t =>
            {
                prepointA = hitA.position;
                
                if (NavMesh.CalculatePath(fromPosition, prepointA, areaMask, path))
                {
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        // Check if path passes through any other door's prepoint
                        if (!PathPassesThroughOtherPrepoint(path, edge.prePointA, allPrepoints, prepointThreshold))
                        {
                            pathLengthA = GetPathLength(path);
                            canReachA = true;
                        }
                    }
                }
            }

            // Check path to prepoint B
            bool canReachB = false;
            float pathLengthB = float.PositiveInfinity;
            
            Vector3 prepointB = edge.prePointB.position;
            if (NavMesh.SamplePosition(prepointB, out var hitB, 1.0f, areaMask))
            {
                prepointB = hitB.position;
                
                if (NavMesh.CalculatePath(fromPosition, prepointB, areaMask, path))
                {
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        // Check if path passes through any other door's prepoint
                        if (!PathPassesThroughOtherPrepoint(path, edge.prePointB, allPrepoints, prepointThreshold))
                        {
                            pathLengthB = GetPathLength(path);
                            canReachB = true;
                        }
                    }
                }
            }

            // If only one prepoint is reachable → DEFINITIVE answer, return immediately
            if (canReachA && !canReachB)
            {
                // Only prepointA is reachable → we're in roomA
                if (edge.roomA != null && edge.roomA.zoneID == ownerZoneID)
                {
                    string doorName = edge.door ? edge.door.name : "Unknown";
                    string roomName = edge.roomA.roomID ?? edge.roomA.name;
                    //Debug.Log($"[NPCAnchorManager] DEFINITIVE: Door={doorName}, PrepointA reachable (path={pathLengthA:F2}m) → Room={roomName}");
                    return edge.roomA.transform;
                }
            }
            else if (canReachB && !canReachA)
            {
                // Only prepointB is reachable → we're in roomB
                if (edge.roomB != null && edge.roomB.zoneID == ownerZoneID)
                {
                    string doorName = edge.door ? edge.door.name : "Unknown";
                    string roomName = edge.roomB.roomID ?? edge.roomB.name;
                    //Debug.Log($"[NPCAnchorManager] DEFINITIVE: Door={doorName}, PrepointB reachable (path={pathLengthB:F2}m) → Room={roomName}");
                    return edge.roomB.transform;
                }
            }
            // else: both reachable or both failed → continue to next door
        }
        
        // Second pass: If no definitive answer, use closest prepoint from all doors
        RoomAnchor bestRoom = null;
        float bestPathLength = float.PositiveInfinity;
        string bestDoorName = null;
        string bestPrepoint = null;
        
        foreach (var edge in RoomGraph.Instance.edges)
        {
            if (!edge.prePointA || !edge.prePointB || !edge.door) continue;

            bool roomAInZone = edge.roomA && edge.roomA.zoneID == ownerZoneID;
            bool roomBInZone = edge.roomB && edge.roomB.zoneID == ownerZoneID;
            
            if (!roomAInZone && !roomBInZone) continue;

            // Re-check paths (we already calculated above, but keeping logic clear)
            bool canReachA = false;
            float pathLengthA = float.PositiveInfinity;
            
            Vector3 prepointA = edge.prePointA.position;
            if (NavMesh.SamplePosition(prepointA, out var hitA, 1.0f, areaMask))
            {
                prepointA = hitA.position;
                
                if (NavMesh.CalculatePath(fromPosition, prepointA, areaMask, path))
                {
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        if (!PathPassesThroughOtherPrepoint(path, edge.prePointA, allPrepoints, prepointThreshold))
                        {
                            pathLengthA = GetPathLength(path);
                            canReachA = true;
                        }
                    }
                }
            }

            bool canReachB = false;
            float pathLengthB = float.PositiveInfinity;
            
            Vector3 prepointB = edge.prePointB.position;
            if (NavMesh.SamplePosition(prepointB, out var hitB, 1.0f, areaMask))
            {
                prepointB = hitB.position;
                
                if (NavMesh.CalculatePath(fromPosition, prepointB, areaMask, path))
                {
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        if (!PathPassesThroughOtherPrepoint(path, edge.prePointB, allPrepoints, prepointThreshold))
                        {
                            pathLengthB = GetPathLength(path);
                            canReachB = true;
                        }
                    }
                }
            }

            // If both prepoints are reachable, use the closer one
            if (canReachA && canReachB)
            {
                if (pathLengthA < pathLengthB && pathLengthA < bestPathLength)
                {
                    if (edge.roomA != null && edge.roomA.zoneID == ownerZoneID)
                    {
                        bestPathLength = pathLengthA;
                        bestRoom = edge.roomA;
                        bestDoorName = edge.door ? edge.door.name : "Unknown";
                        bestPrepoint = "PrepointA";
                    }
                }
                else if (pathLengthB < pathLengthA && pathLengthB < bestPathLength)
                {
                    if (edge.roomB != null && edge.roomB.zoneID == ownerZoneID)
                    {
                        bestPathLength = pathLengthB;
                        bestRoom = edge.roomB;
                        bestDoorName = edge.door ? edge.door.name : "Unknown";
                        bestPrepoint = "PrepointB";
                    }
                }
            }
        }
        
        // Return the room with the closest prepoint
        if (bestRoom != null)
        {
            string roomName = bestRoom.roomID ?? bestRoom.name;
            //Debug.Log($"[NPCAnchorManager] CLOSEST: Door={bestDoorName}, {bestPrepoint} closest (path={bestPathLength:F2}m) → Room={roomName}");
            return bestRoom.transform;
        }
        
        // No valid immediate door found
        return null;
    }

    /// <summary>
    /// Checks if a NavMesh path passes through any other door's opening (line between prepoints).
    /// This is more accurate than checking proximity to prepoints, since paths go through door openings.
    /// </summary>
    private bool PathPassesThroughOtherPrepoint(NavMeshPath path, Transform targetPrepoint, HashSet<Transform> allPrepoints, float threshold)
    {
        if (path.corners == null || path.corners.Length < 2)
            return false;

        // Get the door edge for the target prepoint (so we can exclude it)
        DoorEdge targetEdge = null;
        foreach (var edge in RoomGraph.Instance.edges)
        {
            if (edge.prePointA == targetPrepoint || edge.prePointB == targetPrepoint)
            {
                targetEdge = edge;
                break;
            }
        }

        // Check each segment of the path
        for (int i = 0; i < path.corners.Length - 1; i++)
        {
            Vector3 segmentStart = path.corners[i];
            Vector3 segmentEnd = path.corners[i + 1];

            // Check if this path segment crosses through any other door's opening
            foreach (var edge in RoomGraph.Instance.edges)
            {
                // Skip the target door
                if (edge == targetEdge) continue;
                if (!edge.prePointA || !edge.prePointB) continue;

                // Check if path segment crosses the line between the two prepoints of this door
                Vector3 doorStart = edge.prePointA.position;
                Vector3 doorEnd = edge.prePointB.position;
                
                // Check if the path segment intersects the door opening (line between prepoints)
                if (LineSegmentsIntersect(segmentStart, segmentEnd, doorStart, doorEnd, threshold))
                {
                    return true; // Path passes through another door's opening
                }
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
