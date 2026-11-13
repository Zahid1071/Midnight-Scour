#if UNITY_EDITOR
using System.Collections.Generic;
using FS_Atmo;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gameplay wiring for the farmhouse built by <see cref="FarmhouseBuilder"/>: butcher-aware door controllers with
/// AI pre-points, soft-push on door leaves, climb-through gaps on the openable windows, the farmhouse part of
/// the AI room graph (one RoomAnchor per room, one edge per doorway) and the hiding spots, each parented to the
/// RoomAnchor of its room so the butcher's room search finds it. Re-run after every rebuild.
/// </summary>
public static class FarmhouseGameplay
{
    const string ZonePath = "NPC Zones/Demo_Environment";
    const string OutsideRoom = "Outside_Room";
    const string ClimbCameraName = "Window Climb Camera";
    const float PrePointDistance = 1f;
    const string HidingPrefabs = "Assets/Prefabs/Objects/Hiding Spots/";
    const string LockerPrefab = "Locker Hiding Spot";
    // Locker root sits at mid-height; its back is 0.25 m behind the root and the door faces local -x.
    const float LockerRootHeight = 0.88f;
    const float LockerWallOffset = 0.37f;

    struct Room
    {
        public string anchor;
        public int story;
        public Rect area;
        public Vector3 spot; // anchor position, kept clear of furniture
        public Room(string anchor, int story, float x0, float z0, float x1, float z1, float sx, float sz)
        {
            this.anchor = anchor; this.story = story; area = Rect.MinMaxRect(x0, z0, x1, z1);
            spot = new Vector3(sx, story * FarmhouseBuilder.Story + (story > 0 ? 0.13f : 0f), sz);
        }
    }

    // Farmhouse frame. The stairwell belongs to Main_Room on both floors, so hall and landing are one room.
    static readonly Room[] Rooms =
    {
        new Room("Living_Room", 0, 0f, 6f, 6f, 12f, 4.8f, 10.2f),
        new Room("Study_Room", 0, 0f, 0f, 6f, 6f, 3.4f, 3.6f),
        new Room("Main_Room", 0, 6f, 0f, 12f, 12f, 10.5f, 9f),
        new Room("Dining_Room", 0, 12f, 6f, 21f, 12f, 19.6f, 9f),
        new Room("Kitchen_Room", 0, 12f, 0f, 21f, 6f, 14f, 3.8f),
        new Room("Main_Room", 1, 6f, 6f, 12f, 12f, 10.5f, 9f),
        new Room("Main_Room", 1, 6f, 0f, 9f, 6f, 10.5f, 9f),
        new Room("Main_Bedroom_Room", 1, 0f, 6f, 6f, 12f, 3.8f, 9.8f),
        new Room("Sewing_Room", 1, 0f, 0f, 6f, 6f, 2.2f, 3.4f),
        new Room("Bathroom_Room", 1, 9f, 0f, 12f, 6f, 10.8f, 2f),
        new Room("Child_Room", 1, 12f, 6f, 21f, 12f, 16.5f, 9.6f),
        new Room("Storage_Room", 1, 12f, 0f, 21f, 6f, 16.5f, 3.2f),
    };

    struct Hide
    {
        public string name, prefab, room, replaces;
        public Vector3 pos;
        public float yaw;
        public HidingSpot.UsableSide side;
    }

    /// Locker with its back to a wall, door facing the room. Wall sides as in FarmhouseDecorator.OnWall:
    /// 'S' wall on z = line with the room on +z, 'N' room on -z, 'W' wall on x = line with the room on +x, 'E' room on -x.
    static Hide Locker(string name, string room, int story, char side, float line, float along)
    {
        float y = story * FarmhouseBuilder.Story + LockerRootHeight;
        var h = new Hide { name = name, prefab = LockerPrefab, room = room, side = HidingSpot.UsableSide.Both };
        switch (side)
        {
            case 'S': h.yaw = 90f; h.pos = new Vector3(along, y, line + LockerWallOffset); break;
            case 'N': h.yaw = 270f; h.pos = new Vector3(along, y, line - LockerWallOffset); break;
            case 'W': h.yaw = 180f; h.pos = new Vector3(line + LockerWallOffset, y, along); break;
            default: h.yaw = 0f; h.pos = new Vector3(line - LockerWallOffset, y, along); break;
        }
        return h;
    }

    // Spots placed from prefabs. The living room, main bedroom and child's room lockers and the king bed were
    // built by hand in the Gameplay group and are moved under their anchors by AdoptHidingSpots. Doorways and
    // windows to keep clear are listed in FarmhouseDecorator.
    static readonly Hide[] HidingSpots =
    {
        Locker("Locker Hiding Spot Study", "Study_Room", 0, 'E', 6f, 4.55f),
        Locker("Locker Hiding Spot Hall", "Main_Room", 0, 'E', 12f, 9.4f),
        Locker("Locker Hiding Spot Dining Room", "Dining_Room", 0, 'W', 12f, 10.8f),
        Locker("Locker Hiding Spot Kitchen", "Kitchen_Room", 0, 'N', 6f, 14.2f),
        Locker("Locker Hiding Spot Sewing Room", "Sewing_Room", 1, 'E', 6f, 2.4f),
        Locker("Locker Hiding Spot Landing", "Main_Room", 1, 'E', 12f, 10.7f),
        Locker("Locker Hiding Spot Storage Room", "Storage_Room", 1, 'E', 21f, 3.3f),
        // Kit twin bed of the child's room, swapped for a hideable one with the same mesh pose. The wall-side
        // pivot is against the east wall, so only the room side is usable.
        new Hide
        {
            name = "Bed_twin Hiding Spot", prefab = "Bed_twin Hiding Spot", room = "Child_Room", replaces = "Bed_twin",
            pos = new Vector3(19.5f, FarmhouseBuilder.Story, 6.86f), yaw = 180f, side = HidingSpot.UsableSide.RightOnly,
        },
    };

    [MenuItem("Tools/Environment/Wire Farmhouse Gameplay")]
    static void WireMenu()
    {
        var env = GameObject.Find("_Environment");
        var house = env ? env.transform.Find(FarmhouseBuilder.RootName) : null;
        if (!house) { Debug.LogError("No farmhouse to wire."); return; }
        Debug.Log(Wire(house));
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(house.gameObject.scene);
    }

    public static string Wire(Transform house)
    {
        int push = AddSoftPush(house);
        int gaps = AddClimbGaps(house);
        var anchors = EnsureAnchors(house);
        int edges = anchors != null ? RebuildEdges(house, anchors) : 0;
        int hides = anchors != null ? AdoptHidingSpots(house, anchors) + EnsureHidingSpots(house, anchors) : 0;
        return $"Gameplay: {push} soft-push doors, {gaps} climb-through windows, {edges} room graph edges, {hides} hiding spots.";
    }

    /// True when a farmhouse room anchor holds a child whose name starts with prefix (e.g. a bed hiding spot).
    public static bool RoomsHold(string prefix)
    {
        var zone = Zone();
        if (!zone) return false;
        foreach (var room in Rooms)
        {
            var anchor = zone.Find(room.anchor);
            if (!anchor) continue;
            foreach (Transform c in anchor)
                if (c.name.StartsWith(prefix)) return true;
        }
        return false;
    }

    static Transform Zone()
    {
        var zones = GameObject.Find("NPC Zones");
        return zones ? zones.transform.Find("Demo_Environment") : null;
    }

    // ---------- Doors ----------

    static int AddSoftPush(Transform house)
    {
        int n = 0;
        foreach (var door in house.GetComponentsInChildren<SimpleOpenClose>(true))
        {
            if (!door.name.StartsWith("Door_mesh")) continue;
            if (!door.GetComponent<DoorSoftPush>()) Undo.AddComponent<DoorSoftPush>(door.gameObject);
            n++;
        }
        return n;
    }

    /// Door controller on a frame: carves the NavMesh while the leaf is shut and lets the butcher open it.
    static DoorController EnsureDoorController(Transform frame, SimpleOpenClose leaf, float width)
    {
        var obstacle = frame.GetComponent<NavMeshObstacle>();
        if (!obstacle) obstacle = Undo.AddComponent<NavMeshObstacle>(frame.gameObject);
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = new Vector3(0f, 1.13f, 0f);
        obstacle.size = new Vector3(width, 2.27f, 0.29f);
        obstacle.carving = true;

        var dc = frame.GetComponent<DoorController>();
        if (!dc) dc = Undo.AddComponent<DoorController>(frame.gameObject);
        var so = new SerializedObject(dc);
        so.FindProperty("simpleOpenClose").objectReferenceValue = leaf;
        so.FindProperty("carveObstacle").objectReferenceValue = obstacle;
        so.ApplyModifiedPropertiesWithoutUndo();
        return dc;
    }

    static Transform PrePoint(Transform parent, string name, Vector3 world)
    {
        var t = parent.Find(name);
        if (!t)
        {
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
        }
        t.position = world;
        t.rotation = Quaternion.identity;
        return t;
    }

    // ---------- Windows ----------

    static int AddClimbGaps(Transform house)
    {
        var walls = house.Find("Exterior Walls");
        if (!walls) return 0;
        var camGo = GameObject.Find(ClimbCameraName);
        var cam = camGo ? camGo.GetComponent("CinemachineCamera") : null;
        int layer = LayerMask.NameToLayer("Interactable");
        int n = 0;
        foreach (var t in walls.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Window_single_vinyl") continue;
            var openable = t.Find("Window_single_vinyl_openable");
            var window = openable ? openable.GetComponent<SimpleOpenClose>() : null;
            if (!window) continue;
            var gap = t.Find("Climb Gap");
            if (!gap)
            {
                gap = new GameObject("Climb Gap").transform;
                Undo.RegisterCreatedObjectUndo(gap.gameObject, "Climb Gap");
                gap.SetParent(t, false);
                gap.localPosition = new Vector3(0f, 0.08f, 0f);
                gap.localRotation = Quaternion.Euler(0f, 180f, 0f);
                gap.gameObject.layer = layer;
                var box = gap.gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(0.85f, 0.55f, 0.12f);
                gap.gameObject.AddComponent<WindowClimbThrough>();
            }
            var climb = gap.GetComponent<WindowClimbThrough>();
            var so = new SerializedObject(climb);
            so.FindProperty("window").objectReferenceValue = window;
            so.FindProperty("gapCollider").objectReferenceValue = gap.GetComponent<BoxCollider>();
            so.FindProperty("outwardAxis").objectReferenceValue = gap;
            if (cam) so.FindProperty("climbCamera").objectReferenceValue = cam;
            so.ApplyModifiedPropertiesWithoutUndo();
            n++;
        }
        return n;
    }

    // ---------- Room graph ----------

    static Dictionary<string, RoomAnchor> EnsureAnchors(Transform house)
    {
        var zone = Zone();
        if (!zone) { Debug.LogWarning($"FarmhouseGameplay: {ZonePath} not found; room graph not wired."); return null; }

        var result = new Dictionary<string, RoomAnchor>();
        foreach (var room in Rooms)
        {
            if (result.ContainsKey(room.anchor)) continue;
            var t = zone.Find(room.anchor);
            if (!t)
            {
                t = new GameObject(room.anchor).transform;
                Undo.RegisterCreatedObjectUndo(t.gameObject, "Room Anchor");
                t.SetParent(zone, false);
            }
            // Hotspots and ambush points under the anchor are placed by hand; moving the anchor must not drag them.
            var kids = new List<(Transform t, Vector3 p, Quaternion r)>();
            foreach (Transform c in t) kids.Add((c, c.position, c.rotation));
            Undo.RecordObject(t, "Room Anchor");
            t.position = house.TransformPoint(room.spot);
            foreach (var (c, p, r) in kids)
            {
                Undo.RecordObject(c, "Room Anchor");
                c.SetPositionAndRotation(p, r);
            }
            var anchor = t.GetComponent<RoomAnchor>();
            if (!anchor) anchor = Undo.AddComponent<RoomAnchor>(t.gameObject);
            result[room.anchor] = anchor;
        }
        var outside = zone.Find(OutsideRoom);
        if (outside && outside.TryGetComponent<RoomAnchor>(out var o)) result[OutsideRoom] = o;
        return result;
    }

    static string RoomAt(Vector3 local)
    {
        int story = local.y > FarmhouseBuilder.Story * 0.5f ? 1 : 0;
        var p = new Vector2(local.x, local.z);
        foreach (var r in Rooms)
            if (r.story == story && r.area.Contains(p)) return r.anchor;
        return OutsideRoom;
    }

    static int RebuildEdges(Transform house, Dictionary<string, RoomAnchor> anchors)
    {
        var graph = Object.FindFirstObjectByType<RoomGraph>();
        if (!graph) return 0;
        Undo.RecordObject(graph, "Farmhouse Room Graph");
        // Edges into the old house lose their pre-points when it is destroyed; edges already wired to this
        // house are rebuilt below.
        graph.edges.RemoveAll(e => e == null || !e.prePointA || !e.prePointB || e.prePointA.IsChildOf(house));

        int n = 0;
        var interior = house.Find("Interior Walls");
        foreach (var (centre, alongZ, hasLeaf) in FarmhouseBuilder.InteriorDoorways())
        {
            var frame = FindFrame(interior, house, centre, "Doorframe_in_");
            if (!frame) { Debug.LogWarning($"FarmhouseGameplay: no door frame at {centre}."); continue; }
            var normal = alongZ ? Vector3.right : Vector3.forward;
            n += AddEdge(graph, house, anchors, frame, centre, normal, hasLeaf, hasLeaf ? 1.23f : 2.23f) ? 1 : 0;
        }

        var exterior = house.Find("Exterior Walls");
        foreach (var frame in exterior.GetComponentsInChildren<Transform>(true))
        {
            if (!frame.name.StartsWith("Doorframe_out_")) continue;
            var centre = house.InverseTransformPoint(frame.position);
            centre.y = Mathf.Round(centre.y / FarmhouseBuilder.Story) * FarmhouseBuilder.Story;
            var normal = Mathf.Abs(centre.z) < 0.5f || Mathf.Abs(centre.z - FarmhouseBuilder.Depth) < 0.5f ? Vector3.forward : Vector3.right;
            float width = frame.name.Contains("windows") ? 2.29f : 1.26f;
            n += AddEdge(graph, house, anchors, frame, centre, normal, true, width) ? 1 : 0;
        }
        EditorUtility.SetDirty(graph);
        return n;
    }

    static Transform FindFrame(Transform walls, Transform house, Vector3 centre, string prefix)
    {
        foreach (var t in walls.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith(prefix)) continue;
            if (Vector3.Distance(house.InverseTransformPoint(t.position), centre) < 0.3f) return t;
        }
        return null;
    }

    static bool AddEdge(RoomGraph graph, Transform house, Dictionary<string, RoomAnchor> anchors, Transform frame,
        Vector3 centre, Vector3 normal, bool hasLeaf, float width)
    {
        var sideA = centre - normal * PrePointDistance;
        var sideB = centre + normal * PrePointDistance;
        string roomA = RoomAt(sideA), roomB = RoomAt(sideB);
        if (!anchors.TryGetValue(roomA, out var a) || !anchors.TryGetValue(roomB, out var b) || a == b)
        {
            Debug.LogWarning($"FarmhouseGameplay: doorway at {centre} joins {roomA} and {roomB}; skipped.");
            return false;
        }

        SimpleOpenClose leaf = null;
        if (hasLeaf)
            foreach (var s in frame.parent.GetComponentsInChildren<SimpleOpenClose>(true))
                if (s.name.StartsWith("Door_mesh")) { leaf = s; break; }

        DoorController dc = leaf ? EnsureDoorController(frame, leaf, width) : null;
        var preA = PrePoint(frame, "PrePointA", house.TransformPoint(sideA));
        var preB = PrePoint(frame, "PrePointB", house.TransformPoint(sideB));
        if (dc)
        {
            dc.prePointA = preA;
            dc.prePointB = preB;
            EditorUtility.SetDirty(dc);
        }
        graph.edges.Add(new DoorEdge { roomA = a, roomB = b, door = dc, prePointA = preA, prePointB = preB });
        return true;
    }

    // ---------- Hiding spots ----------

    /// Moves hiding spots built in the Gameplay group under the anchor of the room they stand in.
    static int AdoptHidingSpots(Transform house, Dictionary<string, RoomAnchor> anchors)
    {
        var gameplay = house.Find(FarmhouseBuilder.GameplayName);
        if (!gameplay) return 0;
        var spots = new List<Transform>();
        foreach (Transform t in gameplay)
            if (t.GetComponentInChildren<HidingSpot>(true)) spots.Add(t);

        int n = 0;
        foreach (var t in spots)
        {
            string room = RoomAt(house.InverseTransformPoint(t.position));
            if (room == OutsideRoom || !anchors.TryGetValue(room, out var anchor))
            {
                Debug.LogWarning($"FarmhouseGameplay: hiding spot {t.name} is in no farmhouse room; left in {gameplay.name}.");
                continue;
            }
            Undo.SetTransformParent(t, anchor.transform, "Adopt Hiding Spot");
            n++;
        }
        return n;
    }

    /// Places the missing spots of <see cref="HidingSpots"/>; existing ones keep any hand-tuned pose.
    static int EnsureHidingSpots(Transform house, Dictionary<string, RoomAnchor> anchors)
    {
        var decor = house.Find(FarmhouseDecorator.DecorName);
        int n = 0;
        foreach (var h in HidingSpots)
        {
            if (!anchors.TryGetValue(h.room, out var anchor)) continue;
            var t = anchor.transform.Find(h.name);
            if (!t)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HidingPrefabs + h.prefab + ".prefab");
                if (!prefab) { Debug.LogWarning($"FarmhouseGameplay: missing hiding spot prefab {h.prefab}."); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor.transform);
                Undo.RegisterCreatedObjectUndo(go, "Hiding Spot");
                go.name = h.name;
                t = go.transform;
                t.SetPositionAndRotation(house.TransformPoint(h.pos), house.rotation * Quaternion.Euler(0f, h.yaw, 0f));
                var spot = t.GetComponentInChildren<HidingSpot>(true);
                spot.usableSide = h.side;
                PrefabUtility.RecordPrefabInstancePropertyModifications(spot);
            }
            if (h.replaces != null && decor)
                foreach (var prop in decor.GetComponentsInChildren<Transform>(true))
                    if (prop && prop.name == h.replaces && Vector3.Distance(prop.position, t.position) < 1f)
                        Undo.DestroyObjectImmediate(prop.gameObject);
            n++;
        }
        return n;
    }
}
#endif
