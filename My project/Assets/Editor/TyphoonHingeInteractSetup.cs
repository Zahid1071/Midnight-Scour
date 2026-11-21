using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires Hood / Door_L / Door_R with <see cref="CarHingePanel"/> pivots + interact colliders
/// on the current flat Typhoon hierarchy, and gates part billboards on the hood.
/// Menu: Midnight Scour / Car / Setup Hood+Door Hinges And Billboard Gate
/// </summary>
public static class TyphoonHingeInteractSetup
{
    const string CarPath = "_Environment/GMC Typhoon";

    [MenuItem("Midnight Scour/Car/Setup Hood+Door Hinges And Billboard Gate")]
    public static void Run()
    {
        var car = GameObject.Find(CarPath);
        if (car == null)
        {
            Debug.LogError("[TyphoonHinges] Missing " + CarPath);
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(car, "Typhoon Hood/Door Hinges");

        // Unity left-handed axes on car-aligned pivots:
        // Hood: negative local X lifts the nose-side of the hood up.
        // Door_L (driver/+X side): positive local Y swings outward.
        // Door_R: negative local Y swings outward.
        var hoodPanel = WirePart(car.transform, "Hood", CarHingePanel.PanelKind.Hood,
            hingeAlong: -car.transform.forward, hingeAlongExtent: 0.9f, hingeHeightFrac: 0f,
            closedEuler: Vector3.zero, openEuler: new Vector3(-65f, 0f, 0f),
            minBox: new Vector3(1.2f, 0.2f, 1.0f));

        WirePart(car.transform, "Door_L", CarHingePanel.PanelKind.Door,
            hingeAlong: car.transform.forward, hingeAlongExtent: 0.85f, hingeHeightFrac: 0.55f,
            closedEuler: Vector3.zero, openEuler: new Vector3(0f, 75f, 0f),
            minBox: new Vector3(0.2f, 1.0f, 1.0f));

        WirePart(car.transform, "Door_R", CarHingePanel.PanelKind.Door,
            hingeAlong: car.transform.forward, hingeAlongExtent: 0.85f, hingeHeightFrac: 0.55f,
            closedEuler: Vector3.zero, openEuler: new Vector3(0f, -75f, 0f),
            minBox: new Vector3(0.2f, 1.0f, 1.0f));

        int gated = 0;
        foreach (var bb in car.GetComponentsInChildren<CarPartBillboard>(true))
        {
            var so = new SerializedObject(bb);
            var prop = so.FindProperty("requireOpenHood");
            if (prop != null)
            {
                prop.objectReferenceValue = hoodPanel;
                so.ApplyModifiedPropertiesWithoutUndo();
                gated++;
            }
            else
            {
                Debug.LogWarning("[TyphoonHinges] CarPartBillboard missing requireOpenHood on " + bb.name);
            }
        }

        EditorUtility.SetDirty(car);
        EditorSceneManager.MarkSceneDirty(car.scene);
        Debug.Log($"[TyphoonHinges] Hood/doors wired. Billboards gated on hood={gated}.");
    }

    static CarHingePanel WirePart(
        Transform car,
        string partName,
        CarHingePanel.PanelKind kind,
        Vector3 hingeAlong,
        float hingeAlongExtent,
        float hingeHeightFrac,
        Vector3 closedEuler,
        Vector3 openEuler,
        Vector3 minBox)
    {
        var part = car.Find(partName);
        if (part == null)
        {
            Debug.LogError("[TyphoonHinges] Missing " + partName);
            return null;
        }

        // Already pivoted?
        Transform pivot;
        if (part.parent != null && part.parent.name == partName + "_Hinge")
        {
            pivot = part.parent;
        }
        else
        {
            if (!TryBounds(part, out var b))
            {
                Debug.LogError("[TyphoonHinges] No renderer bounds on " + partName);
                return null;
            }

            Vector3 hingePos = b.center + hingeAlong.normalized * (GetExtentAlong(b, hingeAlong) * hingeAlongExtent);
            if (hingeHeightFrac <= 0f)
                hingePos.y = b.min.y + 0.02f;
            else
                hingePos.y = b.min.y + b.size.y * hingeHeightFrac;

            var pivotGo = new GameObject(partName + "_Hinge");
            Undo.RegisterCreatedObjectUndo(pivotGo, "Create " + pivotGo.name);
            pivotGo.transform.SetParent(car, false);
            pivotGo.transform.position = hingePos;
            pivotGo.transform.rotation = car.rotation;
            pivotGo.transform.SetSiblingIndex(part.GetSiblingIndex());
            Undo.SetTransformParent(part, pivotGo.transform, "Parent " + partName);
            pivot = pivotGo.transform;
        }

        var panel = pivot.GetComponent<CarHingePanel>();
        if (panel == null)
            panel = Undo.AddComponent<CarHingePanel>(pivot.gameObject);
        panel.EditorConfigure(pivot, closedEuler, openEuler, kind);
        pivot.localRotation = Quaternion.Euler(closedEuler);

        var box = pivot.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(pivot.gameObject);

        if (TryBounds(part, out var pb))
        {
            Vector3 localCenter = pivot.InverseTransformPoint(pb.center);
            Vector3 localSize = pivot.InverseTransformVector(pb.size);
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
            box.center = localCenter;
            box.size = Vector3.Max(localSize, minBox);
        }
        else
        {
            box.center = Vector3.zero;
            box.size = minBox;
        }
        box.isTrigger = false;

        int layer = LayerMask.NameToLayer("Interactable_Item");
        if (layer >= 0)
            pivot.gameObject.layer = layer;

        EditorUtility.SetDirty(panel);
        return panel;
    }

    static float GetExtentAlong(Bounds b, Vector3 dir)
    {
        dir = dir.normalized;
        // Project extents onto direction using AABB extents.
        return Mathf.Abs(dir.x) * b.extents.x
             + Mathf.Abs(dir.y) * b.extents.y
             + Mathf.Abs(dir.z) * b.extents.z;
    }

    static bool TryBounds(Transform root, out Bounds bounds)
    {
        var rends = root.GetComponentsInChildren<Renderer>(true);
        if (rends == null || rends.Length == 0)
        {
            bounds = default;
            return false;
        }
        bounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            bounds.Encapsulate(rends[i].bounds);
        return true;
    }
}
