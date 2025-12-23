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
                var anchor = t.GetComponent<RoomAnchor>();
                return anchor != null && anchor.zoneID == zoneID;
            })
            .OrderBy(t => Vector3.Distance(center, t.position))
            .Take(maxCount)
            .ToList();
    }
}
