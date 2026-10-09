using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ghost of an item's world prefab (its meshes only, drawn with one preview material) showing where
/// hold-G will set the item down. The item rests on the surface along the surface normal, centered on the aim point.
/// </summary>
public sealed class PlacementPreview
{
    private sealed class Ghost
    {
        public GameObject Root;
        public Quaternion PrefabRotation;
        // Bounds of the meshes with the root at the origin, rotated by its prefab rotation.
        public Vector3 BottomCenter;
    }

    private readonly Material _material;
    private readonly Dictionary<GameObject, Ghost> _ghosts = new();
    private Ghost _shown;

    public bool IsVisible => _shown != null;
    public Vector3 Position { get; private set; }
    public Quaternion Rotation { get; private set; }

    public PlacementPreview(Material material)
    {
        _material = material;
    }

    public void Show(GameObject prefab, Vector3 surfacePoint, Vector3 surfaceNormal, float yaw)
    {
        Ghost ghost = GetGhost(prefab);
        if (_shown != ghost)
        {
            Hide();
            _shown = ghost;
            ghost.Root.SetActive(true);
        }

        Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, surfaceNormal) * Quaternion.Euler(0f, yaw, 0f);
        Rotation = surfaceRotation * ghost.PrefabRotation;
        Position = surfacePoint - surfaceRotation * ghost.BottomCenter + surfaceNormal * 0.002f;
        ghost.Root.transform.SetPositionAndRotation(Position, Rotation);
    }

    public void Hide()
    {
        if (_shown == null)
            return;
        if (_shown.Root != null)
            _shown.Root.SetActive(false);
        _shown = null;
    }

    public void Dispose()
    {
        foreach (Ghost ghost in _ghosts.Values)
        {
            if (ghost.Root != null)
                Object.Destroy(ghost.Root);
        }
        _ghosts.Clear();
        _shown = null;
    }

    private Ghost GetGhost(GameObject prefab)
    {
        if (_ghosts.TryGetValue(prefab, out Ghost ghost) && ghost.Root != null)
            return ghost;

        ghost = Build(prefab);
        _ghosts[prefab] = ghost;
        return ghost;
    }

    private Ghost Build(GameObject prefab)
    {
        var root = new GameObject(prefab.name + " (Place Preview)");
        root.SetActive(false);

        Quaternion prefabRotation = prefab.transform.rotation;
        Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
        Matrix4x4 oriented = Matrix4x4.Rotate(prefabRotation);
        Bounds bounds = default;
        bool hasBounds = false;

        foreach (Renderer source in prefab.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            if (source is SkinnedMeshRenderer skinned)
                mesh = skinned.sharedMesh;
            else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter))
                mesh = filter.sharedMesh;
            if (mesh == null)
                continue;

            Matrix4x4 local = toRoot * source.transform.localToWorldMatrix;
            var part = new GameObject(source.name);
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = Quaternion.LookRotation(local.GetColumn(2), local.GetColumn(1));
            part.transform.localScale = new Vector3(local.GetColumn(0).magnitude, local.GetColumn(1).magnitude, local.GetColumn(2).magnitude);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = part.AddComponent<MeshRenderer>();
            var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < materials.Length; i++)
                materials[i] = _material;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            EncapsulateMeshBounds(ref bounds, ref hasBounds, mesh.bounds, oriented * local);
        }

        return new Ghost
        {
            Root = root,
            PrefabRotation = prefabRotation,
            BottomCenter = hasBounds ? new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) : Vector3.zero
        };
    }

    private static void EncapsulateMeshBounds(ref Bounds bounds, ref bool hasBounds, Bounds meshBounds, Matrix4x4 matrix)
    {
        Vector3 min = meshBounds.min;
        Vector3 max = meshBounds.max;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            Vector3 p = matrix.MultiplyPoint3x4(corner);
            if (!hasBounds)
            {
                bounds = new Bounds(p, Vector3.zero);
                hasBounds = true;
            }
            else
                bounds.Encapsulate(p);
        }
    }
}
