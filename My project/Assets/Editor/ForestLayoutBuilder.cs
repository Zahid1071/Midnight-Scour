#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Forest layout draft, kept for the gas station scene (its result is saved in "Assets/Terrain/Forest Layout (Gas Station).asset").
/// Builds on the scene terrain: sculpt, stairwell hole, ground layers, grass, trees and props.
/// Overwrites the active scene terrain and rebuilds the "Ground Level Decor" root, so do not run it in Main Scene.
/// </summary>
public static class ForestLayoutBuilder
{
    const string TreePrefabFolder = "Assets/Prefabs/Environment/Trees";
    const string PropPrefabFolder = "Assets/Prefabs/Environment/Nature";
    const string DecorRootName = "Ground Level Decor";
    const int DefaultSeed = 1071;

    const float TerrainBaseY = -4f;
    const float TerrainHeight = 60f;
    const float YardY = 0.02f;
    const float HouseGroundY = -1.3f;

    static readonly Vector2 Core = new Vector2(6f, -6f);
    static readonly Vector2 Truck = new Vector2(0.1f, 0.5f);
    static readonly Rect HouseRect = Rect.MinMaxRect(17.5f, -36.6f, 35.3f, -24.7f);
    static readonly Rect BasementRect = Rect.MinMaxRect(-14f, 3.5f, 17f, 26f);
    static readonly Rect StairwellRect = Rect.MinMaxRect(-9.05f, 19.25f, -5.95f, 24.35f);
    // Outside the cellar exit door; kept at the door's bottom edge (y -0.1) so the ground never covers the door.
    static readonly Rect DoorApronRect = Rect.MinMaxRect(-8.8f, 24.35f, -6.2f, 27.5f);

    // 0: cellar door around the shed to the truck, 1: truck to the porch, 2: dirt road out through the woods.
    static readonly Vector2[][] Paths =
    {
        new[] { new Vector2(-7.5f, 24.6f), new Vector2(-7.2f, 27.2f), new Vector2(-4.2f, 27.4f), new Vector2(-2.6f, 22f), new Vector2(-1.8f, 12f), new Vector2(-0.5f, 4f) },
        new[] { new Vector2(0.5f, -1.5f), new Vector2(7f, -8f), new Vector2(16f, -15.5f), new Vector2(24.5f, -21f), new Vector2(26f, -24.5f) },
        new[] { new Vector2(-2.5f, 0f), new Vector2(-18f, -6f), new Vector2(-45f, -9f), new Vector2(-80f, -4f), new Vector2(-130f, 8f), new Vector2(-200f, 10f), new Vector2(-260f, 6f) },
    };
    static readonly float[] PathWidths = { 1.1f, 2.4f, 3.2f };

    static readonly string[] TerrainLayerPaths =
    {
        "Assets/TerrainSampleAssets/TerrainLayers/Grass_Dry_TerrainLayer.terrainlayer",
        "Assets/TerrainSampleAssets/TerrainLayers/Grass_Soil_TerrainLayer.terrainlayer",
        "Assets/TerrainSampleAssets/TerrainLayers/Muddy_TerrainLayer.terrainlayer",
        "Assets/TerrainSampleAssets/TerrainLayers/Grass_Moss_TerrainLayer.terrainlayer",
    };

    // Order matters: indices are used by PlantDetails.
    static readonly string[] DetailPrefabPaths =
    {
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_A.prefab",
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_B.prefab",
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_C.prefab",
        "Assets/TerrainSampleAssets/Prefabs/Grass_C.prefab",
        "Assets/TerrainSampleAssets/Prefabs/BushDry_A.prefab",
        "Assets/TerrainSampleAssets/Prefabs/BushDry_B.prefab",
        "Assets/TerrainSampleAssets/Prefabs/Fern_A.prefab",
        "Assets/TerrainSampleAssets/Prefabs/Fern_B.prefab",
    };

    static readonly string[] DeadTreeSources = { "Dry3333", "Dry4195", "Dry4910", "Dry5818", "Dry6524", "Dry7509", "Dry8872" };
    static readonly int[] BirchSources = { 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 13, 14 };

    [MenuItem("Tools/Environment/Build Forest Layout (Gas Station Draft)")]
    static void BuildMenu() => Debug.Log(Build(DefaultSeed));

    public static string Build(int seed)
    {
        var envRoot = GameObject.Find("_Environment");
        var terrainGo = envRoot ? envRoot.transform.Find("Terrain")?.gameObject : null;
        if (!terrainGo) return "No _Environment/Terrain found.";
        var terrain = terrainGo.GetComponent<Terrain>();
        var rng = new System.Random(seed);

        Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Build Ground Level");
        Undo.RecordObject(terrainGo.transform, "Build Ground Level");
        terrainGo.SetActive(true);

        Sculpt(terrain);
        Paint(terrain);
        PlantDetails(terrain, rng);
        int trees = PlantTrees(terrain, rng);
        ConfigureTerrain(terrain);
        int props = PlaceProps(terrain, rng);

        var roof = GameObject.Find("Plane");
        if (roof && roof.TryGetComponent(out MeshRenderer roofRenderer))
            roofRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;

        EditorUtility.SetDirty(terrain.terrainData);
        AssetDatabase.SaveAssets();
        UnityEngine.SceneManagement.Scene scene = terrainGo.scene;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        return $"Ground level built: {trees} trees, {props} props.";
    }

    // ---------- Shape ----------

    static float GroundHeight(float x, float z)
    {
        var p = new Vector2(x, z);
        float core = Vector2.Distance(p, Core);
        float road = PathDistance(p, 2) - PathWidths[2] * 0.5f;

        float hills = Smooth01(Mathf.InverseLerp(50f, 170f, core)) *
                      (Mathf.PerlinNoise(x * 0.011f + 13f, z * 0.011f + 7f) * 16f + 2f);
        hills *= Mathf.Lerp(0.2f, 1f, Smooth01(Mathf.InverseLerp(3f, 30f, road)));
        float roll = Mathf.InverseLerp(22f, 50f, core) * (Mathf.PerlinNoise(x * 0.045f + 3f, z * 0.045f + 91f) - 0.5f) * 0.9f;
        roll *= Smooth01(Mathf.InverseLerp(1.5f, 6f, road));

        float h = YardY + hills + roll;
        h = Mathf.Lerp(HouseGroundY, h, Smooth01((RectDistance(HouseRect, p) - 0.5f) / 8f));
        h = Mathf.Max(h, YardY - RectDistance(BasementRect, p) * 0.15f);
        if (DoorApronRect.Contains(p))
        {
            float along = Smooth01((z - DoorApronRect.yMin) / 3f);
            float side = Smooth01((Mathf.Abs(x - DoorApronRect.center.x) - 0.9f) / 0.4f);
            h = Mathf.Lerp(-0.1f, h, Mathf.Max(along, side));
        }
        if (StairwellRect.Contains(p)) h = -0.2f;
        return h;
    }

    static void Sculpt(Terrain terrain)
    {
        var data = terrain.terrainData;
        var size = data.size;
        size.y = TerrainHeight;
        data.size = size;
        var pos = terrain.transform.position;
        pos.y = TerrainBaseY;
        terrain.transform.position = pos;

        int res = data.heightmapResolution;
        var heights = new float[res, res];
        for (int iz = 0; iz < res; iz++)
        for (int ix = 0; ix < res; ix++)
        {
            float x = pos.x + ix / (res - 1f) * size.x;
            float z = pos.z + iz / (res - 1f) * size.z;
            heights[iz, ix] = Mathf.Clamp01((GroundHeight(x, z) - TerrainBaseY) / TerrainHeight);
        }
        data.SetHeights(0, 0, heights);

        int holeRes = data.holesResolution;
        var solid = new bool[holeRes, holeRes];
        var inner = Rect.MinMaxRect(StairwellRect.xMin + 0.05f, StairwellRect.yMin + 0.05f, StairwellRect.xMax - 0.05f, StairwellRect.yMax - 0.05f);
        for (int iz = 0; iz < holeRes; iz++)
        for (int ix = 0; ix < holeRes; ix++)
        {
            float x = pos.x + (ix + 0.5f) / holeRes * size.x;
            float z = pos.z + (iz + 0.5f) / holeRes * size.z;
            solid[iz, ix] = !inner.Contains(new Vector2(x, z));
        }
        data.SetHoles(0, 0, solid);
    }

    // ---------- Ground layers ----------

    struct GroundSample
    {
        public float grass, soil, mud, moss;
        public float core, yard;
    }

    static GroundSample Sample(float x, float z)
    {
        var p = new Vector2(x, z);
        var s = new GroundSample { grass = 1f };
        s.core = Vector2.Distance(p, Core);

        float breakup = Mathf.PerlinNoise(x * 0.35f + 11f, z * 0.35f + 5f);
        for (int i = 0; i < Paths.Length; i++)
        {
            float d = PathDistance(p, i);
            float half = PathWidths[i] * 0.5f;
            if (i == 0)
                s.mud = Mathf.Max(s.mud, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half - 0.2f, half + 0.6f, d + (breakup - 0.5f) * 0.6f)));
            else
            {
                float track = Mathf.Abs(d - half * 0.55f);
                s.mud = Mathf.Max(s.mud, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.55f, track + (breakup - 0.5f) * 0.3f)));
            }
            s.soil = Mathf.Max(s.soil, (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half, half + 2.5f, d))) * 0.85f);
        }

        float house = RectDistance(HouseRect, p);
        s.soil = Mathf.Max(s.soil, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 4f, house)));
        s.mud = Mathf.Max(s.mud, (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 1.5f, house))) * 0.7f);
        float truck = Vector2.Distance(p, Truck);
        s.mud = Mathf.Max(s.mud, (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f, 5f, truck))) * 0.8f);

        s.soil = Mathf.Max(s.soil, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.8f, Mathf.PerlinNoise(x * 0.08f + 31f, z * 0.08f + 17f))) * 0.7f);
        s.moss = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.75f, Mathf.PerlinNoise(x * 0.02f + 50f, z * 0.02f + 50f))) *
                 Mathf.InverseLerp(25f, 60f, s.core);

        s.yard = YardDistance(p);
        return s;
    }

    static void Paint(Terrain terrain)
    {
        var data = terrain.terrainData;
        var layers = new TerrainLayer[TerrainLayerPaths.Length];
        for (int i = 0; i < layers.Length; i++)
            layers[i] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainLayerPaths[i]);
        layers[0].tileSize = new Vector2(3f, 3f);
        layers[2].tileSize = new Vector2(2.5f, 2.5f);
        var tints = new[] { new Color(0.62f, 0.6f, 0.5f), new Color(0.7f, 0.64f, 0.56f), new Color(0.6f, 0.55f, 0.5f), new Color(0.6f, 0.62f, 0.52f) };
        for (int i = 0; i < layers.Length; i++)
        {
            var l = layers[i];
            var max = l.maskMapRemapMax;
            max.w = 0.04f;
            l.maskMapRemapMax = max;
            l.diffuseRemapMax = tints[i];
            l.normalScale = 1.4f;
            EditorUtility.SetDirty(l);
        }

        data.terrainLayers = layers;
        data.alphamapResolution = 1024;
        int res = data.alphamapResolution;
        var pos = terrain.transform.position;
        var size = data.size;
        var maps = new float[res, res, 4];
        for (int iz = 0; iz < res; iz++)
        for (int ix = 0; ix < res; ix++)
        {
            float x = pos.x + (ix + 0.5f) / res * size.x;
            float z = pos.z + (iz + 0.5f) / res * size.z;
            var s = Sample(x, z);
            float mud = s.mud;
            float soil = s.soil * (1f - mud);
            float moss = s.moss * (1f - mud) * (1f - soil);
            float grass = Mathf.Max(0f, 1f - mud - soil - moss);
            float sum = grass + soil + mud + moss;
            maps[iz, ix, 0] = grass / sum;
            maps[iz, ix, 1] = soil / sum;
            maps[iz, ix, 2] = mud / sum;
            maps[iz, ix, 3] = moss / sum;
        }
        data.SetAlphamaps(0, 0, maps);
    }

    // ---------- Grass ----------

    static void PlantDetails(Terrain terrain, System.Random rng)
    {
        var data = terrain.terrainData;
        data.SetDetailResolution(1024, 32);
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

        var protos = new DetailPrototype[DetailPrefabPaths.Length];
        for (int i = 0; i < protos.Length; i++)
        {
            bool bush = i == 4 || i == 5;
            protos[i] = new DetailPrototype
            {
                prototype = AssetDatabase.LoadAssetAtPath<GameObject>(DetailPrefabPaths[i]),
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = bush ? 0.7f : 0.8f,
                maxWidth = bush ? 1.4f : 1.3f,
                minHeight = bush ? 0.7f : 0.75f,
                maxHeight = bush ? 1.4f : 1.4f,
                noiseSeed = 100 + i,
                noiseSpread = 0.3f,
                density = 1f,
                alignToGround = 0.6f,
                positionJitter = 1f,
                healthyColor = Color.white,
                dryColor = Color.white,
            };
        }
        data.detailPrototypes = protos;

        int res = data.detailResolution;
        var pos = terrain.transform.position;
        var size = data.size;
        var layers = new int[protos.Length][,];
        for (int i = 0; i < layers.Length; i++) layers[i] = new int[res, res];

        for (int iz = 0; iz < res; iz++)
        for (int ix = 0; ix < res; ix++)
        {
            float x = pos.x + (ix + 0.5f) / res * size.x;
            float z = pos.z + (iz + 0.5f) / res * size.z;
            var p = new Vector2(x, z);
            if (Vector2.Distance(p, Core) > 140f || StairwellRect.Contains(p) || RectDistance(HouseRect, p) < 0.3f || Vector2.Distance(p, Truck) < 3.2f)
                continue;
            var s = Sample(x, z);
            if (s.mud > 0.35f) continue;

            float lush = (1f - s.soil * 0.7f) * Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(x * 0.15f + 7f, z * 0.15f + 3f));
            double r = rng.NextDouble();
            if (r < 0.85 * lush)
                layers[rng.Next(0, 3)][iz, ix] = 1 + (int)(lush * 2.5f * (float)rng.NextDouble());
            else if (r < 0.92 * lush)
                layers[3][iz, ix] = 1;
            else if (s.yard > 6f && r < 0.92 * lush + 0.012)
                layers[4 + rng.Next(0, 2)][iz, ix] = 1;
            else if (s.moss > 0.3f && r < 0.92 * lush + 0.012 + 0.08 * s.moss)
                layers[6 + rng.Next(0, 2)][iz, ix] = 1;
        }
        for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
    }

    // ---------- Trees ----------

    static int PlantTrees(Terrain terrain, System.Random rng)
    {
        var dead = new List<GameObject>();
        foreach (var n in DeadTreeSources)
            dead.Add(MakeTreeVariant($"Assets/Dry_Trees/Prefab/{n}.prefab", $"DeadTree_{n.Substring(3)}", false));
        var birch = new List<GameObject>();
        foreach (int n in BirchSources)
            birch.Add(MakeTreeVariant($"Assets/Next_Spring/Tree_Bundle/Prefabs/White_Birch/Mobile/M_Realistic21_{n}.prefab", $"Birch_{n:00}", true));

        var data = terrain.terrainData;
        var protos = new List<TreePrototype>();
        foreach (var g in dead) protos.Add(new TreePrototype { prefab = g });
        foreach (var g in birch) protos.Add(new TreePrototype { prefab = g });
        data.treePrototypes = protos.ToArray();

        var pos = terrain.transform.position;
        var size = data.size;
        var instances = new List<TreeInstance>();
        const float cell = 6f;
        for (float gz = pos.z + 4f; gz < pos.z + size.z - 4f; gz += cell)
        for (float gx = pos.x + 4f; gx < pos.x + size.x - 4f; gx += cell)
        {
            float x = gx + (float)rng.NextDouble() * cell;
            float z = gz + (float)rng.NextDouble() * cell;
            var p = new Vector2(x, z);
            float yard = YardDistance(p);
            if (yard < 7f || PathDistance(p, 2) < PathWidths[2] * 0.5f + 3.5f) continue;
            if (RectDistance(BasementRect, p) < 1f) continue;

            float groves = Mathf.PerlinNoise(x * 0.025f + 71f, z * 0.025f + 29f);
            float chance = Mathf.Lerp(0.07f, 1f, Smooth01(Mathf.InverseLerp(10f, 38f, yard))) * Mathf.Lerp(0.5f, 1.3f, groves);
            if (rng.NextDouble() > chance) continue;

            bool isDead = rng.NextDouble() < Mathf.Lerp(0.85f, 0.6f, Mathf.InverseLerp(25f, 90f, yard));
            int index = isDead ? rng.Next(0, dead.Count) : dead.Count + rng.Next(0, birch.Count);
            float scale = isDead ? Mathf.Lerp(0.55f, 0.9f, (float)rng.NextDouble()) : Mathf.Lerp(0.42f, 0.7f, (float)rng.NextDouble());
            instances.Add(new TreeInstance
            {
                prototypeIndex = index,
                position = new Vector3((x - pos.x) / size.x, 0f, (z - pos.z) / size.z),
                heightScale = scale,
                widthScale = scale * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble()),
                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white,
            });
        }
        data.SetTreeInstances(instances.ToArray(), true);
        return instances.Count;
    }

    static GameObject MakeTreeVariant(string sourcePath, string name, bool addLodGroup)
    {
        EnsureFolder(TreePrefabFolder);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            foreach (var mc in instance.GetComponentsInChildren<MeshCollider>()) Object.DestroyImmediate(mc);
            var capsule = instance.AddComponent<CapsuleCollider>();
            capsule.radius = TrunkRadius(instance);
            capsule.height = 8f;
            capsule.center = new Vector3(0f, 4f, 0f);

            if (addLodGroup && !instance.GetComponent<LODGroup>())
            {
                var lod = instance.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, instance.GetComponentsInChildren<Renderer>()) });
                lod.RecalculateBounds();
            }
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m && !m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); }

            return PrefabUtility.SaveAsPrefabAsset(instance, $"{TreePrefabFolder}/{name}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static float TrunkRadius(GameObject tree)
    {
        var lod = tree.GetComponent<LODGroup>();
        Renderer r = lod ? lod.GetLODs()[0].renderers[0] : tree.GetComponentInChildren<Renderer>();
        var mesh = r ? r.GetComponent<MeshFilter>()?.sharedMesh : null;
        if (!mesh) return 0.3f;
        var dists = new List<float>();
        var toRoot = tree.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
        foreach (var v in mesh.vertices)
        {
            var p = toRoot.MultiplyPoint3x4(v);
            if (p.y > 0.6f && p.y < 1.6f) dists.Add(new Vector2(p.x, p.z).magnitude);
        }
        if (dists.Count == 0) return 0.3f;
        dists.Sort();
        return Mathf.Clamp(dists[(int)(dists.Count * 0.6f)], 0.12f, 0.8f);
    }

    // ---------- Props ----------

    struct PropSpot
    {
        public string prefab;
        public float x, z, yaw, scale;
        public PropSpot(string prefab, float x, float z, float yaw, float scale = 1f) { this.prefab = prefab; this.x = x; this.z = z; this.yaw = yaw; this.scale = scale; }
    }

    static readonly PropSpot[] PropSpots =
    {
        // In view from the cellar door
        new PropSpot("PH_DeadLog_A", -3.6f, 32.5f, 28f),
        new PropSpot("PH_Stump_A", -12.2f, 30.4f, 70f),
        new PropSpot("PH_DryBranches", -9.6f, 29.2f, 15f),
        new PropSpot("PH_DryBranches", -1.4f, 30.8f, 140f, 1.2f),
        new PropSpot("PH_MossRock_2", -15.5f, 36f, 40f),
        new PropSpot("PH_MossRock_4", 7.5f, 35f, 200f),
        new PropSpot("PH_Shrub_C", -10.4f, 18.4f, 0f),
        new PropSpot("PH_Shrub_A", -4.6f, 25.6f, 120f),
        new PropSpot("PH_Shrub_D", -14f, 26f, 60f),
        // Around the truck and the drive
        new PropSpot("PH_DryBranches", 3.8f, 4.2f, 260f),
        new PropSpot("PH_Stump_B", 10.5f, -3.5f, 30f),
        new PropSpot("PH_DeadLog_B", 6.5f, -17.5f, 65f),
        new PropSpot("PH_MossRock_5", 13f, -9f, 10f, 0.8f),
        new PropSpot("PH_Shrub_B", 4f, -12f, 300f),
        // Around the farmhouse
        new PropSpot("PH_Shrub_C", 16.4f, -23.8f, 90f),
        new PropSpot("PH_Shrub_A", 36.4f, -24f, 10f),
        new PropSpot("PH_Shrub_D", 36.6f, -36f, 200f),
        new PropSpot("PH_Shrub_B", 16.6f, -37.4f, 140f),
        new PropSpot("PH_Stump_A", 38.5f, -20f, 300f),
        new PropSpot("PH_DryBranches", 31f, -21f, 80f),
        // Edge of the dirt road
        new PropSpot("PH_MossRock_1", -21f, -1.5f, 120f),
        new PropSpot("PH_MossRock_3", -34f, -14f, 300f),
        new PropSpot("PH_DeadLog_A", -28f, -2f, 95f, 1.2f),
        new PropSpot("PH_Stump_B", -40f, -3.5f, 210f),
        new PropSpot("PH_MossRock_6", -55f, -14f, 80f),
    };

    static int PlaceProps(Terrain terrain, System.Random rng)
    {
        var prefabs = BuildPropPrefabs();
        var old = GameObject.Find(DecorRootName);
        if (old) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(DecorRootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Ground Level");

        int count = 0;
        foreach (var spot in PropSpots)
        {
            if (!prefabs.TryGetValue(spot.prefab, out var prefab)) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            var world = new Vector3(spot.x, 0f, spot.z);
            world.y = terrain.SampleHeight(world) + terrain.transform.position.y;
            var data = terrain.terrainData;
            var tp = terrain.transform.position;
            var normal = data.GetInterpolatedNormal((spot.x - tp.x) / data.size.x, (spot.z - tp.z) / data.size.z);
            go.transform.SetPositionAndRotation(world, Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, normal, 0.6f)) * Quaternion.Euler(0f, spot.yaw, 0f));
            go.transform.localScale = Vector3.one * spot.scale * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble());
            count++;
        }
        return count;
    }

    static Dictionary<string, GameObject> BuildPropPrefabs()
    {
        EnsureFolder(PropPrefabFolder);
        const string ph = "Assets/Props/PolyHaven";
        var result = new Dictionary<string, GameObject>();
        void Add(string name, string fbx, string[] parts, bool collider, float sink, float[] lods = null)
        {
            result[name] = MakePropPrefab(name, $"{ph}/{fbx}/{fbx}_1k.fbx", parts, collider, sink, lods);
        }
        Add("PH_DeadLog_A", "dead_tree_trunk", null, true, 0.04f);
        Add("PH_DeadLog_B", "dead_tree_trunk_02", null, true, 0.08f, new[] { 0.4f, 0.15f, 0.05f, 0.01f });
        Add("PH_Stump_A", "tree_stump_01", null, true, 0.06f);
        Add("PH_Stump_B", "tree_stump_02", null, true, 0.06f);
        Add("PH_DryBranches", "dry_branches_medium_01", null, false, 0.02f);
        for (int i = 1; i <= 6; i++)
            Add($"PH_MossRock_{i}", "rock_moss_set_01", new[] { $"rock_moss_set_01_rock0{i}" }, true, 0.2f);
        foreach (var c in new[] { "a", "b", "c", "d" })
            Add($"PH_Shrub_{c.ToUpper()}", "shrub_02", new[] { $"shrub_02_{c}_LOD0" }, false, 0.03f);
        return result;
    }

    static GameObject MakePropPrefab(string name, string fbxPath, string[] parts, bool collider, float sinkFraction, float[] lodHeights)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        var source = Object.Instantiate(model);
        var root = new GameObject(name);
        try
        {
            var keep = new List<Transform>();
            foreach (var r in source.GetComponentsInChildren<Renderer>())
                if (parts == null || System.Array.IndexOf(parts, r.name) >= 0)
                    keep.Add(r.transform);

            var holder = new GameObject("Mesh").transform;
            holder.SetParent(root.transform, false);
            foreach (var t in keep) t.SetParent(holder, true);

            var renderers = holder.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            holder.position = -new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * sinkFraction, bounds.center.z);

            if (lodHeights != null)
            {
                var lods = new LOD[Mathf.Min(lodHeights.Length, renderers.Length)];
                for (int i = 0; i < lods.Length; i++) lods[i] = new LOD(lodHeights[i], new[] { renderers[i] });
                var group = root.AddComponent<LODGroup>();
                group.SetLODs(lods);
                group.RecalculateBounds();
            }
            if (collider)
            {
                var colliderRenderer = renderers[renderers.Length > 1 && lodHeights != null ? Mathf.Min(1, renderers.Length - 1) : 0];
                var mc = colliderRenderer.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = colliderRenderer.GetComponent<MeshFilter>().sharedMesh;
            }
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            return PrefabUtility.SaveAsPrefabAsset(root, $"{PropPrefabFolder}/{name}.prefab");
        }
        finally
        {
            Object.DestroyImmediate(root);
            if (source) Object.DestroyImmediate(source);
        }
    }

    // ---------- Terrain settings ----------

    static void ConfigureTerrain(Terrain terrain)
    {
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 4f;
        terrain.basemapDistance = 150f;
        terrain.treeDistance = 220f;
        terrain.treeBillboardDistance = 60f;
        terrain.treeCrossFadeLength = 5f;
        terrain.treeMaximumFullLODCount = 60;
        terrain.detailObjectDistance = 70f;
        terrain.detailObjectDensity = 1f;
        terrain.shadowCastingMode = ShadowCastingMode.On;
        EditorUtility.SetDirty(terrain);
    }

    // ---------- Helpers ----------

    static float YardDistance(Vector2 p)
    {
        float d = Mathf.Min(RectDistance(HouseRect, p) - 2f, Vector2.Distance(p, Truck) - 4f);
        d = Mathf.Min(d, RectDistance(StairwellRect, p) - 2f);
        d = Mathf.Min(d, PathDistance(p, 0) - PathWidths[0] * 0.5f);
        d = Mathf.Min(d, PathDistance(p, 1) - PathWidths[1] * 0.5f);
        return d;
    }

    static float PathDistance(Vector2 p, int path)
    {
        var pts = Paths[path];
        float best = float.MaxValue;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var a = pts[i];
            var ab = pts[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    static float RectDistance(Rect r, Vector2 p)
    {
        float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
        float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float Smooth01(float t) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
#endif
