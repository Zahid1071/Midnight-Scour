using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a low-poly, editor-only copy of the terrain that stands in for the ground during light baking.
/// The real terrain is 2049x2049 and runs the bake out of memory when it contributes GI, but without ground
/// the outdoor probes see the sky from below and dynamic objects glow.
/// </summary>
public static class BakeGroundProxy
{
    const string ObjectName = "Bake Ground Proxy";
    const string MeshPath = "Assets/Lighting/BakeGroundProxy.asset";
    const string MaterialPath = "Assets/Lighting/BakeGroundProxy.mat";
    const float CellSize = 2f;
    const float SinkDepth = 0.25f;

    // The basement's existing bake has no roof over it; keep it that way so its lighting doesn't change.
    static readonly Rect UndergroundXZ = Rect.MinMaxRect(-14f, -23f, 17f, 25.5f);

    [MenuItem("Tools/Lighting/Rebuild Bake Ground Proxy")]
    static void RebuildMenu() => Debug.Log(Rebuild());

    public static string Rebuild()
    {
        var terrain = Terrain.activeTerrain;
        if (!terrain) return "No active terrain.";

        var mesh = BuildMesh(terrain, out int quads);
        var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existingMesh)
        {
            existingMesh.Clear();
            EditorUtility.CopySerialized(mesh, existingMesh);
            Object.DestroyImmediate(mesh);
            mesh = existingMesh;
        }
        else
        {
            EnsureFolder("Assets/Lighting");
            AssetDatabase.CreateAsset(mesh, MeshPath);
        }

        var go = GameObject.Find(ObjectName) ?? new GameObject(ObjectName);
        go.tag = "EditorOnly";
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var parent = GameObject.Find("Probe Volumes");
        if (parent) go.transform.SetParent(parent.transform, true);

        if (!go.TryGetComponent<MeshFilter>(out var filter)) filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        if (!go.TryGetComponent<MeshRenderer>(out var renderer)) renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = false;
        renderer.receiveGI = ReceiveGI.LightProbes;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(go.scene);
        return $"Bake ground proxy: {quads} quads, {mesh.vertexCount} vertices.";
    }

    static Mesh BuildMesh(Terrain terrain, out int quads)
    {
        var data = terrain.terrainData;
        var origin = terrain.transform.position;
        int cells = Mathf.CeilToInt(data.size.x / CellSize);
        float step = data.size.x / cells;
        var holes = data.GetHoles(0, 0, data.holesResolution, data.holesResolution);

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var vertexIndex = new int[cells + 1, cells + 1];
        for (int z = 0; z <= cells; z++)
        for (int x = 0; x <= cells; x++)
            vertexIndex[z, x] = -1;

        quads = 0;
        for (int z = 0; z < cells; z++)
        for (int x = 0; x < cells; x++)
        {
            var min = new Vector2(origin.x + x * step, origin.z + z * step);
            var cell = new Rect(min, new Vector2(step, step));
            if (cell.Overlaps(UndergroundXZ) || HasHole(holes, data, x * step, z * step, step)) continue;

            int v00 = Vertex(x, z), v10 = Vertex(x + 1, z), v01 = Vertex(x, z + 1), v11 = Vertex(x + 1, z + 1);
            indices.Add(v00); indices.Add(v01); indices.Add(v11);
            indices.Add(v00); indices.Add(v11); indices.Add(v10);
            quads++;
        }

        var mesh = new Mesh { name = "BakeGroundProxy", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        // The light baker rejects meshes without UVs, even ones lit only by probes.
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;

        int Vertex(int x, int z)
        {
            if (vertexIndex[z, x] >= 0) return vertexIndex[z, x];
            var world = new Vector3(origin.x + x * step, 0f, origin.z + z * step);
            world.y = origin.y + terrain.SampleHeight(world) - SinkDepth;
            vertexIndex[z, x] = vertices.Count;
            vertices.Add(world);
            uvs.Add(new Vector2((float)x / cells, (float)z / cells));
            return vertexIndex[z, x];
        }
    }

    static bool HasHole(bool[,] holes, TerrainData data, float localX, float localZ, float size)
    {
        int res = data.holesResolution;
        int x0 = Mathf.FloorToInt(localX / data.size.x * res), x1 = Mathf.CeilToInt((localX + size) / data.size.x * res);
        int z0 = Mathf.FloorToInt(localZ / data.size.z * res), z1 = Mathf.CeilToInt((localZ + size) / data.size.z * res);
        for (int z = Mathf.Max(z0, 0); z < Mathf.Min(z1, res); z++)
        for (int x = Mathf.Max(x0, 0); x < Mathf.Min(x1, res); x++)
            if (!holes[z, x]) return true;
        return false;
    }

    static Material GetMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BakeGroundProxy" };
        // Roughly the terrain's average albedo, so outdoor bounce light keeps a plausible tint.
        material.SetColor("_BaseColor", new Color(0.2f, 0.19f, 0.14f));
        material.SetFloat("_Smoothness", 0f);
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'), System.IO.Path.GetFileName(path));
    }
}
