#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Hole cell size = terrain width / (heightmapResolution - 1).
/// Raises the heightmap resolution while resampling existing heights and holes so nothing is lost.
/// </summary>
public static class TerrainHoleResolution
{
    [MenuItem("Tools/Terrain/Set Heightmap Resolution/1025 (medium holes)")]
    static void Set1025() => Resize(1025);

    [MenuItem("Tools/Terrain/Set Heightmap Resolution/2049 (small holes)")]
    static void Set2049() => Resize(2049);

    [MenuItem("Tools/Terrain/Set Heightmap Resolution/4097 (tiny holes, heavy)")]
    static void Set4097() => Resize(4097);

    static void Resize(int newRes)
    {
        Terrain terrain = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Terrain>()
            : null;
        if (terrain == null)
            terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
        {
            EditorUtility.DisplayDialog("Terrain Hole Resolution", "Select a Terrain in the scene first.", "OK");
            return;
        }

        TerrainData data = terrain.terrainData;
        int oldRes = data.heightmapResolution;
        if (oldRes == newRes)
        {
            Debug.Log($"[TerrainHoleResolution] {terrain.name} is already {newRes}.");
            return;
        }

        Vector3 size = data.size;
        float oldCell = size.x / (oldRes - 1);
        float newCell = size.x / (newRes - 1);
        if (!EditorUtility.DisplayDialog(
                "Terrain Hole Resolution",
                $"{terrain.name}: heightmap {oldRes} -> {newRes}\n" +
                $"Hole size: {oldCell:0.###} m -> {newCell:0.###} m\n\n" +
                "Existing heights and holes will be resampled.",
                "Resize", "Cancel"))
            return;

        float[,] oldHeights = data.GetHeights(0, 0, oldRes, oldRes);
        int oldHoleRes = data.holesResolution;
        bool[,] oldHoles = data.GetHoles(0, 0, oldHoleRes, oldHoleRes);

        Undo.RegisterCompleteObjectUndo(data, "Change Terrain Heightmap Resolution");

        data.heightmapResolution = newRes;
        data.size = size;

        data.SetHeights(0, 0, ResampleHeights(oldHeights, oldRes, newRes));

        int newHoleRes = data.holesResolution;
        data.SetHoles(0, 0, ResampleHoles(oldHoles, oldHoleRes, newHoleRes));

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        Debug.Log($"[TerrainHoleResolution] {terrain.name}: {oldRes} -> {newRes}, hole size now {newCell:0.###} m.");
    }

    static float[,] ResampleHeights(float[,] src, int srcRes, int dstRes)
    {
        var dst = new float[dstRes, dstRes];
        float scale = (srcRes - 1f) / (dstRes - 1f);
        for (int y = 0; y < dstRes; y++)
        {
            float sy = y * scale;
            int y0 = Mathf.Min((int)sy, srcRes - 1);
            int y1 = Mathf.Min(y0 + 1, srcRes - 1);
            float ty = sy - y0;
            for (int x = 0; x < dstRes; x++)
            {
                float sx = x * scale;
                int x0 = Mathf.Min((int)sx, srcRes - 1);
                int x1 = Mathf.Min(x0 + 1, srcRes - 1);
                float tx = sx - x0;
                float a = Mathf.Lerp(src[y0, x0], src[y0, x1], tx);
                float b = Mathf.Lerp(src[y1, x0], src[y1, x1], tx);
                dst[y, x] = Mathf.Lerp(a, b, ty);
            }
        }
        return dst;
    }

    static bool[,] ResampleHoles(bool[,] src, int srcRes, int dstRes)
    {
        var dst = new bool[dstRes, dstRes];
        float scale = (float)srcRes / dstRes;
        for (int y = 0; y < dstRes; y++)
        {
            int sy = Mathf.Min((int)((y + 0.5f) * scale), srcRes - 1);
            for (int x = 0; x < dstRes; x++)
            {
                int sx = Mathf.Min((int)((x + 0.5f) * scale), srcRes - 1);
                dst[y, x] = src[sy, sx];
            }
        }
        return dst;
    }
}
#endif
