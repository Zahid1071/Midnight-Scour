#if STEAMAUDIO_ENABLED
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SteamAudioDynamicObject = SteamAudio.SteamAudioDynamicObject;
using SteamAudioGeometry = SteamAudio.SteamAudioGeometry;
using SteamAudioManager = SteamAudio.SteamAudioManager;
using SteamAudioMaterial = SteamAudio.SteamAudioMaterial;
using SteamAudioProbeBatch = SteamAudio.SteamAudioProbeBatch;
using SteamAudioStaticMesh = SteamAudio.SteamAudioStaticMesh;
using SerializedData = SteamAudio.SerializedData;

/// <summary>
/// Steam Audio setup for the main scene: the farmhouse and basement shells become static acoustic geometry,
/// hinged doors become dynamic objects (so they block sound only while shut), then the scene is exported and
/// probe batches are generated and baked for pathing. Re-run both steps after changing level geometry.
/// </summary>
static class SteamAudioLevelSetup
{
    const string DataFolder = "Assets/Audio/Steam Audio";
    const string PresetFolder = "Assets/Plugins/SteamAudio/Resources/Materials";

    const string FarmhousePath = "/_Environment/Farmhouse";
    const string BasementPath = "/Basement";

    // Structural groups under each building and the material they get. Decor is left out on purpose.
    static readonly (string group, string material)[] FarmhouseGroups =
    {
        ("Exterior Walls", "House Exterior Wall"),
        ("Interior Walls", "House Interior Wall"),
        ("Floors", "House Floor"),
        ("Ceilings", "House Floor"),
        ("Stairs", "Wood"),
        ("Roof", "Wood"),
        ("Porches", "Wood"),
        ("Foundation", "Concrete"),
    };

    [MenuItem("Tools/Audio/Steam Audio/1. Tag Geometry and Export")]
    static void TagAndExport()
    {
        Scene scene = SceneManager.GetActiveScene();
        EnsureFolder(DataFolder);
        EnsureFolder(DataFolder + "/Dynamic");

        int tagged = 0;
        Transform farmhouse = Find(FarmhousePath);
        if (farmhouse != null)
        {
            foreach ((string group, string material) in FarmhouseGroups)
            {
                Transform t = farmhouse.Find(group);
                if (t != null && Tag(t.gameObject, Material(material), true))
                    tagged++;
            }
        }

        Transform basement = Find(BasementPath);
        if (basement != null && Tag(basement.gameObject, Material("Concrete"), true))
            tagged++;

        int glass = 0;
        foreach (Transform root in new[] { farmhouse, basement })
        {
            if (root == null)
                continue;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<SteamAudioGeometry>() == null || !UsesMaterial(r, "glass"))
                    continue;
                if (Tag(r.gameObject, Material("Glass"), false))
                    glass++;
            }
        }

        var doors = new List<SteamAudioDynamicObject>();
        foreach (Transform root in new[] { farmhouse, basement })
        {
            if (root == null)
                continue;
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (!animator.name.StartsWith("Door_mesh"))
                    continue;
                GameObject door = animator.gameObject;
                Tag(door, Material("House Door"), true);
                var dynamic = door.GetComponent<SteamAudioDynamicObject>();
                if (dynamic == null)
                    dynamic = Undo.AddComponent<SteamAudioDynamicObject>(door);
                if (dynamic.asset == null)
                    dynamic.asset = CreateData($"{DataFolder}/Dynamic/{scene.name}_{UniqueName(door.transform)}.asset");
                doors.Add(dynamic);
            }
        }

        foreach (SteamAudioDynamicObject door in doors)
            SteamAudioManager.ExportDynamicObject(door, false);

        EnsureStaticMeshAsset(scene);
        SteamAudioManager.ExportScene(scene, false);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"Steam Audio: tagged {tagged} building groups, {glass} glass panes, {doors.Count} doors; scene exported.");
    }

    [MenuItem("Tools/Audio/Steam Audio/2. Generate Probes and Bake Pathing")]
    static void GenerateAndBake()
    {
        Scene scene = SceneManager.GetActiveScene();
        EnsureFolder(DataFolder);

        var batches = new List<SteamAudioProbeBatch>();
        AddBatch(batches, scene, "Farmhouse", Find(FarmhousePath), new Vector3(6f, 2f, 6f), true);
        AddBatch(batches, scene, "Basement", Find(BasementPath), new Vector3(1f, 1f, 1f), false);

        foreach (SteamAudioProbeBatch batch in batches)
            batch.GenerateProbes();

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        SteamAudioProbeBatch.BeginBake(batches.ToArray());
    }

    static void AddBatch(List<SteamAudioProbeBatch> batches, Scene scene, string label, Transform building,
        Vector3 margin, bool skipDecor)
    {
        if (building == null)
            return;

        Bounds? bounds = null;
        foreach (Renderer r in building.GetComponentsInChildren<Renderer>(true))
        {
            if (skipDecor && r.transform.IsChildOf(building) && IsUnder(r.transform, building, "Decor"))
                continue;
            if (bounds == null)
                bounds = r.bounds;
            else
            {
                Bounds b = bounds.Value;
                b.Encapsulate(r.bounds);
                bounds = b;
            }
        }
        if (bounds == null)
            return;

        string name = "Steam Audio Probes - " + label;
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
        }
        go.transform.SetPositionAndRotation(bounds.Value.center, Quaternion.identity);
        go.transform.localScale = bounds.Value.size + margin;

        var batch = go.GetComponent<SteamAudioProbeBatch>();
        if (batch == null)
            batch = go.AddComponent<SteamAudioProbeBatch>();
        batch.horizontalSpacing = 1.5f;
        batch.heightAboveFloor = 1.5f;
        if (batch.asset == null)
            batch.asset = CreateData($"{DataFolder}/{scene.name}_{label}_Probes.asset");
        batches.Add(batch);
    }

    static bool Tag(GameObject go, SteamAudioMaterial material, bool allChildren)
    {
        var geometry = go.GetComponent<SteamAudioGeometry>();
        bool added = geometry == null;
        if (added)
            geometry = Undo.AddComponent<SteamAudioGeometry>(go);
        geometry.material = material;
        geometry.exportAllChildren = allChildren;
        EditorUtility.SetDirty(geometry);
        return added;
    }

    static void EnsureStaticMeshAsset(Scene scene)
    {
        SteamAudioStaticMesh staticMesh = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            staticMesh = root.GetComponentInChildren<SteamAudioStaticMesh>();
            if (staticMesh != null)
                break;
        }
        if (staticMesh == null)
        {
            var go = new GameObject("Steam Audio Static Mesh");
            Undo.RegisterCreatedObjectUndo(go, go.name);
            staticMesh = go.AddComponent<SteamAudioStaticMesh>();
        }
        if (staticMesh.asset == null)
        {
            staticMesh.asset = CreateData($"{DataFolder}/{scene.name}_StaticMesh.asset");
            staticMesh.sceneNameWhenExported = scene.name;
        }
    }

    static SteamAudioMaterial Material(string name)
    {
        var preset = AssetDatabase.LoadAssetAtPath<SteamAudioMaterial>($"{PresetFolder}/{name}.asset");
        if (preset != null)
            return preset;

        string path = $"{DataFolder}/{name}.asset";
        var material = AssetDatabase.LoadAssetAtPath<SteamAudioMaterial>(path);
        if (material != null)
            return material;

        material = ScriptableObject.CreateInstance<SteamAudioMaterial>();
        // Transmission is set well above real-world values: the butcher has to stay audible through the house.
        switch (name)
        {
            case "House Interior Wall":
                Set(material, 0.10f, 0.06f, 0.04f, 0.35f, 0.18f, 0.06f);
                break;
            case "House Exterior Wall":
                Set(material, 0.12f, 0.06f, 0.04f, 0.20f, 0.07f, 0.02f);
                break;
            case "House Floor":
                Set(material, 0.15f, 0.10f, 0.08f, 0.35f, 0.12f, 0.03f);
                break;
            case "House Door":
                Set(material, 0.10f, 0.07f, 0.06f, 0.40f, 0.22f, 0.08f);
                break;
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void Set(SteamAudioMaterial m, float absLow, float absMid, float absHigh, float tLow, float tMid, float tHigh)
    {
        m.lowFreqAbsorption = absLow;
        m.midFreqAbsorption = absMid;
        m.highFreqAbsorption = absHigh;
        m.scattering = 0.05f;
        m.lowFreqTransmission = tLow;
        m.midFreqTransmission = tMid;
        m.highFreqTransmission = tHigh;
    }

    static SerializedData CreateData(string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<SerializedData>(path);
        if (existing != null)
            return existing;
        var data = ScriptableObject.CreateInstance<SerializedData>();
        AssetDatabase.CreateAsset(data, path);
        return data;
    }

    static bool UsesMaterial(Renderer r, string keyword)
    {
        foreach (UnityEngine.Material m in r.sharedMaterials)
        {
            if (m != null && m.name.ToLowerInvariant().Contains(keyword))
                return true;
        }
        return false;
    }

    static bool IsUnder(Transform t, Transform root, string childName)
    {
        for (Transform p = t; p != null && p != root; p = p.parent)
        {
            if (p.parent == root && p.name == childName)
                return true;
        }
        return false;
    }

    static string UniqueName(Transform t)
    {
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent)
            path = p.name + "/" + path;
        return t.name + "_" + ((uint)path.GetHashCode()).ToString("x8");
    }

    static Transform Find(string path)
    {
        GameObject go = GameObject.Find(path);
        return go != null ? go.transform : null;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
#endif
