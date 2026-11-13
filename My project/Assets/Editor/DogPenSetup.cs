#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Puts the Blender dog pen (Assets/Models/Dog Pen) out in the east field: project materials, colliders,
/// the zombie dog asleep on its spot with an animator, the pen lamp, and a muddy, grass-free floor inside the fence.
/// Re-running replaces the placed pen.
/// </summary>
public static class DogPenSetup
{
    const string Folder = "Assets/Models/Dog Pen";
    const string PenPath = Folder + "/DogPen.fbx";
    const string DogPath = Folder + "/ZombieDog.fbx";
    const string MaterialFolder = Folder + "/Materials";
    const string ControllerPath = Folder + "/ZombieDog.controller";
    const string DogTexturePath = Folder + "/Textures/ZombieDog_BaseColor.png";
    const string SoilLayerPath = "Assets/Terrain/Farm/Farm Soil.terrainlayer";
    public const string RootName = "Dog Pen";

    // Out on its own at the end of the field lane, about 80 m east of the yard (the old house sits as far out to the
    // west, across the road). Yaw 270 turns the gate west, toward the lane and the farm.
    public static readonly Vector3 FenceCentre = new Vector3(76f, 0f, 39f);
    const float Yaw = 270f;
    // Fence centre in the FBX's own frame.
    static readonly Vector3 ModelFenceCentre = new Vector3(-14f, 0f, 0f);
    const float GateOpenAngle = 92f;

    // In the pen root's frame (yaw 0).
    static readonly Rect LocalFence = Rect.MinMaxRect(-4.2f, -3.7f, 4.2f, 3.7f);
    static readonly Rect LocalFootprint = Rect.MinMaxRect(-9.7f, -8.4f, 9.6f, 7.1f);
    static readonly Vector3 LocalGate = new Vector3(0.55f, 0f, 3.63f);

    /// World rect of the fence, kept clear of grass and painted with mud.
    public static readonly Rect FenceRect = ToWorld(LocalFence);
    /// World rect of everything in the pen model, including the trees and props outside the fence.
    public static readonly Rect Footprint = ToWorld(LocalFootprint);
    /// Gate opening on the ground, in world space.
    public static Vector3 Gate => FenceCentre + Quaternion.Euler(0f, Yaw, 0f) * LocalGate;

    static readonly Dictionary<string, string> ProjectMaterials = new Dictionary<string, string>
    {
        { "PH_barrel_03", "Assets/Props/PolyHaven/barrel_03/barrel_03.mat" },
        { "PH_old_tyre", "Assets/Props/PolyHaven/old_tyre/tire_r13_01.mat" },
        { "PH_wooden_crate_02", "Assets/Props/PolyHaven/wooden_crate_02/wooden_crate_02.mat" },
        { "PH_dead_tree_trunk", "Assets/Props/PolyHaven/dead_tree_trunk/dead_tree_trunk.mat" },
        { "PH_rock_moss_set_01", "Assets/Props/PolyHaven/rock_moss_set_01/rock_moss_set_01.mat" },
        { "PH_rusted_spade_01", "Assets/Props/PolyHaven/rusted_spade_01/rusted_spade_01.mat" },
        { "PH_tree_stump_01", "Assets/Props/PolyHaven/tree_stump_01/tree_stump_01.mat" },
        { "PH_wooden_bucket_01", "Assets/Props/PolyHaven/wooden_bucket_01/wooden_bucket_01.mat" },
        { "Prop_DoorKey", "Assets/Prefabs/Objects/Box Key/door-key/textures/Key.mat" },
        { "Prop_DryTreeBark", "Assets/Dry_Trees/Materials/Bark.mat" },
        { "Prop_HumanBone", "Assets/Models/Human Bone/Materials/BONE_LOW_baseTexBaked_COLOR.mat" },
        { "Prop_Padlock", "Assets/Models/Padlock/Textures/Padlock.mat" },
    };

    // Small or thin parts that would only snag the player.
    static readonly string[] NoCollider =
    {
        "Prop_Bone", "Pen2_Straw", "Prop_RottenMeat", "Prop_GateChain", "Prop_Padlock", "Prop_StakeChain", "Pen_Key",
        "Pen_LampBulb", "Pen2_BarbedWire", "Prop_FoodBowl", "Pen2_Sign", "MK_",
    };

    static readonly string[] LoopingClips = { "Dog_Idle", "Dog_Sleep", "Dog_Walk", "Dog_Run", "Dog_Bark" };

    [MenuItem("Tools/Environment/Place Dog Pen")]
    static void PlaceMenu() => Debug.Log(Place());

    public static string Place()
    {
        var env = GameObject.Find("_Environment");
        if (!env) return "No _Environment root found.";
        var terrainGo = env.transform.Find("Terrain");
        var terrain = terrainGo ? terrainGo.GetComponent<Terrain>() : null;

        SetupMaterials();
        var controller = SetupDog();

        var old = env.transform.Find(RootName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject(RootName).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Place Dog Pen");
        root.SetParent(env.transform, false);
        float y = terrain ? terrain.SampleHeight(FenceCentre) + terrain.transform.position.y : 0f;
        root.SetPositionAndRotation(new Vector3(FenceCentre.x, y, FenceCentre.z), Quaternion.Euler(0f, Yaw, 0f));

        var pen = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PenPath), root);
        pen.transform.localPosition = -ModelFenceCentre;
        pen.transform.localRotation = Quaternion.identity;

        // Modelled 24 degrees ajar, which still blocks the opening; swing it out so the player can get in.
        var gate = pen.transform.Find("Pen2_Gate");
        if (gate)
        {
            Vector3 e = gate.localEulerAngles;
            gate.localEulerAngles = new Vector3(e.x, GateOpenAngle, e.z);
        }

        int colliders = AddColliders(pen.transform);
        GameObjectUtility.SetStaticEditorFlags(pen, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                    StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        foreach (var t in pen.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, GameObjectUtility.GetStaticEditorFlags(pen));
            if (t.name.StartsWith("MK_")) t.gameObject.SetActive(false);
        }

        // The pen file carries a posed copy of the dog; the animated one takes its place.
        var staticDog = pen.transform.Find("ZombieDog");
        Vector3 dogPos = staticDog ? staticDog.position : pen.transform.TransformPoint(new Vector3(-11.6f, 0f, -1.05f));
        if (staticDog) staticDog.gameObject.SetActive(false);
        var dog = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DogPath), root);
        dog.name = "Zombie Dog";
        dog.transform.SetPositionAndRotation(dogPos, pen.transform.rotation);
        var animator = dog.GetComponent<Animator>();
        if (!animator) animator = dog.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        var bulb = pen.transform.Find("Pen_LampBulb");
        if (bulb) AddLamp(root, bulb.position);

        if (terrain) PrepareGround(terrain);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(env.scene);
        return $"Dog pen placed at {root.position} with {colliders} colliders.";
    }

    // ---------- Assets ----------

    static void SetupMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(Folder, "Materials");
        var map = new Dictionary<string, Material>();
        foreach (var kv in ProjectMaterials)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(kv.Value);
            if (m) map[kv.Key] = m;
            else Debug.LogWarning($"DogPenSetup: missing {kv.Value}.");
        }

        var rust = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Props/PolyHaven/rusted_spade_01/textures/rusted_spade_01_diff_1k.jpg");
        var wood = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Prefabs/Environment/Models/US House/textures/Wood062_2K_Color.jpg");
        map["Pen2_WeatheredPlank"] = Lit("Pen2_WeatheredPlank", new Color(0.8f, 0.76f, 0.72f), 0f, 0.12f, wood);
        map["Pen2_PostWood"] = Lit("Pen2_PostWood", new Color(0.6f, 0.56f, 0.52f), 0f, 0.1f, wood);
        map["Prop_Chain"] = Lit("Prop_Chain", new Color(0.8f, 0.78f, 0.75f), 0.85f, 0.35f,
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Models/Chainlink/ChainValley_ChainMaterial_BaseColor.png"));
        map["Pen2_Straw"] = Lit("Pen2_Straw", new Color(1f, 0.82f, 0.5f), 0f, 0.05f,
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Forest Leaves/forest_leaves_04_diff_1k.jpg"));
        map["Pen2_Rust"] = Lit("Pen2_Rust", new Color(0.9f, 0.75f, 0.65f), 0.35f, 0.2f, rust);
        map["Pen2_RoofMetal"] = Lit("Pen2_RoofMetal", new Color(0.7f, 0.66f, 0.6f), 0.55f, 0.3f, rust);
        map["Pen2_Cable"] = Lit("Pen2_Cable", new Color(0.015f, 0.015f, 0.015f), 0f, 0.45f, null);
        map["Pen2_RawMeat"] = Lit("Pen2_RawMeat", new Color(0.32f, 0.04f, 0.03f), 0f, 0.65f, null);
        map["Pen2_RedPaint"] = Lit("Pen2_RedPaint", new Color(0.22f, 0.012f, 0.008f), 0f, 0.3f, null);
        var bulb = Lit("Pen_Bulb", new Color(1f, 0.85f, 0.6f), 0f, 0.8f, null);
        bulb.EnableKeyword("_EMISSION");
        bulb.SetColor("_EmissionColor", new Color(1f, 0.75f, 0.4f) * 2f);
        bulb.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(bulb);
        map["Pen_Bulb"] = bulb;
        map["DogB_Mat"] = Lit("DogB_Mat", Color.white, 0f, 0.15f, AssetDatabase.LoadAssetAtPath<Texture2D>(DogTexturePath));
        AssetDatabase.SaveAssets();

        Remap(PenPath, map);
        Remap(DogPath, map);
    }

    static Material Lit(string name, Color color, float metallic, float smoothness, Texture2D baseMap)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        if (baseMap) m.SetTexture("_BaseMap", baseMap);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Remap(string modelPath, Dictionary<string, Material> map)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        var used = new HashSet<string>();
        foreach (var r in AssetDatabase.LoadAssetAtPath<GameObject>(modelPath).GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                if (m) used.Add(m.name);
        var existing = importer.GetExternalObjectMap();
        bool changed = false;
        foreach (var kv in map)
        {
            if (!used.Contains(kv.Key) && !existing.ContainsKey(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key)))
                continue;
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key);
            if (existing.TryGetValue(id, out var current) && current == kv.Value) continue;
            importer.AddRemap(id, kv.Value);
            changed = true;
        }
        if (changed) importer.SaveAndReimport();
    }

    static AnimatorController SetupDog()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(DogPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.loopTime = System.Array.IndexOf(LoopingClips, c.name) >= 0;
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = true;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        var byName = new Dictionary<string, AnimationClip>();
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(DogPath))
            if (o is AnimationClip clip && !clip.name.StartsWith("__preview__")) byName[clip.name] = clip;

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Awake", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Bark", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        AnimatorState State(string clip, Vector3 pos)
        {
            var s = sm.AddState(clip.Replace("Dog_", ""), pos);
            if (byName.TryGetValue(clip, out var c)) s.motion = c;
            return s;
        }
        var sleep = State("Dog_Sleep", new Vector3(250f, 0f));
        var wake = State("Dog_WakeUp", new Vector3(250f, 100f));
        var idle = State("Dog_Idle", new Vector3(500f, 100f));
        var walk = State("Dog_Walk", new Vector3(750f, 50f));
        var run = State("Dog_Run", new Vector3(1000f, 50f));
        var bark = State("Dog_Bark", new Vector3(500f, 250f));
        var attack = State("Dog_Attack", new Vector3(750f, 250f));
        sm.defaultState = sleep;

        AnimatorStateTransition Link(AnimatorState from, AnimatorState to, bool exitTime = false)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = exitTime;
            t.exitTime = exitTime ? 0.95f : 0f;
            t.duration = 0.2f;
            return t;
        }
        Link(sleep, wake).AddCondition(AnimatorConditionMode.If, 0f, "Awake");
        Link(wake, idle, true);
        Link(idle, sleep).AddCondition(AnimatorConditionMode.IfNot, 0f, "Awake");
        Link(idle, walk).AddCondition(AnimatorConditionMode.Greater, 0.2f, "Speed");
        Link(walk, idle).AddCondition(AnimatorConditionMode.Less, 0.2f, "Speed");
        Link(walk, run).AddCondition(AnimatorConditionMode.Greater, 3f, "Speed");
        Link(run, walk).AddCondition(AnimatorConditionMode.Less, 3f, "Speed");
        Link(idle, bark).AddCondition(AnimatorConditionMode.If, 0f, "Bark");
        Link(bark, idle).AddCondition(AnimatorConditionMode.IfNot, 0f, "Bark");
        var toAttack = sm.AddAnyStateTransition(attack);
        toAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
        toAttack.duration = 0.1f;
        toAttack.canTransitionToSelf = false;
        Link(attack, idle, true);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // ---------- Scene ----------

    static int AddColliders(Transform pen)
    {
        int n = 0;
        foreach (var mf in pen.GetComponentsInChildren<MeshFilter>(true))
        {
            // Parts of a skipped prop (the padlock's shackle and cylinder) are skipped with it.
            bool skip = false;
            for (var t = mf.transform; t != pen && !skip; t = t.parent)
                foreach (var prefix in NoCollider)
                    if (t.name.StartsWith(prefix)) { skip = true; break; }
            if (skip || !mf.sharedMesh || mf.GetComponent<Collider>()) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            n++;
        }
        return n;
    }

    static void AddLamp(Transform root, Vector3 bulb)
    {
        var go = new GameObject("Pen Lamp Light");
        go.transform.SetParent(root, false);
        go.transform.position = bulb + Vector3.down * 0.12f;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.42f);
        light.intensity = 2.5f;
        light.range = 10f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.9f;
        light.lightmapBakeType = LightmapBakeType.Realtime;
    }

    /// Mud floor inside the fence, no grass poking through props, and no terrain trees inside the pen's footprint.
    static void PrepareGround(Terrain terrain)
    {
        var data = terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(data, "Place Dog Pen");
        var pos = terrain.transform.position;
        var size = data.size;

        var keep = new List<TreeInstance>();
        foreach (var tree in data.treeInstances)
        {
            var w = Vector3.Scale(tree.position, size) + pos;
            if (!Grow(Footprint, 1f).Contains(new Vector2(w.x, w.z))) keep.Add(tree);
        }
        data.SetTreeInstances(keep.ToArray(), true);

        var clear = Grow(FenceRect, 0.4f);
        int res = data.detailResolution;
        int x0 = Mathf.FloorToInt((clear.xMin - pos.x) / size.x * res), x1 = Mathf.CeilToInt((clear.xMax - pos.x) / size.x * res);
        int z0 = Mathf.FloorToInt((clear.yMin - pos.z) / size.z * res), z1 = Mathf.CeilToInt((clear.yMax - pos.z) / size.z * res);
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
            data.SetDetailLayer(x0, z0, layer, new int[z1 - z0, x1 - x0]);

        int soil = System.Array.IndexOf(data.terrainLayers, AssetDatabase.LoadAssetAtPath<TerrainLayer>(SoilLayerPath));
        if (soil >= 0)
        {
            var paint = Grow(FenceRect, 1.5f);
            int ares = data.alphamapResolution;
            int ax0 = Mathf.FloorToInt((paint.xMin - pos.x) / size.x * ares), ax1 = Mathf.CeilToInt((paint.xMax - pos.x) / size.x * ares);
            int az0 = Mathf.FloorToInt((paint.yMin - pos.z) / size.z * ares), az1 = Mathf.CeilToInt((paint.yMax - pos.z) / size.z * ares);
            var maps = data.GetAlphamaps(ax0, az0, ax1 - ax0, az1 - az0);
            int layers = maps.GetLength(2);
            for (int iz = 0; iz < az1 - az0; iz++)
            for (int ix = 0; ix < ax1 - ax0; ix++)
            {
                float x = pos.x + (ix + ax0 + 0.5f) / ares * size.x, z = pos.z + (iz + az0 + 0.5f) / ares * size.z;
                float inside = Mathf.Min(x - FenceRect.xMin, FenceRect.xMax - x, z - FenceRect.yMin, FenceRect.yMax - z);
                float noise = (Mathf.PerlinNoise(x * 0.6f, z * 0.6f) - 0.5f) * 0.8f;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((inside + 1.2f + noise) / 1.4f));
                if (k <= 0f) continue;
                for (int l = 0; l < layers; l++) maps[iz, ix, l] *= 1f - k;
                maps[iz, ix, soil] += k;
            }
            data.SetAlphamaps(ax0, az0, maps);
        }
        EditorUtility.SetDirty(data);
    }

    static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

    static Rect ToWorld(Rect local)
    {
        var rot = Quaternion.Euler(0f, Yaw, 0f);
        Vector3 a = FenceCentre + rot * new Vector3(local.xMin, 0f, local.yMin);
        Vector3 b = FenceCentre + rot * new Vector3(local.xMax, 0f, local.yMax);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z));
    }
}
#endif
