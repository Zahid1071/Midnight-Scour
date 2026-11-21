#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Wires the placed Zombie Dog into the existing NPC AI stack: DogPen room/hotspots,
/// NPCData routines (idle / nap / patrol), NavMeshAgent, perception, chase on sight.
/// </summary>
public static class DogNpcSetup
{
    const string ZoneName = "DogPen";
    const string RoomName = "DogPen_Room";
    const string DogDataPath = "Assets/Scriptable Objects/NPC Scriptable Objecst/Dog Data.asset";
    const string ZonesPath = "NPC Zones";
    const string DogName = "Zombie Dog";

    static readonly Vector3[] HotspotLocalOffsets =
    {
        new Vector3(0.4f, 0f, -0.2f),   // sleep / hang near spawn
        new Vector3(-1.6f, 0f, 1.8f),   // toward gate
        new Vector3(1.8f, 0f, 1.2f),    // opposite corner
        new Vector3(-1.2f, 0f, -1.8f),  // back corner
        new Vector3(1.4f, 0f, -1.5f),   // side patrol
    };

    static readonly string[] HotspotNames =
    {
        "Hotspot_Dog_Sleep",
        "Hotspot_Dog_Gate",
        "Hotspot_Dog_CornerA",
        "Hotspot_Dog_CornerB",
        "Hotspot_Dog_Patrol",
    };

    [MenuItem("Tools/Environment/Make Dog NPC Functional")]
    static void MenuMakeFunctional() => Debug.Log(MakeFunctional());

    public static string MakeFunctional()
    {
        var dog = GameObject.Find(DogName);
        if (!dog) return "No 'Zombie Dog' in the scene. Run Tools/Environment/Place Dog Pen first.";

        var pen = dog.transform.parent;
        if (!pen || pen.name != DogPenSetup.RootName)
            return "Zombie Dog must be under the Dog Pen root.";

        var zonesRoot = GameObject.Find(ZonesPath);
        if (!zonesRoot) return "No 'NPC Zones' root found.";

        var mgr = Object.FindFirstObjectByType<NPCAnchorManager>();
        if (!mgr) return "No NPCAnchorManager in the scene.";

        var player = GameObject.Find("Player");
        if (!player) return "No Player found.";

        var data = EnsureDogData(dog);
        var room = EnsureRoomAnchors(zonesRoot.transform, pen);
        RegisterAnchorRoot(mgr, room.parent); // DogPen zone

        WireDog(dog, data, player.transform, room);

        EditorUtility.SetDirty(dog);
        EditorUtility.SetDirty(mgr);
        EditorSceneManager.MarkSceneDirty(dog.scene);
        return $"Dog NPC ready under zone '{ZoneName}' / room '{RoomName}' with {HotspotNames.Length} hotspots.";
    }

    static NPCData EnsureDogData(GameObject dog)
    {
        var data = AssetDatabase.LoadAssetAtPath<NPCData>(DogDataPath);
        if (!data)
        {
            data = ScriptableObject.CreateInstance<NPCData>();
            AssetDatabase.CreateAsset(data, DogDataPath);
        }

        data.npcName = "Zombie Dog";
        data.homeID = ZoneName;
        data.captureChance = 0f;
        data.patrolChance = 0f;
        data.ambushChance = 0f;
        data.footstepVolume = 0.6f;

        data.vision = new VisionSettings
        {
            maxViewDistance = 14f,
            maxViewAngle = 85f,
            ignoreLayer = LayerMask.GetMask("Ignore Raycast", "UI", "NPC"),
            confirmSeconds = 0.6f,
            buildRate = 2f,
            decayRate = 0.8f,
            instantConfirmDistance = 3.5f,
        };

        // Match butcher ignore bits if layer names differ in this project.
        if (data.vision.ignoreLayer == 0)
            data.vision.ignoreLayer = (LayerMask)144516;

        data.hearing = new HearingSettings { ambushSuspicionMultiplier = 0.5f };
        data.suspicion = new SuspicionSettings
        {
            baseThreshold = 100f,
            baseDecayPerSecond = 8f,
            footstepThreshold = 100f,
            footstepDecayPerSecond = 12f,
        };
        data.locomotion = new LocomotionSettings
        {
            walkingSpeed = 2.2f,
            runningSpeed = 6.5f,
        };
        data.chaseRetention = new ChaseRetentionSettings
        {
            gracePeriod = 4f,
            keepChasingDistance = 6f,
        };
        data.poiLook = new POILookSettings
        {
            naturalPoiIntervalSeconds = 0f,
            naturalLookHoldChance = 0f,
        };

        data.weightedRoutinePlans = new List<WeightedZonePlan>
        {
            new WeightedZonePlan
            {
                zoneID = ZoneName,
                weight = 1f,
                roomPlans = new List<WeightedRoomPlan>
                {
                    new WeightedRoomPlan
                    {
                        roomAnchorID = RoomName,
                        weight = 1f,
                        hotspots = new List<WeightedHotspot>
                        {
                            new WeightedHotspot
                            {
                                hotspotID = HotspotNames[0],
                                weight = 0.35f,
                                duration = 8f,
                                actionType = HotspotActionType.Nap,
                                requiresVisualOffset = false,
                            },
                            new WeightedHotspot
                            {
                                hotspotID = HotspotNames[1],
                                weight = 0.2f,
                                duration = 5f,
                                actionType = HotspotActionType.IdleAction,
                                requiresVisualOffset = false,
                            },
                            new WeightedHotspot
                            {
                                hotspotID = HotspotNames[2],
                                weight = 0.15f,
                                duration = 4f,
                                actionType = HotspotActionType.IdleAction,
                                requiresVisualOffset = false,
                            },
                            new WeightedHotspot
                            {
                                hotspotID = HotspotNames[3],
                                weight = 0.15f,
                                duration = 4f,
                                actionType = HotspotActionType.IdleAction,
                                requiresVisualOffset = false,
                            },
                            new WeightedHotspot
                            {
                                hotspotID = HotspotNames[4],
                                weight = 0.15f,
                                duration = 3f,
                                actionType = HotspotActionType.IdleAction,
                                requiresVisualOffset = false,
                            },
                        },
                    },
                },
            },
        };

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return data;
    }

    static Transform EnsureRoomAnchors(Transform zonesRoot, Transform pen)
    {
        var zone = zonesRoot.Find(ZoneName);
        if (!zone)
        {
            var go = new GameObject(ZoneName);
            Undo.RegisterCreatedObjectUndo(go, "Dog Pen Zone");
            zone = go.transform;
            zone.SetParent(zonesRoot, false);
        }

        if (!zone.GetComponent<NPCZone>())
            Undo.AddComponent<NPCZone>(zone.gameObject);

        var room = zone.Find(RoomName);
        if (!room)
        {
            var go = new GameObject(RoomName);
            Undo.RegisterCreatedObjectUndo(go, "Dog Pen Room");
            room = go.transform;
            room.SetParent(zone, false);
        }

        room.position = pen.position;
        if (!room.GetComponent<RoomAnchor>())
            Undo.AddComponent<RoomAnchor>(room.gameObject);

        // Clear old hotspots so re-run is idempotent.
        for (int i = room.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(room.GetChild(i).gameObject);

        for (int i = 0; i < HotspotNames.Length; i++)
        {
            var hs = new GameObject(HotspotNames[i]).transform;
            Undo.RegisterCreatedObjectUndo(hs.gameObject, "Dog Hotspot");
            hs.SetParent(room, false);
            // Pen root yaw 270: convert local pen offsets into world.
            hs.position = pen.TransformPoint(HotspotLocalOffsets[i]);
            SampleGround(hs);
        }

        return room;
    }

    static void SampleGround(Transform t)
    {
        if (NavMesh.SamplePosition(t.position, out var hit, 2f, NavMesh.AllAreas))
            t.position = hit.position;
    }

    static void RegisterAnchorRoot(NPCAnchorManager mgr, Transform zone)
    {
        var so = new SerializedObject(mgr);
        var roots = so.FindProperty("anchorRoots");
        for (int i = 0; i < roots.arraySize; i++)
        {
            var existing = roots.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (existing == zone) return;
        }

        roots.arraySize++;
        roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = zone;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mgr);
    }

    static void WireDog(GameObject dog, NPCData data, Transform player, Transform room)
    {
        int npcLayer = LayerMask.NameToLayer("NPC");
        if (npcLayer >= 0)
            dog.layer = npcLayer;

        var agent = dog.GetComponent<NavMeshAgent>();
        if (!agent) agent = Undo.AddComponent<NavMeshAgent>(dog);
        agent.radius = 0.35f;
        agent.height = 0.7f;
        agent.baseOffset = 0f;
        agent.speed = data.locomotion.walkingSpeed;
        agent.angularSpeed = 720f;
        agent.acceleration = 40f;
        agent.stoppingDistance = 0.35f;
        agent.autoBraking = true;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;

        var col = dog.GetComponent<CapsuleCollider>();
        if (!col) col = Undo.AddComponent<CapsuleCollider>(dog);
        col.radius = 0.28f;
        col.height = 0.7f;
        col.center = new Vector3(0f, 0.35f, 0f);
        col.direction = 1;

        if (!dog.GetComponent<AudioSource>())
            Undo.AddComponent<AudioSource>(dog);

        var npc = dog.GetComponent<NPCController>() ?? Undo.AddComponent<NPCController>(dog);
        var interaction = dog.GetComponent<InteractionController>() ?? Undo.AddComponent<InteractionController>(dog);
        var perception = dog.GetComponent<PerceptionController>() ?? Undo.AddComponent<PerceptionController>(dog);
        if (!dog.GetComponent<SuspicionSystem>())
            Undo.AddComponent<SuspicionSystem>(dog);
        var navigator = dog.GetComponent<FS_Atmo.Navigator>() ?? Undo.AddComponent<FS_Atmo.Navigator>(dog);
        var bridge = dog.GetComponent<DogAnimatorBridge>() ?? Undo.AddComponent<DogAnimatorBridge>(dog);

        var face = dog.transform.Find("Face");
        if (!face)
        {
            face = new GameObject("Face").transform;
            Undo.RegisterCreatedObjectUndo(face.gameObject, "Dog Face");
            face.SetParent(dog.transform, false);
        }

        var head = FindChild(dog.transform, "head");
        if (head)
            face.position = head.position;
        else
            face.localPosition = new Vector3(0f, 0.55f, 0.35f);
        face.rotation = dog.transform.rotation;

        var soNpc = new SerializedObject(npc);
        soNpc.FindProperty("captureDistance").floatValue = 1.4f;
        soNpc.FindProperty("hideExitSecondsBeforeCapture").floatValue = 0.5f;
        soNpc.FindProperty("chaseVisibleSpeedMultiplier").floatValue = 1.15f;
        soNpc.FindProperty("jumpscarePoint").objectReferenceValue = face;
        soNpc.ApplyModifiedPropertiesWithoutUndo();

        var soIx = new SerializedObject(interaction);
        soIx.FindProperty("data").objectReferenceValue = data;
        soIx.FindProperty("agent").objectReferenceValue = agent;
        soIx.FindProperty("remainHomeFactor").floatValue = 1f;
        soIx.FindProperty("visualRoot").objectReferenceValue = null;
        soIx.ApplyModifiedPropertiesWithoutUndo();

        var soPerc = new SerializedObject(perception);
        soPerc.FindProperty("player").objectReferenceValue = player;
        soPerc.FindProperty("face").objectReferenceValue = face;
        soPerc.FindProperty("faceForwardAxis").enumValueIndex = 0; // Forward
        soPerc.ApplyModifiedPropertiesWithoutUndo();

        var soNav = new SerializedObject(navigator);
        soNav.FindProperty("homeZoneID").stringValue = ZoneName;
        soNav.FindProperty("logPlannedRoute").boolValue = false;
        soNav.ApplyModifiedPropertiesWithoutUndo();

        var soBridge = new SerializedObject(bridge);
        soBridge.FindProperty("animator").objectReferenceValue = dog.GetComponent<Animator>();
        soBridge.FindProperty("agent").objectReferenceValue = agent;
        soBridge.FindProperty("npc").objectReferenceValue = npc;
        soBridge.ApplyModifiedPropertiesWithoutUndo();

        // Seat the agent on the NavMesh at its current pose.
        if (NavMesh.SamplePosition(dog.transform.position, out var hit, 2f, NavMesh.AllAreas))
        {
            dog.transform.position = hit.position;
            agent.Warp(hit.position);
        }

        EditorUtility.SetDirty(dog);
        EditorUtility.SetDirty(room.gameObject);
    }

    static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindChild(root.GetChild(i), name);
            if (found) return found;
        }
        return null;
    }
}
#endif
