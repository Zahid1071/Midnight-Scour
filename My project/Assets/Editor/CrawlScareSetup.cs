using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Formats.Alembic.Importer;

/// <summary>
/// Wires the small-house crawl scare in Room08 with the car battery as the hold-E trigger.
/// </summary>
public static class CrawlScareSetup
{
    const string Folder = "Assets/Models/Corpse/Crawl Scare";
    const string FbxPath = Folder + "/Crawl_Scare.fbx";
    const string AbcPath = Folder + "/Crawl_Sheet.abc";
    const string ControllerPath = Folder + "/Crawl_Scare.controller";
    const string PrefabPath = "Assets/Prefabs/Environment/Crawl Scare.prefab";
    const string StateName = "Crawl Scare";
    const string StartSfxPath = "Assets/Audio/SFX/Corpse Scare/dragon-studio-zombie-sound-2-357976.mp3";
    const string StingSfxPath = "Assets/Audio/SFX/Corpse Scare/Corpse_Wall_Sting.wav";
    const string HouseMusicPath = "Assets/Audio/Music/Ghost Story.mp3";

    [MenuItem("Tools/Environment/Setup Small House Crawl Scare")]
    public static void Setup()
    {
        EnsureFolders();
        ConfigureAbc();
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(AbcPath, ImportAssetOptions.ForceUpdate);
        ConfigureFbx();
        ConfigureAbc();

        AnimationClip clip = FindClip();
        if (clip == null)
        {
            Debug.LogError("CrawlScareSetup: no animation clip found on Crawl_Scare.fbx");
            return;
        }

        AnimatorController controller = EnsureController(clip);
        GameObject scare = EnsureScareInstance(controller, clip);
        InteractableItem battery = EnsureBatteryNearScare(scare.transform);
        WireHoldTrigger(battery, scare.GetComponent<CorpseCrawlScare>());

        Selection.activeGameObject = scare;
        EditorGUIUtility.PingObject(scare);
        Debug.Log("CrawlScareSetup: wired crawl scare in Room08. Hold E on the battery to trigger.");
    }

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Environment"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Environment");
    }

    static void ConfigureFbx()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
            return;

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        importer.resampleCurves = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.External;
        importer.SaveAndReimport();
    }

    static void ConfigureAbc()
    {
        var importer = AssetImporter.GetAtPath(AbcPath);
        if (importer == null)
            return;

        // Blender exports meters; Unity's Alembic default scaleFactor 0.01 (cm) shrinks the sheet to a speck.
        var so = new SerializedObject(importer);
        var scale = so.FindProperty("streamSettings.scaleFactor");
        var swap = so.FindProperty("streamSettings.swapHandedness");
        if (scale != null)
            scale.floatValue = 1f;
        if (swap != null)
            swap.boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();
    }

    static AnimationClip FindClip()
    {
        // Prefer the longest non-preview clip so a single baked 1–195 take wins over hold stubs.
        return AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview"))
            .OrderByDescending(c => c.length)
            .FirstOrDefault();
    }

    static AnimatorController EnsureController(AnimationClip clip)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null)
        {
            // Keep state motion pointed at the clip.
            foreach (var layer in existing.layers)
            {
                foreach (var child in layer.stateMachine.states)
                {
                    if (child.state.name == StateName)
                    {
                        child.state.motion = clip;
                        EditorUtility.SetDirty(existing);
                        AssetDatabase.SaveAssets();
                        return existing;
                    }
                }
            }
            return existing;
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var root = controller.layers[0].stateMachine;
        // Remove auto-generated default empty state if present.
        if (root.defaultState != null && root.defaultState.motion == null)
            root.RemoveState(root.defaultState);

        var crawlState = root.AddState(StateName);
        crawlState.motion = clip;
        root.defaultState = crawlState;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static GameObject EnsureScareInstance(AnimatorController controller, AnimationClip clip)
    {
        const string RootName = "Crawl Scare";
        var existing = GameObject.Find(RootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
            throw new System.InvalidOperationException("Missing " + FbxPath);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Crawl Scare");

        var body = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        if (body == null)
            body = Object.Instantiate(fbx);
        body.name = "Body";
        body.transform.SetParent(root.transform, false);

        var animator = body.GetComponentInChildren<Animator>();
        if (animator == null)
            animator = body.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.speed = 0f;

        GameObject sheetGo = null;
        var abcPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AbcPath);
        if (abcPrefab != null)
        {
            sheetGo = (GameObject)PrefabUtility.InstantiatePrefab(abcPrefab);
            if (sheetGo == null)
                sheetGo = Object.Instantiate(abcPrefab);
            sheetGo.name = "Sheet";
            sheetGo.transform.SetParent(root.transform, false);
            ApplySheetMaterial(sheetGo);
        }

        var ragdoll = root.GetComponent<CrawlScareRagdoll>();
        if (ragdoll == null)
            ragdoll = root.AddComponent<CrawlScareRagdoll>();
        ragdoll.SetBodyRoot(body.transform);
        ragdoll.Build();

        var scare = root.AddComponent<CorpseCrawlScare>();
        var so = new SerializedObject(scare);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("bodyRoot").objectReferenceValue = body;
        so.FindProperty("crawlState").stringValue = StateName;
        so.FindProperty("crawlClip").objectReferenceValue = clip;
        var sheetPlayer = sheetGo != null ? sheetGo.GetComponentInChildren<AlembicStreamPlayer>(true) : null;
        so.FindProperty("sheet").objectReferenceValue = sheetPlayer;
        so.FindProperty("sheetRoot").objectReferenceValue = sheetGo;
        so.FindProperty("ragdoll").objectReferenceValue = ragdoll;
        so.FindProperty("clipFps").floatValue = 30f;
        so.FindProperty("idleFrame").intValue = 29;
        so.FindProperty("speed").floatValue = 1f;
        so.FindProperty("startClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>(StartSfxPath);
        so.FindProperty("startClipVolume").floatValue = 1f;
        so.FindProperty("stingClip").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>(StingSfxPath);
        so.FindProperty("stingClipVolume").floatValue = 1f;
        so.FindProperty("houseMusic").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<AudioClip>(HouseMusicPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        PlaceInRoom08(root.transform, body.transform, sheetGo != null ? sheetGo.transform : null);

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        return root;
    }

    static void PlaceInRoom08(Transform root, Transform body, Transform sheet)
    {
        // Room08 is the largest room in the active small House.
        // Battery sits toward the hallway; crawler faces the battery.
        Transform room08 = FindRoom08();
        if (room08 != null)
            root.SetParent(room08, true);

        Vector3 crawlerPos = new Vector3(78.6f, 0.45f, 44.4f);
        Vector3 facePoint = new Vector3(75.4f, 0.45f, 42.5f);
        root.position = crawlerPos;

        Vector3 flat = facePoint - crawlerPos;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f)
            root.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);

        // Snap soles to floor using renderer bounds (prefer body mesh over sheet).
        Renderer[] rends = body != null
            ? body.GetComponentsInChildren<Renderer>(true)
            : root.GetComponentsInChildren<Renderer>(true);
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++)
                b.Encapsulate(rends[i].bounds);
            float dy = crawlerPos.y - b.min.y;
            root.position += Vector3.up * dy;
        }
    }

    static void ApplySheetMaterial(GameObject sheetGo)
    {
        const string MatPath = Folder + "/Materials/Crawl_Sheet.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            if (!AssetDatabase.IsValidFolder(Folder + "/Materials"))
                AssetDatabase.CreateFolder(Folder, "Materials");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            mat = new Material(shader != null ? shader : Shader.Find("Standard"));
            mat.name = "Crawl_Sheet";
            mat.color = new Color(0.85f, 0.84f, 0.80f, 1f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(mat, MatPath);
        }

        foreach (var renderer in sheetGo.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = mat;
    }

    static Transform FindRoom08()
    {
        // Prefer the Room08 under the active House near the expected small-house origin.
        var rooms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform best = null;
        float bestDist = float.MaxValue;
        Vector3 houseHint = new Vector3(75.87f, 0f, 39.16f);
        foreach (var t in rooms)
        {
            if (t == null || t.name != "Room08")
                continue;
            float d = Vector3.Distance(new Vector3(t.position.x, 0f, t.position.z), houseHint);
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        return best;
    }

    static InteractableItem EnsureBatteryNearScare(Transform scare)
    {
        const string PrefabBattery = "Assets/Prefabs/Items/Car parts/CarBattery.prefab";
        var batteryPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabBattery);
        Vector3 batteryPos = new Vector3(75.4f, 0.55f, 42.5f);

        InteractableItem item = null;
        var existing = Object.FindObjectsByType<InteractableItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var it in existing)
        {
            if (it == null || it.itemData == null)
                continue;
            if (it.gameObject.name == "CarBattery_SmallHouse")
            {
                item = it;
                break;
            }
        }

        // Remove stacked duplicate batteries at this spot (raycast was hitting a non-hold CarBattery).
        foreach (var it in existing)
        {
            if (it == null || it == item)
                continue;
            bool isBattery = it.gameObject.name.IndexOf("Battery", System.StringComparison.OrdinalIgnoreCase) >= 0
                || (it.itemData != null && it.itemData.name.IndexOf("Battery", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (!isBattery)
                continue;
            if (Vector3.Distance(it.transform.position, batteryPos) < 1.25f)
                Undo.DestroyObjectImmediate(it.gameObject);
        }

        if (item == null && batteryPrefab != null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(batteryPrefab);
            go.name = "CarBattery_SmallHouse";
            Undo.RegisterCreatedObjectUndo(go, "Small House Battery");
            item = go.GetComponent<InteractableItem>();
        }

        if (item == null)
            return null;

        Transform room08 = FindRoom08();
        if (room08 != null)
            item.transform.SetParent(room08, true);

        // Between hallway approach and crawler.
        item.transform.position = batteryPos;
        item.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
        return item;
    }

    static void WireHoldTrigger(InteractableItem battery, CorpseCrawlScare scare)
    {
        if (battery == null || scare == null)
            return;

        var so = new SerializedObject(battery);
        so.FindProperty("requireHoldToPickup").boolValue = true;
        so.FindProperty("pickupHoldSeconds").floatValue = 1.25f;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Rebuild the UnityEvent listener to CorpseCrawlScare.Play.
        var eventProp = so.FindProperty("onPickedUp");
        if (eventProp != null)
        {
            // Clear persistent calls via SerializedObject then add through UnityEventTools.
            var onPickedField = typeof(InteractableItem).GetField("onPickedUp",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (onPickedField != null)
            {
                var evt = onPickedField.GetValue(battery) as UnityEvent;
                if (evt == null)
                {
                    evt = new UnityEvent();
                    onPickedField.SetValue(battery, evt);
                }

                while (evt.GetPersistentEventCount() > 0)
                    UnityEventTools.RemovePersistentListener(evt, 0);

                UnityEventTools.AddVoidPersistentListener(evt, scare.Play);
            }
        }

        EditorUtility.SetDirty(battery);
    }
}
