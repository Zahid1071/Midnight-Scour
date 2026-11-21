using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot: strip GMC Typhoon Visual to body/hood/doors/tires and wire hinge + hood HUD.
/// Menu: Midnight Scour / Car / Make Typhoon Game-Ready
/// </summary>
public static class TyphoonGameReadySetup
{
    const string CarPath = "_Environment/GMC Typhoon";

    static readonly string[] DropNeedles =
    {
        "engine", "enginebay", "dash", "seat", "floor", "trunkliner", "trunkbay",
        "centerconsole", "centerstack", "steeringwheel", "wiper", "controlarm",
        "caliper", "rotor", "pillartrim", "doorsill", "speaker", "gauge",
        "seatbelt", "undercarriage", "exhaust"
    };

    static readonly string[] KeepNeedles =
    {
        "body", "fender", "bumper", "grille", "hood", "door", "glass", "tyre", "tire",
        "rim_", "wingmirror", "headlight", "taillight", "skirt", "trunk_a",
        "glassf", "glasslr", "glassrr", "glasslhl", "glassrhl", "glassltl", "glassrtl", "glassbump"
    };

    [MenuItem("Midnight Scour/Car/Make Typhoon Game-Ready")]
    public static void Run()
    {
        var car = GameObject.Find(CarPath);
        if (car == null)
        {
            Debug.LogError("[TyphoonGameReady] Missing " + CarPath);
            return;
        }

        var visual = car.transform.Find("Visual");
        if (visual == null)
        {
            Debug.LogError("[TyphoonGameReady] Missing Visual child");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(car, "Typhoon Game-Ready");

        // GLB imports as a prefab instance — must unpack before reparenting parts.
        if (PrefabUtility.IsPartOfPrefabInstance(visual.gameObject))
        {
            PrefabUtility.UnpackPrefabInstance(
                PrefabUtility.GetOutermostPrefabInstanceRoot(visual.gameObject),
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            visual = car.transform.Find("Visual");
            if (visual == null)
            {
                // After unpack the root may be renamed; find first child with many mesh filters
                foreach (Transform c in car.transform)
                {
                    if (c.GetComponentsInChildren<MeshFilter>(true).Length > 10)
                    {
                        visual = c;
                        visual.name = "Visual";
                        break;
                    }
                }
            }
        }
        if (visual == null)
        {
            Debug.LogError("[TyphoonGameReady] Visual missing after unpack");
            return;
        }

        int dropped = Prune(visual);
        BuildFoldersAndParent(visual, car.transform, out Transform body, out Transform hood,
            out Transform doorL, out Transform doorR, out Transform wheels, out Transform trunk);

        // Hinge pivots: hood at rear of hood bounds; doors at front edge of each door bounds
        var hoodHinge = EnsurePivot(hood, "HoodHinge");
        PlaceHoodHinge(hood, hoodHinge);
        ReparentMeshesUnder(hood, hoodHinge);

        var doorLHinge = EnsurePivot(doorL, "DoorL_Hinge");
        PlaceDoorHinge(doorL, doorLHinge, left: true, car.transform);
        ReparentMeshesUnder(doorL, doorLHinge);

        var doorRHinge = EnsurePivot(doorR, "DoorR_Hinge");
        PlaceDoorHinge(doorR, doorRHinge, left: false, car.transform);
        ReparentMeshesUnder(doorR, doorRHinge);

        // Asset rest pose is already OPEN. open = identity, closed = swing shut.
        WireHinge(hoodHinge.gameObject, hoodHinge, CarHingePanel.PanelKind.Hood,
            closedEuler: new Vector3(70f, 0f, 0f), openEuler: Vector3.zero);
        hoodHinge.localRotation = Quaternion.Euler(70f, 0f, 0f);

        WireHinge(doorLHinge.gameObject, doorLHinge, CarHingePanel.PanelKind.Door,
            closedEuler: new Vector3(0f, 75f, 0f), openEuler: Vector3.zero);
        doorLHinge.localRotation = Quaternion.Euler(0f, 75f, 0f);

        WireHinge(doorRHinge.gameObject, doorRHinge, CarHingePanel.PanelKind.Door,
            closedEuler: new Vector3(0f, -75f, 0f), openEuler: Vector3.zero);
        doorRHinge.localRotation = Quaternion.Euler(0f, -75f, 0f);

        // Interact colliders on hinges
        AddInteractBox(hoodHinge, hood, new Vector3(1.2f, 0.15f, 1.0f));
        AddInteractBox(doorLHinge, doorL, new Vector3(0.15f, 1.0f, 1.0f));
        AddInteractBox(doorRHinge, doorR, new Vector3(0.15f, 1.0f, 1.0f));

        // Body collider (one) — remove hundreds of tiny mesh colliders on kept meshes first? keep mesh colliders on body only
        SimplifyColliders(visual, body, wheels);

        var hud = car.GetComponent<CarHoodPartsHud>();
        if (hud == null)
            hud = Undo.AddComponent<CarHoodPartsHud>(car);

        var so = new SerializedObject(hud);
        so.FindProperty("hood").objectReferenceValue = hoodHinge.GetComponent<CarHingePanel>();
        var slots = car.GetComponentsInChildren<CarPartSlot>(true);
        var slotsProp = so.FindProperty("slots");
        slotsProp.arraySize = slots.Length;
        for (int i = 0; i < slots.Length; i++)
            slotsProp.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // Turn repair back on so missing-parts / install loop works
        var escape = car.GetComponentInChildren<CarEscapePoint>(true);
        if (escape != null)
        {
            var eso = new SerializedObject(escape);
            var disabled = eso.FindProperty("repairDisabledForDemo");
            if (disabled != null)
            {
                disabled.boolValue = false;
                eso.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        EditorUtility.SetDirty(car);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Debug.Log($"[TyphoonGameReady] Done. Dropped {dropped} meshes. Hood/doors hinged + HUD wired. Body={CountMeshes(body)} Hood={CountMeshes(hoodHinge)} DoorL={CountMeshes(doorLHinge)} DoorR={CountMeshes(doorRHinge)} Wheels={CountMeshes(wheels)} Trunk={CountMeshes(trunk)}");
    }

    static int Prune(Transform visual)
    {
        var kill = new List<GameObject>();
        int dropped = 0;
        foreach (var mf in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf == null) continue;
            if (ShouldKeep(mf.name))
                continue;
            kill.Add(mf.gameObject);
            dropped++;
        }
        foreach (var go in kill)
            Undo.DestroyObjectImmediate(go);
        return dropped;
    }

    static bool ShouldKeep(string raw)
    {
        string n = raw.ToLowerInvariant();
        for (int i = 0; i < DropNeedles.Length; i++)
            if (n.Contains(DropNeedles[i]))
                return false;
        for (int i = 0; i < KeepNeedles.Length; i++)
            if (n.Contains(KeepNeedles[i]))
                return true;
        return false;
    }

    static void BuildFoldersAndParent(Transform visual, Transform car, out Transform body, out Transform hood,
        out Transform doorL, out Transform doorR, out Transform wheels, out Transform trunk)
    {
        body = EnsureChild(visual, "Body");
        hood = EnsureChild(visual, "Hood");
        doorL = EnsureChild(visual, "Door_L");
        doorR = EnsureChild(visual, "Door_R");
        wheels = EnsureChild(visual, "Wheels");
        trunk = EnsureChild(visual, "Trunk");

        foreach (var mf in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            var t = mf.transform;
            if (t == visual) continue;
            if (t == body || t == hood || t == doorL || t == doorR || t == wheels || t == trunk) continue;
            if (t.parent != null && (t.parent.name.Contains("Hinge") || t.parent == body || t.parent == hood ||
                                     t.parent == doorL || t.parent == doorR || t.parent == wheels || t.parent == trunk))
            {
                // already sorted under a folder/hinge
                if (t.parent == body || t.parent == hood || t.parent == doorL || t.parent == doorR ||
                    t.parent == wheels || t.parent == trunk)
                { }
                else
                    continue;
            }

            string n = t.name.ToLowerInvariant();
            if (n.Contains("hood"))
            {
                t.SetParent(hood, true);
                continue;
            }
            if (n.Contains("trunk_a") || (n.Contains("trunk") && !n.Contains("liner") && !n.Contains("bay")))
            {
                t.SetParent(trunk, true);
                continue;
            }
            if (n.Contains("tyre_") || n.Contains("rim_"))
            {
                t.SetParent(wheels, true);
                continue;
            }

            bool doorish = n.Contains("door") || n.Contains("glasslf") || n.Contains("glassrf") ||
                           n.Contains("wingmirror") || n.Contains("speakerdoor");
            if (doorish)
            {
                // This asset mis-tags RF parts as LF — side by world X vs car center.
                var r = t.GetComponent<Renderer>();
                bool left = true;
                if (r != null)
                    left = Vector3.Dot(r.bounds.center - car.position, car.right) < 0f;
                else if (n.Contains("rf") || n.Contains("wingmirrorr") || n.Contains("glassrf") || n.Contains("mirror_right"))
                    left = false;
                t.SetParent(left ? doorL : doorR, true);
                continue;
            }

            t.SetParent(body, true);
        }
    }

    static Transform EnsureChild(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (t != null) return t;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    static Transform EnsurePivot(Transform parent, string name)
    {
        var existing = parent.Find(name);
        if (existing != null) return existing;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = LayerMask.NameToLayer("Interactable_Item");
        if (go.layer < 0) go.layer = parent.gameObject.layer;
        return go.transform;
    }

    static void ReparentMeshesUnder(Transform folder, Transform hinge)
    {
        var kids = new List<Transform>();
        for (int i = 0; i < folder.childCount; i++)
        {
            var c = folder.GetChild(i);
            if (c == hinge) continue;
            if (c.GetComponent<MeshFilter>() != null)
                kids.Add(c);
        }
        foreach (var c in kids)
            c.SetParent(hinge, true);
    }

    static bool TryBounds(Transform root, out Bounds bounds)
    {
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            bounds = new Bounds(root.position, Vector3.one);
            return false;
        }
        bounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            bounds.Encapsulate(rends[i].bounds);
        return true;
    }

    static void PlaceHoodHinge(Transform hoodFolder, Transform hinge)
    {
        if (!TryBounds(hoodFolder, out var b))
        {
            hinge.position = hoodFolder.position;
            return;
        }
        // Hinge near the windshield edge: back of hood (toward car rear along -forward of car)
        var car = hoodFolder.root; // not reliable
        Transform carT = hoodFolder;
        while (carT.parent != null && carT.parent.name != "_Environment")
            carT = carT.parent;
        Vector3 back = -carT.forward;
        // Hood rear edge ≈ bounds center pulled toward rear
        Vector3 pos = b.center;
        pos += back * (b.extents.z * 0.85f);
        pos.y = b.min.y + 0.05f;
        hinge.position = pos;
        hinge.rotation = carT.rotation;
    }

    static void PlaceDoorHinge(Transform doorFolder, Transform hinge, bool left, Transform car)
    {
        if (!TryBounds(doorFolder, out var b))
        {
            hinge.position = doorFolder.position;
            hinge.rotation = car.rotation;
            return;
        }
        // Hinge at front edge of door, mid height
        Vector3 pos = b.center;
        pos += car.forward * (b.extents.z * 0.8f);
        pos.y = b.min.y + b.size.y * 0.55f;
        // Nudge toward car body on X
        pos += (left ? car.right : -car.right) * 0.05f;
        hinge.position = pos;
        hinge.rotation = car.rotation;
    }

    static void WireHinge(GameObject host, Transform hinge, CarHingePanel.PanelKind kind, Vector3 closedEuler, Vector3 openEuler)
    {
        var panel = host.GetComponent<CarHingePanel>();
        if (panel == null)
            panel = Undo.AddComponent<CarHingePanel>(host);
        panel.EditorConfigure(hinge, closedEuler, openEuler, kind);
        EditorUtility.SetDirty(panel);
    }

    static void AddInteractBox(Transform hinge, Transform meshRoot, Vector3 localSize)
    {
        var box = hinge.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(hinge.gameObject);
        if (TryBounds(meshRoot, out var b))
        {
            // Convert world bounds center to hinge local
            Vector3 localCenter = hinge.InverseTransformPoint(b.center);
            Vector3 localSizeApprox = hinge.InverseTransformVector(b.size);
            localSizeApprox = new Vector3(Mathf.Abs(localSizeApprox.x), Mathf.Abs(localSizeApprox.y), Mathf.Abs(localSizeApprox.z));
            box.center = localCenter;
            box.size = Vector3.Max(localSizeApprox, localSize);
        }
        else
        {
            box.center = Vector3.zero;
            box.size = localSize;
        }
        box.isTrigger = false;

        int layer = LayerMask.NameToLayer("Interactable_Item");
        if (layer >= 0)
            hinge.gameObject.layer = layer;
    }

    static void SimplifyColliders(Transform visual, Transform body, Transform wheels)
    {
        // Strip mesh colliders from everything, then add one on body + wheels
        foreach (var col in visual.GetComponentsInChildren<MeshCollider>(true))
            Undo.DestroyObjectImmediate(col);

        foreach (var mf in body.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
        }
        foreach (var mf in wheels.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
        }
    }

    static int CountMeshes(Transform t)
    {
        if (t == null) return 0;
        return t.GetComponentsInChildren<MeshFilter>(true).Length;
    }
}
