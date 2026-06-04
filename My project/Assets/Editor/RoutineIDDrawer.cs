using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

[CustomPropertyDrawer(typeof(RoutineIDAttribute))]
public class RoutineIDDrawer : PropertyDrawer
{
    static Dictionary<RoutineIDAttribute.IDType, List<string>> _sceneIdsByType;
    static bool _cacheHooksRegistered;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "RoutineID requires a string field.");
            return;
        }

        EnsureCacheHooks();
        var attr = (RoutineIDAttribute)attribute;
        var sceneIds = GetSceneIds(attr.Type);

        string current = property.stringValue ?? string.Empty;

        // No scene matches — plain text field only; never overwrite from code.
        if (sceneIds.Count == 0)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        var options = BuildPopupOptions(current, sceneIds);
        int selectedIndex = ResolvePopupIndex(current, options);

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUI.Popup(position, label.text, selectedIndex, options.ToArray());
        if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < options.Count)
            property.stringValue = options[newIndex];
    }

    /// <summary>
    /// Popup options for picking only. Preserves the current value even if it is not in the scene scan.
    /// </summary>
    static List<string> BuildPopupOptions(string current, List<string> sceneIds)
    {
        var options = new List<string>(sceneIds.Count + 1);

        if (!string.IsNullOrEmpty(current) && !sceneIds.Contains(current))
            options.Add(current);

        options.AddRange(sceneIds);
        return options;
    }

    static int ResolvePopupIndex(string current, List<string> options)
    {
        if (string.IsNullOrEmpty(current))
            return 0;

        int index = options.IndexOf(current);
        return index >= 0 ? index : 0;
    }

    static void EnsureCacheHooks()
    {
        if (_cacheHooksRegistered)
            return;

        _cacheHooksRegistered = true;
        void Invalidate() => _sceneIdsByType = null;

        EditorApplication.hierarchyChanged += Invalidate;
        EditorApplication.projectChanged += Invalidate;
    }

    static List<string> GetSceneIds(RoutineIDAttribute.IDType type)
    {
        _sceneIdsByType ??= new Dictionary<RoutineIDAttribute.IDType, List<string>>();

        if (_sceneIdsByType.TryGetValue(type, out var cached))
            return cached;

        var ids = new HashSet<string>();

        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!t.gameObject.scene.IsValid())
                continue;

            string lower = t.name.ToLower();

            switch (type)
            {
                case RoutineIDAttribute.IDType.Zone:
                    if (lower.Contains("house") || lower.Contains("zone"))
                        ids.Add(t.name);
                    break;

                case RoutineIDAttribute.IDType.Room:
                    if (lower.Contains("room") || lower.Contains("trigger"))
                        ids.Add(t.name);
                    break;

                case RoutineIDAttribute.IDType.Hotspot:
                    if (lower.Contains("spot") || lower.Contains("point") || lower.Contains("action"))
                        ids.Add(t.name);
                    break;
            }
        }

        cached = ids.OrderBy(id => id, System.StringComparer.OrdinalIgnoreCase).ToList();
        _sceneIdsByType[type] = cached;
        return cached;
    }
}
