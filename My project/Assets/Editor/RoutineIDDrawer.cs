#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

[CustomPropertyDrawer(typeof(RoutineIDAttribute))]
public class RoutineIDDrawer : PropertyDrawer
{
    // cache per type + (for hotspot) per room context
    private readonly Dictionary<string, List<string>> _cache = new();

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var attr = (RoutineIDAttribute)attribute;

        // Hotspot list depends on selected roomAnchorID in the same room plan
        string roomContext = attr.Type == RoutineIDAttribute.IDType.Hotspot
            ? TryGetSiblingRoomAnchorID(property)
            : string.Empty;

        string cacheKey = $"{SceneManager.GetActiveScene().path}::{attr.Type}::{roomContext}";

        if (!_cache.TryGetValue(cacheKey, out var ids) || ids == null || ids.Count == 0)
        {
            ids = BuildIDs(attr.Type, roomContext);
            _cache[cacheKey] = ids;
        }

        if (ids.Count == 0)
        {
            EditorGUI.HelpBox(position,
                attr.Type == RoutineIDAttribute.IDType.Hotspot && !string.IsNullOrEmpty(roomContext)
                    ? $"No hotspots found under room '{roomContext}'."
                    : "No valid IDs found in scene.",
                MessageType.Warning);

            return;
        }

        int currentIndex = Mathf.Max(0, ids.IndexOf(property.stringValue));
        int newIndex = EditorGUI.Popup(position, property.displayName, currentIndex, ids.ToArray());
        property.stringValue = ids[Mathf.Clamp(newIndex, 0, ids.Count - 1)];
    }

    private List<string> BuildIDs(RoutineIDAttribute.IDType type, string roomContext)
    {
        var all = EnumerateSceneTransforms().ToList();

        switch (type)
        {
            case RoutineIDAttribute.IDType.Zone:
                // keep as-is (few options)
                return all
                    .Where(t => t && t.gameObject.scene.IsValid())
                    .Where(t => t.name.ToLower().Contains("house_0"))
                    .Select(t => t.name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

            case RoutineIDAttribute.IDType.Room:
                // ✅ only real rooms
                return all
                    .Where(t => t && t.gameObject.scene.IsValid())
                    .Where(t => t.GetComponent<RoomAnchor>() != null)
                    .Select(t => t.name)
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

            case RoutineIDAttribute.IDType.Hotspot:
                // ✅ only hotspots under the selected room
                if (!string.IsNullOrEmpty(roomContext))
                {
                    // Try to use NPCAnchorManager first (authoritative source)
                    Transform roomT = null;
                    if (NPCAnchorManager.Instance != null)
                    {
                        roomT = NPCAnchorManager.Instance.GetAnchor(roomContext);
                    }
                    
                    // Fallback to scene search if NPCAnchorManager not available
                    if (roomT == null)
                    {
                        roomT = FindTransformByName(roomContext);
                    }
                    
                    if (roomT != null)
                    {
                        // Only search direct children and their immediate children (hotspots are usually direct children of room)
                        // This matches the runtime behavior where hotspots are children of room anchors
                        var hotspots = new List<string>();
                        
                        // Check direct children first
                        for (int i = 0; i < roomT.childCount; i++)
                        {
                            var child = roomT.GetChild(i);
                            if (child && child.gameObject.scene.IsValid())
                            {
                                string childName = child.name;
                                if (childName.ToLower().StartsWith("hotspot_"))
                                {
                                    hotspots.Add(childName);
                                }
                            }
                        }
                        
                        return hotspots
                            .Distinct()
                            .OrderBy(n => n)
                            .ToList();
                    }
                }

                // fallback: if room not selected yet, show nothing (prevents bad picks)
                return new List<string>();
        }

        return new List<string>();
    }

    // --- Context lookup: hotspotID -> sibling roomAnchorID in same WeightedRoomPlan ---

    // propertyPath example:
    // weightedRoutinePlans.Array.data[0].roomPlans.Array.data[2].hotspots.Array.data[1].hotspotID
    // sibling room path:
    // weightedRoutinePlans.Array.data[0].roomPlans.Array.data[2].roomAnchorID
    private static string TryGetSiblingRoomAnchorID(SerializedProperty hotspotIDProp)
    {
        if (hotspotIDProp == null) return string.Empty;

        string path = hotspotIDProp.propertyPath;
        int idx = path.LastIndexOf(".hotspots", System.StringComparison.Ordinal);
        if (idx < 0) return string.Empty;

        string roomPath = path.Substring(0, idx) + ".roomAnchorID";
        var roomProp = hotspotIDProp.serializedObject.FindProperty(roomPath);
        return roomProp != null ? roomProp.stringValue : string.Empty;
    }

    // --- Scene traversal helpers (NO Resources.FindObjectsOfTypeAll, NO deprecated FindObjectsOfType) ---

    private static IEnumerable<Transform> EnumerateSceneTransforms()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        foreach (var go in roots)
        {
            foreach (var t in EnumerateHierarchy(go.transform))
                yield return t;
        }
    }

    private static IEnumerable<Transform> EnumerateHierarchy(Transform root)
    {
        if (!root) yield break;
        yield return root;
        for (int i = 0; i < root.childCount; i++)
        {
            foreach (var c in EnumerateHierarchy(root.GetChild(i)))
                yield return c;
        }
    }

    private static Transform FindTransformByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var t in EnumerateSceneTransforms())
            if (t && t.name == name)
                return t;

        return null;
    }
}
#endif
