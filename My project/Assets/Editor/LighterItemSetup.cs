using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the lighter: model import settings, animator controller, first-person
/// prefab, item asset, and scene wiring (held item root under the camera with the arms placed in it,
/// starting item). Safe to run again; existing prefab / item asset / placed arms are kept, only
/// missing links are filled.
/// </summary>
public static class LighterItemSetup
{
    const string Folder = "Assets/First Person/Lighter";
    const string DrawFbx = Folder + "/Arms_Lighter_DrawOpen.fbx";
    const string StrikeFbx = Folder + "/Arms_Lighter_Strike.fbx";
    const string IdleFbx = Folder + "/Arms_Lighter_Idle.fbx";
    const string IdleClosedFbx = Folder + "/Arms_Lighter_IdleClosed.fbx";
    const string ControllerPath = Folder + "/Lighter_FP.controller";
    const string PrefabPath = Folder + "/Lighter_FP.prefab";
    const string ItemPath = "Assets/Scriptable Objects/Item Scriptable Objects/Lighter.asset";
    const string HeldRootName = "Held Item Root";

    [MenuItem("Tools/Items/Set Up Lighter Item")]
    static void Run()
    {
        ConfigureModel(DrawFbx, null, false);
        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(DrawFbx).OfType<Avatar>().FirstOrDefault();
        ConfigureModel(StrikeFbx, avatar, false);
        ConfigureModel(IdleFbx, avatar, true);
        ConfigureModel(IdleClosedFbx, avatar, true);

        AnimationClip drawClip = LoadClip(DrawFbx, "Lighter_DrawOpen");
        AnimationClip idleClip = LoadClip(IdleFbx, "Lighter_Idle");
        AnimationClip idleClosedClip = LoadClip(IdleClosedFbx, "Lighter_IdleClosed");
        AnimationClip strikeClip = LoadClip(StrikeFbx, "Lighter_Strike");
        if (drawClip == null || idleClip == null || idleClosedClip == null || strikeClip == null)
        {
            Debug.LogError("[LighterItemSetup] Lighter_DrawOpen / Lighter_Idle / Lighter_IdleClosed / Lighter_Strike clip not found in " + Folder);
            return;
        }

        AnimatorController controller = BuildController(drawClip, idleClip, idleClosedClip, strikeClip);
        GameObject prefab = BuildPrefab(controller, avatar);
        InventoryItem item = BuildItem();
        WireScene(item, prefab);

        AssetDatabase.SaveAssets();
        Selection.activeObject = item;
        Debug.Log("[LighterItemSetup] Done. Save the scene to keep the scene changes.");
    }

    static void ConfigureModel(string path, Avatar sourceAvatar, bool loop)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError("[LighterItemSetup] Missing model " + path);
            return;
        }

        importer.importCameras = false;
        importer.importLights = false;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = sourceAvatar == null
            ? ModelImporterAvatarSetup.CreateFromThisModel
            : ModelImporterAvatarSetup.CopyFromOther;
        if (sourceAvatar != null)
            importer.sourceAvatar = sourceAvatar;
        importer.importAnimation = true;
        // Fingertip contact on the lid/wheel is exact in the source; keyframe reduction drifts it.
        importer.animationCompression = ModelImporterAnimationCompression.Off;

        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            int bar = clip.takeName.LastIndexOf('|');
            clip.name = bar >= 0 ? clip.takeName.Substring(bar + 1) : clip.takeName;
            clip.loopTime = loop;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimationClip LoadClip(string path, string name) =>
        AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name);

    static AnimatorController BuildController(AnimationClip drawClip, AnimationClip idleClip,
                                              AnimationClip idleClosedClip, AnimationClip strikeClip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                         ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var draw = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Draw")
                   ?? machine.AddState("Draw");
        draw.motion = drawClip;
        machine.defaultState = draw;

        var idle = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle")
                   ?? machine.AddState("Idle");
        idle.motion = idleClip;
        // Draw ends on the exact first pose of Idle, so a zero-length hand-off is seamless.
        var toIdle = draw.transitions.FirstOrDefault(t => t.destinationState == idle) ?? draw.AddTransition(idle);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 1f;
        toIdle.hasFixedDuration = true;
        toIdle.duration = 0f;
        toIdle.offset = 0f;

        // HeldLighter cross-fades into Strike / LidClose / LidOpen / PutDown itself; only the hand-offs
        // back to Idle live here.
        var strike = GetOrAddState(machine, "Strike", strikeClip, 2f);
        // Strike ends on Idle's first pose.
        SetExitTransition(strike, idle, 1f, 0f);

        // The draw clip's lid section (frames 20-68) forward / reversed, and the whole draw reversed.
        // The closed-lid idle starts on draw frame 20.
        AnimationClip reversed = BuildReversedClip(drawClip);
        var lidOpen = GetOrAddState(machine, "LidOpen", drawClip, 1.5f);
        // Thumb is back at rest at frame 58 of 68; the rest of the clip is still.
        SetExitTransition(lidOpen, idle, 58f / 68f, 0.1f);
        GetOrAddState(machine, "LidClose", reversed, 1.5f);
        GetOrAddState(machine, "LidClosed", idleClosedClip, 1f);
        GetOrAddState(machine, "PutDown", reversed, 2f);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>Writes the clip played backwards (keys mirrored in time, tangents swapped) next to the controller.</summary>
    static AnimationClip BuildReversedClip(AnimationClip source)
    {
        string path = Folder + "/" + source.name + "_Reversed.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }
        clip.ClearCurves();
        clip.frameRate = source.frameRate;

        float length = source.length;
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var keys = AnimationUtility.GetEditorCurve(source, binding).keys;
            var flipped = new Keyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe k = keys[keys.Length - 1 - i];
                WeightedMode mode = k.weightedMode == WeightedMode.In ? WeightedMode.Out
                    : k.weightedMode == WeightedMode.Out ? WeightedMode.In
                    : k.weightedMode;
                flipped[i] = new Keyframe(length - k.time, k.value, -k.outTangent, -k.inTangent,
                                          k.outWeight, k.inWeight) { weightedMode = mode };
            }
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(flipped));
        }
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorState GetOrAddState(AnimatorStateMachine machine, string name, Motion motion, float speed)
    {
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name)
                    ?? machine.AddState(name);
        state.motion = motion;
        state.speed = speed;
        return state;
    }

    static void SetExitTransition(AnimatorState from, AnimatorState to, float exitTime, float duration)
    {
        var t = from.transitions.FirstOrDefault(x => x.destinationState == to) ?? from.AddTransition(to);
        t.hasExitTime = true;
        t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration;
    }

    static GameObject BuildPrefab(AnimatorController controller, Avatar avatar)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null)
            return existing;

        var root = new GameObject("Lighter_FP");
        var view = root.AddComponent<HeldItemView>();
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DrawFbx), root.transform);
        model.name = "Arms";

        var animator = model.GetComponent<Animator>();
        if (animator == null)
            animator = model.AddComponent<Animator>();
        if (animator.avatar == null)
            animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var so = new SerializedObject(view);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static InventoryItem BuildItem()
    {
        var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = "Lighter";
            item.canDrop = false;
            AssetDatabase.CreateAsset(item, ItemPath);
        }
        return item;
    }

    static void PlaceView(Transform holder, GameObject prefab, InventoryItem item)
    {
        foreach (var existing in holder.GetComponentsInChildren<HeldItemView>(true))
        {
            if (existing.Item == item)
                return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
        Undo.RegisterCreatedObjectUndo(go, "Place Lighter View");

        var view = go.GetComponent<HeldItemView>();
        var so = new SerializedObject(view);
        so.FindProperty("item").objectReferenceValue = item;
        so.ApplyModifiedProperties();

        var lighter = go.AddComponent<HeldLighter>();
        var lighterSo = new SerializedObject(lighter);
        lighterSo.FindProperty("animator").objectReferenceValue = view.Animator;
        lighterSo.ApplyModifiedProperties();
    }

    static void WireScene(InventoryItem item, GameObject prefab)
    {
        var camGo = GameObject.FindWithTag("MainCamera");
        if (camGo == null)
            Debug.LogWarning("[LighterItemSetup] No MainCamera in the open scene; add a HeldItemController under the gameplay camera manually.");
        else
        {
            Transform holder = camGo.transform.Find(HeldRootName);
            if (holder == null)
            {
                var go = new GameObject(HeldRootName);
                Undo.RegisterCreatedObjectUndo(go, "Create Held Item Root");
                go.transform.SetParent(camGo.transform, false);
                holder = go.transform;
            }
            if (holder.GetComponent<HeldItemController>() == null)
                Undo.AddComponent<HeldItemController>(holder.gameObject);
            PlaceView(holder, prefab, item);
        }

        var inventory = Object.FindFirstObjectByType<InventoryManager>(FindObjectsInactive.Include);
        if (inventory == null)
            Debug.LogWarning("[LighterItemSetup] No InventoryManager in the open scene; add PlayerStartingItems manually.");
        else
        {
            var starting = inventory.GetComponent<PlayerStartingItems>();
            if (starting == null)
                starting = Undo.AddComponent<PlayerStartingItems>(inventory.gameObject);
            var so = new SerializedObject(starting);
            var items = so.FindProperty("items");
            bool present = false;
            for (int i = 0; i < items.arraySize; i++)
                present |= items.GetArrayElementAtIndex(i).objectReferenceValue == item;
            if (!present)
            {
                items.InsertArrayElementAtIndex(0);
                items.GetArrayElementAtIndex(0).objectReferenceValue = item;
            }
            so.ApplyModifiedProperties();
        }

        foreach (var flame in Object.FindObjectsByType<LighterFlame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (flame.GetComponent<FollowHeldItemSocket>() == null)
                Undo.AddComponent<FollowHeldItemSocket>(flame.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
