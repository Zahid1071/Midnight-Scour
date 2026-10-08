using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the lighter: model import settings, animator controller, first-person
/// prefab, item asset, and scene wiring (held item root under the camera + starting item).
/// Safe to run again; existing prefab / item asset are kept, only missing links are filled.
/// </summary>
public static class LighterItemSetup
{
    const string Folder = "Assets/First Person/Lighter";
    const string DrawFbx = Folder + "/Arms_Lighter_DrawOpen.fbx";
    const string StrikeFbx = Folder + "/Arms_Lighter_Strike.fbx";
    const string IdleFbx = Folder + "/Arms_Lighter_Idle.fbx";
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

        AnimationClip drawClip = LoadClip(DrawFbx, "Lighter_DrawOpen");
        AnimationClip idleClip = LoadClip(IdleFbx, "Lighter_Idle");
        if (drawClip == null || idleClip == null)
        {
            Debug.LogError("[LighterItemSetup] Lighter_DrawOpen / Lighter_Idle clip not found in " + Folder);
            return;
        }

        AnimatorController controller = BuildController(drawClip, idleClip);
        GameObject prefab = BuildPrefab(controller, avatar);
        InventoryItem item = BuildItem(prefab);
        WireScene(item);

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

    static AnimatorController BuildController(AnimationClip drawClip, AnimationClip idleClip)
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
        EditorUtility.SetDirty(controller);
        return controller;
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

    static InventoryItem BuildItem(GameObject prefab)
    {
        var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<InventoryItem>();
            item.itemName = "Lighter";
            item.canDrop = false;
            AssetDatabase.CreateAsset(item, ItemPath);
        }

        if (item.heldViewPrefab == null)
        {
            item.heldViewPrefab = prefab;
            EditorUtility.SetDirty(item);
        }
        return item;
    }

    static void WireScene(InventoryItem item)
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

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
