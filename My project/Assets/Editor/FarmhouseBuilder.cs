#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Assembles a two-story farmhouse from the Atmospheric House modular kit (furnished by FarmhouseDecorator).
/// Local frame: 15 m wide (x) by 9 m deep (z), front facade on +z, ground floor at y 0, 3 m stories.
/// Plan: centre hall (x 6..9) with the stairs in its back-right corner climbing toward the front door;
/// living room on the left; dining and kitchen on the right; upstairs a large bedroom on the left,
/// a bedroom with an en-suite bathroom on the right.
/// </summary>
public static class FarmhouseBuilder
{
    public const string RootName = "Farmhouse";
    const string Kit = "Assets/AtmosphericHouse/Prefabs/Building/";
    const string DoorKit = "Assets/AtmosphericHouse/Prefabs/Doors/";
    const float Story = 3f;
    const float Width = 15f;
    const float Depth = 9f;
    // Stairs_main_L is built for a corner: its own wall panels cover the ground-floor back wall and the
    // x = 9 partition up to z = 4 on both stories, so those faces are left out below.
    static readonly Vector3 StairsPivot = new Vector3(9f, 0f, 0f);
    const float StairsYaw = 270f;
    const int StairsBackModule = 2;
    const float HallX = 6f;

    const string DoorOpenSound = "Assets/Audio/SFX/dragon-studio-open-door-stock-sfx-454246.mp3";
    const string DoorCloseSound = "Assets/Audio/SFX/garage_door_mp3/zapsplat_household_garage_door_wooden_closed_impact_rattle_001_53573.mp3";
    const string DoorHandleSound = "Assets/Audio/SFX/garage_door_mp3/zapsplat_household_door_garage_wooden_side_door_handle_turn_fast_single_fast_int_001_97034.mp3";

    // Exterior modules per side, index 0 = ground floor. Modules run in the wall's +x / +z order.
    static readonly string[][] Front =
    {
        new[] { "window", "window", "frontdoor", "window", "window" },
        new[] { "window", "window", "windowsmall", "window", "window" },
    };
    static readonly string[][] Back =
    {
        new[] { "window", "window", "plain", "door", "window" },
        new[] { "window", "window", "plain", "windowsmall", "plain" },
    };
    static readonly string[][] Left =
    {
        new[] { "window", "plain", "window" },
        new[] { "window", "plain", "window" },
    };
    static readonly string[][] Right =
    {
        new[] { "window", "plain", "window" },
        new[] { "window", "plain", "window" },
    };

    struct Partition
    {
        public int story;
        public bool alongZ; // true: wall on the line x = c; false: wall on the line z = c
        public float c, start, length;
        public string kind;
        public bool posFace, negFace;

        public Partition(int story, bool alongZ, float c, float start, string kind, bool posFace = true, bool negFace = true, float length = 3f)
        {
            this.story = story; this.alongZ = alongZ; this.c = c; this.start = start; this.kind = kind;
            this.posFace = posFace; this.negFace = negFace; this.length = length;
        }
    }

    static readonly Partition[] Partitions =
    {
        // Ground floor: hall | living room on x = 6, hall | kitchen + dining on x = 9, kitchen | dining on z = 3.
        new Partition(0, true, 6f, 0f, "plain"),
        new Partition(0, true, 6f, 3f, "doordouble"),
        new Partition(0, true, 6f, 6f, "plain"),
        new Partition(0, true, 9f, 0f, "plain", negFace: false),
        new Partition(0, true, 9f, 3f, "plain", negFace: false),
        new Partition(0, true, 9f, 4f, "plain", posFace: false, length: 2f),
        new Partition(0, true, 9f, 6f, "doordouble"),
        new Partition(0, false, 3f, 9f, "plain"),
        new Partition(0, false, 3f, 12f, "doordouble"),
        // Upstairs: hall | bedroom on x = 6, hall | bedroom on x = 9, bathroom | bedroom on z = 3.
        new Partition(1, true, 6f, 0f, "plain"),
        new Partition(1, true, 6f, 3f, "plain"),
        new Partition(1, true, 6f, 6f, "door"),
        new Partition(1, true, 9f, 0f, "plain", negFace: false),
        new Partition(1, true, 9f, 3f, "plain", negFace: false),
        new Partition(1, true, 9f, 4f, "plain", posFace: false, length: 2f),
        new Partition(1, true, 9f, 6f, "door"),
        new Partition(1, false, 3f, 9f, "door"),
        new Partition(1, false, 3f, 12f, "plain"),
    };

    [MenuItem("Tools/Environment/Build Farmhouse")]
    static void BuildMenu()
    {
        var env = GameObject.Find("_Environment");
        var existing = env ? env.transform.Find(RootName) : null;
        Debug.Log(existing ? Build(existing.position, existing.eulerAngles.y) : Build(Vector3.zero, 0f));
    }

    public static string Build(Vector3 position, float yaw)
    {
        var env = GameObject.Find("_Environment");
        if (!env) return "No _Environment root found.";
        var old = env.transform.Find(RootName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject(RootName).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Farmhouse");
        root.SetParent(env.transform, false);
        root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        int count = 0;
        count += BuildExterior(Group(root, "Exterior Walls"), Group(root, "Foundation"));
        count += BuildInterior(Group(root, "Interior Walls"));
        count += BuildFloors(Group(root, "Floors"), Group(root, "Ceilings"));
        count += BuildStairs(Group(root, "Stairs"));
        count += BuildRoof(Group(root, "Roof"));
        count += BuildPorches(Group(root, "Porches"));
        BuildReflectionProbes(Group(root, "Reflection Probes"));
        int props = FarmhouseDecorator.Decorate(root);
        int doors = FinishOpenablesAndStatics(root.gameObject);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(env.scene);
        return $"Farmhouse built with {count} kit parts, {props} decor props, {doors} interactable doors/windows/drawers.";
    }

    public static int FinishOpenablesAndStatics(GameObject root)
    {
        int doors = ConfigureOpenables(root);
        MarkPlaced(root);
        MarkStatic(root);
        HideKitHelpers(root);
        return doors;
    }

    /// The kit's ceiling pieces carry red emissive editing markers that would bake red light into the rooms.
    public static int HideKitHelpers(GameObject root)
    {
        int hidden = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.EndsWith("_visualizationhelper") || !t.gameObject.activeSelf) continue;
            t.gameObject.SetActive(false);
            hidden++;
        }
        return hidden;
    }

    // ---------- Exterior ----------

    static int BuildExterior(Transform walls, Transform foundation)
    {
        int n = 0;
        for (int story = 0; story < 2; story++)
        {
            float y = story * Story;
            string set = InteriorSet(story);
            for (int m = 0; m < Front[story].Length; m++)
            {
                float a = m * 3f;
                n += ExteriorModule(walls, foundation, story, set, Front[story][m], new Vector3(a + 3f, y, Depth), 0f, new Vector3(a, y, Depth));
                n += ExteriorModule(walls, foundation, story, set, Back[story][m], new Vector3(a, y, 0f), 180f, new Vector3(a + 3f, y, 0f),
                    lining: story > 0 || m != StairsBackModule);
            }
            for (int m = 0; m < Left[story].Length; m++)
            {
                float a = m * 3f;
                n += ExteriorModule(walls, foundation, story, set, Right[story][m], new Vector3(Width, y, a), 90f, new Vector3(Width, y, a + 3f));
                n += ExteriorModule(walls, foundation, story, set, Left[story][m], new Vector3(0f, y, a + 3f), 270f, new Vector3(0f, y, a));
            }
            Place(walls, "WallOut_corner", new Vector3(Width, y, Depth), 0f);
            Place(walls, "WallOut_corner", new Vector3(Width, y, 0f), 90f);
            Place(walls, "WallOut_corner", new Vector3(0f, y, 0f), 180f);
            Place(walls, "WallOut_corner", new Vector3(0f, y, Depth), 270f);
            n += 4;
        }
        return n;
    }

    static int ExteriorModule(Transform walls, Transform foundation, int story, string set, string kind, Vector3 outPivot, float yaw, Vector3 inPivot, bool lining = true)
    {
        string outside, inside;
        switch (kind)
        {
            case "window": outside = "WallOut_3x_window_vinyl"; inside = "3x_window"; break;
            case "windowsmall": outside = "WallOut_3x_windowsmall_vinyl"; inside = "3x_windowsmall"; break;
            case "windowdouble": outside = "WallOut_3x_windowdouble_vinyl"; inside = "3x_windowdouble"; break;
            case "door": outside = "WallOut_3x_door"; inside = "3x_door"; break;
            case "frontdoor": outside = "WallOut_3x_door_windows"; inside = "3x_doordouble"; break;
            default: outside = "WallOut_3x"; inside = "3x"; break;
        }
        int n = 1;
        Place(walls, outside, outPivot, yaw);
        if (lining) { Place(walls, $"WallIn_{set}_{inside}", inPivot, yaw + 180f); n++; }
        if (story > 0) return n;
        Place(foundation, "Foundation_3x", outPivot + Vector3.down * 0.5f, yaw);
        return n + 1;
    }

    // ---------- Interior ----------

    static int BuildInterior(Transform parent)
    {
        int n = 0;
        foreach (var p in Partitions)
        {
            float y = p.story * Story;
            string set = InteriorSet(p.story);
            string len = Mathf.RoundToInt(p.length) + "x";
            string piece = p.kind == "door" ? len + "_door" : p.kind == "doordouble" ? len + "_doordouble" : len;
            string part = $"WallIn_{set}_{piece}";
            Vector3 posPivot, negPivot;
            float posYaw, negYaw;
            if (p.alongZ)
            {
                posPivot = new Vector3(p.c, y, p.start); posYaw = 90f;
                negPivot = new Vector3(p.c, y, p.start + p.length); negYaw = 270f;
            }
            else
            {
                posPivot = new Vector3(p.start + p.length, y, p.c); posYaw = 0f;
                negPivot = new Vector3(p.start, y, p.c); negYaw = 180f;
            }
            GameObject doorWall = null;
            if (p.posFace) { doorWall = Place(parent, part, posPivot, posYaw); n++; }
            if (p.negFace) { var w = Place(parent, part, negPivot, negYaw); doorWall = doorWall ? doorWall : w; n++; }
            if (doorWall && (p.kind == "door" || p.kind == "doordouble"))
                n += AddKitDoor(doorWall.transform, p.kind == "doordouble");
        }
        return n;
    }

    /// Same parts and offsets as the kit's "Spawn additional" buttons on WallIn_*_door / _doordouble.
    static int AddKitDoor(Transform wall, bool isDouble)
    {
        var offset = new Vector3(-1.5f, 0f, 0f);
        PlaceChild(wall, Kit + (isDouble ? "Doorframe_in_double" : "Doorframe_in_single"), offset);
        PlaceChild(wall, DoorKit + (isDouble ? "Door_parent_A_double" : "Door_parent_A_L"), offset);
        PlaceChild(wall, Kit + (isDouble ? "Floor_trim_doordouble" : "Floor_trim_door"), offset);
        return 3;
    }

    static void PlaceChild(Transform parent, string assetPathNoExt, Vector3 localPos)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPathNoExt + ".prefab");
        if (!prefab)
        {
            var guids = AssetDatabase.FindAssets(System.IO.Path.GetFileName(assetPathNoExt) + " t:Prefab", new[] { "Assets/AtmosphericHouse/Prefabs" });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == System.IO.Path.GetFileName(assetPathNoExt)) { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p); break; }
            }
        }
        if (!prefab) throw new System.ArgumentException("Missing kit part " + assetPathNoExt);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
    }

    // ---------- Floors, ceilings, stairs ----------

    static int BuildFloors(Transform floors, Transform ceilings)
    {
        int n = 0;
        for (float x = 0f; x < Width; x += 3f)
        for (float z = 0f; z < Depth; z += 3f)
        {
            bool hall = x == HallX;
            Fill(floors, "Floor_3x3", x, z, x + 3f, z + 3f, 0f);
            Fill(ceilings, "Ceiling_3x3", x, z, x + 3f, z + 3f, Story * 2f);
            n += 2;
            if (hall) continue;
            Fill(floors, "Floor_3x3", x, z, x + 3f, z + 3f, Story);
            Fill(ceilings, "Ceiling_3x3", x, z, x + 3f, z + 3f, Story);
            n += 2;
        }

        // Centre hall: stair opening over z 0..6, plain tile over the front 3 m.
        float x0 = HallX, x1 = HallX + 3f;
        Place(floors, "Floor_6x3_stairs_L", StairsPivot + Vector3.up * Story, StairsYaw);
        Place(ceilings, "Ceiling_6x3_stairs_L", StairsPivot + Vector3.up * Story, StairsYaw);
        Fill(floors, "Floor_3x3", x0, 6f, x1, Depth, Story);
        Fill(ceilings, "Ceiling_3x3", x0, 6f, x1, Depth, Story);
        return n + 4;
    }

    static int BuildStairs(Transform parent)
    {
        Place(parent, "Stairs_main_L", StairsPivot, StairsYaw);
        return 1;
    }

    // ---------- Roof and porches ----------

    static int BuildRoof(Transform parent)
    {
        float y = Story * 2f;
        for (float x = 3f; x <= Width; x += 3f)
            Place(parent, "Roof_A_9x", new Vector3(x, y, Depth), 0f);
        Place(parent, "Roof_A_9x_gable", new Vector3(Width, y, Depth), 0f);
        Place(parent, "Roof_A_9x_gable", new Vector3(0f, y, 0f), 180f);
        return 7;
    }

    static int BuildPorches(Transform parent)
    {
        // Full-width front porch, steps in front of the door. The end pieces close the porch flush with the
        // side walls; Porch_A_3x_corner is only for porches that wrap around onto a side wall.
        Place(parent, "Porch_A_3x_end_R", new Vector3(3f, 0f, Depth), 0f);
        Place(parent, "Porch_A_3x", new Vector3(6f, 0f, Depth), 0f);
        Place(parent, "Porch_A_3x_entrance", new Vector3(9f, 0f, Depth), 0f);
        Place(parent, "Porch_A_3x", new Vector3(12f, 0f, Depth), 0f);
        Place(parent, "Porch_A_3x_end_L", new Vector3(Width, 0f, Depth), 0f);
        // Back porch: the self-contained 6 m piece (posts and railings on both ends), steps centred on the kitchen door.
        Place(parent, "Porch_A_6x_entrance_separate", new Vector3(10.5f, 0f, 0f), 180f);
        return 6;
    }

    // ---------- Helpers ----------

    static string InteriorSet(int story) => story == 0 ? "A" : "B";

    static Transform Group(Transform root, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(root, false);
        return t;
    }

    static GameObject Place(Transform parent, string part, Vector3 localPos, float localYaw)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + part + ".prefab");
        if (!prefab) throw new System.ArgumentException("Missing kit part " + part);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, localYaw, 0f);
        return go;
    }

    /// Places a flat floor/ceiling tile so it exactly covers the rectangle, rotating it 90 degrees when needed.
    static GameObject Fill(Transform parent, string part, float x0, float z0, float x1, float z1, float y)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Kit + part + ".prefab");
        if (!prefab) throw new System.ArgumentException("Missing kit part " + part);
        var b = LocalBounds(prefab);
        float w = x1 - x0, d = z1 - z0;
        float yaw = Mathf.Abs(b.size.x - w) < 0.05f && Mathf.Abs(b.size.z - d) < 0.05f ? 0f : 90f;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var c0 = rot * b.min;
        var c1 = rot * b.max;
        var min = Vector3.Min(c0, c1);
        var go = Place(parent, part, new Vector3(x0 - min.x, y, z0 - min.z), yaw);
        return go;
    }

    static Bounds LocalBounds(GameObject prefab)
    {
        var renderers = prefab.GetComponentsInChildren<MeshRenderer>();
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    /// The player's interact raycast ignores the Default layer, so kit doors go on "Door" (with the basement
    /// door sounds) and openable windows on "Interactable", matching the doors already in the game.
    static int ConfigureOpenables(GameObject root)
    {
        int doorLayer = LayerMask.NameToLayer("Door");
        int interactLayer = LayerMask.NameToLayer("Interactable");
        var open = AssetDatabase.LoadAssetAtPath<AudioClip>(DoorOpenSound);
        var close = AssetDatabase.LoadAssetAtPath<AudioClip>(DoorCloseSound);
        var handle = AssetDatabase.LoadAssetAtPath<AudioClip>(DoorHandleSound);
        int n = 0;
        foreach (var door in root.GetComponentsInChildren<FS_Atmo.SimpleOpenClose>(true))
        {
            bool isDoor = door.name.StartsWith("Door_mesh");
            door.gameObject.layer = isDoor ? doorLayer : interactLayer;
            if (isDoor)
            {
                var so = new SerializedObject(door);
                so.FindProperty("openSound").objectReferenceValue = open;
                so.FindProperty("closeSound").objectReferenceValue = close;
                so.FindProperty("handleOpenSound").objectReferenceValue = handle;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            n++;
        }
        return n;
    }

    /// Kit pieces auto-spawn their "spawn along" parts the first time they are selected unless marked placed;
    /// everything here is already complete.
    static void MarkPlaced(GameObject root)
    {
        foreach (var swap in root.GetComponentsInChildren<PrefabSwapper.SwapPrefab>(true))
        {
            swap.placed = true;
            EditorUtility.SetDirty(swap);
        }
    }

    static void MarkStatic(GameObject root)
    {
        const StaticEditorFlags flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic |
                                        StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
        var decor = root.transform.Find(FarmhouseDecorator.DecorName);
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            // Openable doors and windows animate, so they stay dynamic.
            if (t.GetComponentInParent<Animator>(true)) continue;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            // Props are lit by the probe volume: the Poly Haven models have no lightmap UVs, and small props
            // would only waste lightmap space.
            if (decor && t.IsChildOf(decor) && t.TryGetComponent<MeshRenderer>(out var r)) r.receiveGI = ReceiveGI.LightProbes;
        }
    }

    // ---------- Reflection probes ----------

    static readonly (string name, int story, float x0, float z0, float x1, float z1)[] ProbeRooms =
    {
        ("Living Room", 0, 0f, 0f, 6f, 9f), ("Hall", 0, 6f, 0f, 9f, 9f), ("Dining Room", 0, 9f, 3f, 15f, 9f), ("Kitchen", 0, 9f, 0f, 15f, 3f),
        ("Main Bedroom", 1, 0f, 0f, 6f, 9f), ("Landing", 1, 6f, 0f, 9f, 9f), ("Bedroom", 1, 9f, 3f, 15f, 9f), ("Bathroom", 1, 9f, 0f, 15f, 3f),
    };

    /// One baked box-projected probe per room, plus a low-importance probe for the porches and yard.
    static int BuildReflectionProbes(Transform parent)
    {
        foreach (var room in ProbeRooms)
        {
            var size = new Vector3(room.x1 - room.x0, Story - 0.2f, room.z1 - room.z0);
            var centre = new Vector3((room.x0 + room.x1) * 0.5f, room.story * Story + size.y * 0.5f, (room.z0 + room.z1) * 0.5f);
            AddProbe(parent, "Probe " + room.name, centre, size, 1, true, 0.3f);
        }
        AddProbe(parent, "Probe Yard", new Vector3(Width * 0.5f, 3f, Depth * 0.5f), new Vector3(60f, 20f, 60f), 0, false, 1f);
        return ProbeRooms.Length + 1;
    }

    static void AddProbe(Transform parent, string name, Vector3 localCentre, Vector3 localSize, int importance, bool boxProjection, float blend)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCentre;
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
        // Probe boxes are world-axis aligned, so the size follows the farmhouse yaw.
        var s = parent.rotation * localSize;
        probe.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        probe.boxProjection = boxProjection;
        probe.blendDistance = blend;
        probe.importance = importance;
        probe.resolution = 128;
    }
}
#endif
