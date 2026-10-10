#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Turns the scene terrain into a Midwest farmstead: flat yard around the cellar exit, the farmhouse facing the
/// cellar door, the truck parked between them, a gravel county road and driveway, crop fields and windbreaks.
/// Safe to re-run; it overwrites the terrain data and rebuilds the farmhouse.
/// </summary>
public static class FarmLayoutBuilder
{
    const string TreePrefabFolder = "Assets/Prefabs/Environment/Trees";
    const string ForestDecorRootName = "Ground Level Decor";
    const int DefaultSeed = 1071;

    const float TerrainBaseY = -4f;
    const float TerrainHeight = 60f;
    const float YardY = 0.02f;
    const float HousePadY = 0.05f;

    // Farmhouse local frame is turned 180 degrees so its front porch faces the cellar door.
    static readonly Vector3 HouseOrigin = new Vector3(0f, 0.55f, 56f);
    const float HouseYaw = 180f;
    static readonly Rect HouseRect = Rect.MinMaxRect(-18.5f, 43.5f, 3.5f, 59.5f);

    // Driver side faces the cellar door; nose points out toward the driveway. Offset right so the porch steps stay visible.
    static readonly Vector3 TruckPosition = new Vector3(-3.4f, 0f, 33.2f);
    const float TruckYaw = 194f;

    static readonly Vector2 Core = new Vector2(-6f, 42f);
    static readonly Rect FarmsteadRect = Rect.MinMaxRect(-38f, 4f, 24f, 82f);
    static readonly Rect GravelPadRect = Rect.MinMaxRect(-19f, 29.5f, 3f, 40.5f);
    static readonly Rect BasementRect = Rect.MinMaxRect(-14f, 3.5f, 17f, 26f);
    static readonly Rect StairwellRect = Rect.MinMaxRect(-9.05f, 19.25f, -5.95f, 24.35f);
    // Outside the cellar exit door; kept at the door's bottom edge (y -0.1) so the ground never covers the door.
    static readonly Rect DoorApronRect = Rect.MinMaxRect(-8.8f, 24.35f, -6.2f, 27.5f);

    const float CountyRoadX = -70f;
    const float CountyRoadHalfWidth = 3.5f;

    // 0: cellar door to the gravel pad, 1: gravel pad to the porch steps, 2: driveway to the county road, 3: field lane.
    static readonly Vector2[][] Paths =
    {
        new[] { new Vector2(-7.5f, 24.6f), new Vector2(-7.4f, 27.5f), new Vector2(-7.2f, 30f) },
        new[] { new Vector2(-7.5f, 40f), new Vector2(-7.5f, 44.2f) },
        new[] { new Vector2(-18f, 35f), new Vector2(-36f, 35.5f), new Vector2(-52f, 36.2f), new Vector2(CountyRoadX, 36.5f) },
        new[] { new Vector2(2f, 35f), new Vector2(22f, 36f), new Vector2(34f, 39f), new Vector2(60f, 39.5f) },
    };
    static readonly float[] PathWidths = { 1.2f, 1.4f, 4.2f, 2.8f };

    struct Field
    {
        public Rect rect;
        public bool rowsAlongZ, stubble;
        public Field(Rect rect, bool rowsAlongZ, bool stubble) { this.rect = rect; this.rowsAlongZ = rowsAlongZ; this.stubble = stubble; }
    }

    static readonly Field[] Fields =
    {
        new Field(Rect.MinMaxRect(30f, -60f, 260f, 260f), true, true),     // east: harvested corn
        new Field(Rect.MinMaxRect(-64f, 102f, 30f, 260f), false, true),    // north, past the windbreak
        new Field(Rect.MinMaxRect(-64f, -260f, 260f, -64f), false, true),  // south
        new Field(Rect.MinMaxRect(-260f, -260f, -78f, 260f), true, false), // west of the road: fall-plowed
    };
    const float FieldMargin = 3.5f;
    const float RowSpacing = 500f / 512f;
    static Vector3 _terrainOrigin;

    static readonly string[] TerrainLayerPaths =
    {
        "Assets/TerrainSampleAssets/TerrainLayers/Grass_Dry_TerrainLayer.terrainlayer",
        "Assets/TerrainSampleAssets/TerrainLayers/Grass_Soil_TerrainLayer.terrainlayer",
        "Assets/Terrain/Farm/Farm Soil.terrainlayer",
        "Assets/Terrain/Farm/Gravel Road.terrainlayer",
    };

    // Order matters: indices are used by PlantDetails.
    static readonly string[] DetailPrefabPaths =
    {
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_A.prefab",
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_B.prefab",
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_C.prefab",
        "Assets/TerrainSampleAssets/Prefabs/Grass_C.prefab",
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_B.prefab",
        "Assets/TerrainSampleAssets/Prefabs/GrassDry_C.prefab",
        "Assets/TerrainSampleAssets/Prefabs/BushDry_A.prefab",
    };
    const int StubbleDetail = 4, TallGrassDetail = 5, BushDetail = 6;

    static readonly string[] DeadTreeNames = { "DeadTree_3333", "DeadTree_4195", "DeadTree_4910", "DeadTree_5818", "DeadTree_6524", "DeadTree_7509", "DeadTree_8872" };
    static readonly int[] BirchNumbers = { 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 13, 14 };

    static readonly Vector2[] YardTrees =
    {
        new Vector2(-25f, 57f), new Vector2(-28f, 19f), new Vector2(13f, 66f), new Vector2(19f, 31f), new Vector2(-30f, 70f),
    };

    [MenuItem("Tools/Environment/Build Farm Layout")]
    static void BuildMenu() => Debug.Log(Build(DefaultSeed));

    public static string Build(int seed)
    {
        var envRoot = GameObject.Find("_Environment");
        var terrainGo = envRoot ? envRoot.transform.Find("Terrain")?.gameObject : null;
        if (!terrainGo) return "No _Environment/Terrain found.";
        var terrain = terrainGo.GetComponent<Terrain>();
        var rng = new System.Random(seed);

        Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Build Farm Layout");
        Undo.RecordObject(terrainGo.transform, "Build Farm Layout");
        terrainGo.SetActive(true);
        _terrainOrigin = terrain.transform.position;

        EnsureFarmLayers();
        Sculpt(terrain);
        Paint(terrain);
        PlantDetails(terrain, rng);
        int trees = PlantTrees(terrain, rng);
        ConfigureTerrain(terrain);

        var forestDecor = GameObject.Find(ForestDecorRootName);
        if (forestDecor) Undo.DestroyObjectImmediate(forestDecor);
        var oldHouse = envRoot.transform.Find("House");
        if (oldHouse)
        {
            Undo.RecordObject(oldHouse.gameObject, "Build Farm Layout");
            oldHouse.gameObject.SetActive(false);
        }
        PlaceTruck(envRoot.transform);
        string house = FarmhouseBuilder.Build(HouseOrigin, HouseYaw);

        EditorUtility.SetDirty(terrain.terrainData);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrainGo.scene);
        return $"Farm layout built: {trees} trees. {house}";
    }

    // ---------- Shape ----------

    static float BaseHeight(float x, float z)
    {
        var p = new Vector2(x, z);
        float core = Vector2.Distance(p, Core);
        float rolling = (Mathf.PerlinNoise(x * 0.0055f + 13f, z * 0.0055f + 7f) - 0.35f) * 11f;
        rolling *= Smooth01(Mathf.InverseLerp(70f, 190f, core));
        float swell = (Mathf.PerlinNoise(x * 0.03f + 3f, z * 0.03f + 91f) - 0.5f) * 0.5f;
        swell *= Smooth01(Mathf.InverseLerp(35f, 70f, core));
        return YardY + rolling + swell;
    }

    static float GroundHeight(float x, float z)
    {
        var p = new Vector2(x, z);
        float h = BaseHeight(x, z);

        float roadCenter = BaseHeight(CountyRoadX, z);
        float dx = Mathf.Abs(x - CountyRoadX);
        float ditch = Mathf.Exp(-Mathf.Pow((dx - 6.5f) / 1.4f, 2f)) * 0.55f;
        float roadBlend = Smooth01(Mathf.InverseLerp(9f, 5f, dx));
        h = Mathf.Lerp(h, roadCenter, roadBlend) - ditch;
        if (dx < CountyRoadHalfWidth) h = roadCenter + 0.12f * (1f - dx * dx / (CountyRoadHalfWidth * CountyRoadHalfWidth));

        var field = FieldAt(p, out float edge);
        if (field.HasValue && edge > FieldMargin)
        {
            float rows = Smooth01(Mathf.InverseLerp(FieldMargin, FieldMargin + 2f, edge)) *
                         (1f - Smooth01(Mathf.InverseLerp(120f, 170f, Vector2.Distance(p, Core))));
            h += RowProfile(x, z, field.Value) * 0.05f * rows;
        }

        h = Mathf.Lerp(HousePadY, h, Smooth01((RectDistance(HouseRect, p) - 0.5f) / 6f));
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
        public float grass, straw, soil, gravel;
        public float field; // 0 outside crop fields, 1 inside the planted area
        public float ridge; // 1 on a crop row, 0 in the furrow
        public bool stubbleField;
    }

    static GroundSample Sample(float x, float z)
    {
        var p = new Vector2(x, z);
        var s = new GroundSample { grass = 1f };
        float breakup = Mathf.PerlinNoise(x * 0.35f + 11f, z * 0.35f + 5f) - 0.5f;

        float pad = RectDistance(GravelPadRect, p) - 1.5f + breakup * 1.2f;
        s.gravel = 1f - Smooth01(Mathf.InverseLerp(-1f, 1.5f, pad));
        float road = Mathf.Abs(x - CountyRoadX) - CountyRoadHalfWidth + breakup * 0.8f;
        s.gravel = Mathf.Max(s.gravel, 1f - Smooth01(Mathf.InverseLerp(-0.6f, 0.8f, road)));
        float drive = PathDistance(p, 2) - PathWidths[2] * 0.5f + breakup * 0.6f;
        s.gravel = Mathf.Max(s.gravel, 1f - Smooth01(Mathf.InverseLerp(-0.6f, 0.7f, drive)));

        for (int i = 0; i < 2; i++)
        {
            float d = PathDistance(p, i) - PathWidths[i] * 0.5f + breakup * 0.5f;
            s.soil = Mathf.Max(s.soil, 1f - Smooth01(Mathf.InverseLerp(-0.2f, 0.6f, d)));
        }
        float lane = PathDistance(p, 3);
        float rut = Mathf.Abs(lane - PathWidths[3] * 0.3f) + breakup * 0.25f;
        if (Vector2.Distance(p, Core) > 12f)
            s.soil = Mathf.Max(s.soil, 1f - Smooth01(Mathf.InverseLerp(0.2f, 0.5f, rut)));

        float house = RectDistance(HouseRect, p);
        s.straw = Mathf.Max(s.straw, (1f - Smooth01(Mathf.InverseLerp(-1f, 1.5f, house))) * 0.6f);
        s.straw = Mathf.Max(s.straw, Smooth01(Mathf.InverseLerp(0.55f, 0.8f, Mathf.PerlinNoise(x * 0.08f + 31f, z * 0.08f + 17f))) * 0.6f);

        // Yard / pasture / road weights.
        s.soil *= 1f - s.gravel;
        s.straw = Mathf.Min(s.straw, 1f - s.gravel - s.soil);
        s.grass = 1f - s.gravel - s.soil - s.straw;

        var field = FieldAt(p, out float edge);
        if (field.HasValue)
        {
            var f = field.Value;
            s.field = Smooth01(Mathf.InverseLerp(FieldMargin - 0.5f + breakup, FieldMargin + 1f, edge));
            s.stubbleField = f.stubble;
            s.ridge = RowProfile(x, z, f) > 0f ? 1f : 0f;
            float rows = 1f - Smooth01(Mathf.InverseLerp(110f, 170f, Vector2.Distance(p, Core)));
            float patchy = Mathf.Lerp(0.7f, 1.2f, Mathf.PerlinNoise(x * 0.05f + 7f, z * 0.05f + 77f));
            float fieldStraw = (f.stubble ? Mathf.Lerp(0.3f, s.ridge * 0.75f, rows) : Mathf.Lerp(0.05f, s.ridge * 0.15f, rows)) * patchy;
            fieldStraw = Mathf.Clamp01(fieldStraw);
            float k = s.field;
            s.grass *= 1f - k;
            s.gravel *= 1f - k;
            s.straw = Mathf.Lerp(s.straw, fieldStraw, k);
            s.soil = Mathf.Lerp(s.soil, 1f - fieldStraw, k);
        }
        return s;
    }

    static void Paint(Terrain terrain)
    {
        var data = terrain.terrainData;
        var layers = new TerrainLayer[TerrainLayerPaths.Length];
        for (int i = 0; i < layers.Length; i++)
            layers[i] = AssetDatabase.LoadAssetAtPath<TerrainLayer>(TerrainLayerPaths[i]);
        data.terrainLayers = layers;
        data.alphamapResolution = 2048;

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
            float grass = Mathf.Max(0f, s.grass), straw = Mathf.Max(0f, s.straw), soil = Mathf.Max(0f, s.soil), gravel = Mathf.Max(0f, s.gravel);
            float sum = Mathf.Max(1e-4f, grass + straw + soil + gravel);
            maps[iz, ix, 0] = grass / sum;
            maps[iz, ix, 1] = straw / sum;
            maps[iz, ix, 2] = soil / sum;
            maps[iz, ix, 3] = gravel / sum;
        }
        data.SetAlphamaps(0, 0, maps);
    }

    static void EnsureFarmLayers()
    {
        MakeLayer(TerrainLayerPaths[2], "Assets/Terrain/Farm/farm_soil", 2.5f, new Color(0.72f, 0.66f, 0.6f));
        MakeLayer(TerrainLayerPaths[3], "Assets/Terrain/Farm/gravel_road", 3f, new Color(0.72f, 0.95f, 1.25f));
    }

    static void MakeLayer(string path, string texturePrefix, float tile, Color tint)
    {
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        bool created = !layer;
        if (created) layer = new TerrainLayer();
        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePrefix + "_diff.jpg");
        layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePrefix + "_nor_gl.png");
        layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePrefix + "_mask.png");
        layer.tileSize = new Vector2(tile, tile);
        layer.normalScale = 1.2f;
        layer.diffuseRemapMax = tint;
        layer.maskMapRemapMin = Vector4.zero;
        layer.maskMapRemapMax = new Vector4(0f, 1f, 1f, 0.12f);
        if (created) AssetDatabase.CreateAsset(layer, path);
        else EditorUtility.SetDirty(layer);
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
            float minW = 0.8f, maxW = 1.3f, minH = 0.75f, maxH = 1.4f;
            if (i == StubbleDetail) { minW = 0.45f; maxW = 0.8f; minH = 0.25f; maxH = 0.45f; }
            else if (i == TallGrassDetail) { minW = 1f; maxW = 1.5f; minH = 1.3f; maxH = 1.9f; }
            else if (i == BushDetail) { minW = 0.7f; maxW = 1.3f; minH = 0.7f; maxH = 1.3f; }
            protos[i] = new DetailPrototype
            {
                prototype = AssetDatabase.LoadAssetAtPath<GameObject>(DetailPrefabPaths[i]),
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = minW,
                maxWidth = maxW,
                minHeight = minH,
                maxHeight = maxH,
                noiseSeed = 100 + i,
                noiseSpread = 0.3f,
                density = 1f,
                alignToGround = 0.6f,
                positionJitter = i == StubbleDetail ? 0.35f : 1f,
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

        var truck = new Vector2(TruckPosition.x, TruckPosition.z);
        for (int iz = 0; iz < res; iz++)
        for (int ix = 0; ix < res; ix++)
        {
            float x = pos.x + (ix + 0.5f) / res * size.x;
            float z = pos.z + (iz + 0.5f) / res * size.z;
            var p = new Vector2(x, z);
            float core = Vector2.Distance(p, Core);
            if (core > 150f || StairwellRect.Contains(p) || RectDistance(HouseRect, p) < 0.3f || Vector2.Distance(p, truck) < 3.6f)
                continue;
            var s = Sample(x, z);
            if (s.gravel > 0.3f || s.soil > 0.55f && s.field < 0.5f) continue;
            double r = rng.NextDouble();

            if (s.field > 0.5f)
            {
                if (s.stubbleField && s.ridge > 0.5f && core < 130f && r < 0.8)
                    layers[StubbleDetail][iz, ix] = 1 + rng.Next(0, 3);
                continue;
            }

            bool margin = FieldAt(p, out _).HasValue || Mathf.Abs(x - CountyRoadX) < 9f;
            float lush = (1f - s.straw * 0.6f) * Mathf.Lerp(0.55f, 1f, Mathf.PerlinNoise(x * 0.15f + 7f, z * 0.15f + 3f));
            if (margin && r < 0.35)
                layers[TallGrassDetail][iz, ix] = 1 + rng.Next(0, 2);
            else if (r < 0.85 * lush)
                layers[rng.Next(0, 3)][iz, ix] = 1 + (int)(lush * 2.5f * (float)rng.NextDouble());
            else if (r < 0.92 * lush)
                layers[3][iz, ix] = 1;
            else if (margin && r < 0.92 * lush + 0.02)
                layers[BushDetail][iz, ix] = 1;
        }
        for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
    }

    // ---------- Trees ----------

    static int PlantTrees(Terrain terrain, System.Random rng)
    {
        var dead = new List<GameObject>();
        foreach (var n in DeadTreeNames) dead.Add(LoadTree(n));
        var birch = new List<GameObject>();
        foreach (int n in BirchNumbers) birch.Add(LoadTree($"Birch_{n:00}"));

        var data = terrain.terrainData;
        var protos = new List<TreePrototype>();
        foreach (var g in dead) protos.Add(new TreePrototype { prefab = g });
        foreach (var g in birch) protos.Add(new TreePrototype { prefab = g });
        data.treePrototypes = protos.ToArray();

        var pos = terrain.transform.position;
        var size = data.size;
        var instances = new List<TreeInstance>();
        void Add(float x, float z, float deadChance, float sizeBias)
        {
            if (x < pos.x + 2f || z < pos.z + 2f || x > pos.x + size.x - 2f || z > pos.z + size.z - 2f) return;
            bool isDead = rng.NextDouble() < deadChance;
            int index = isDead ? rng.Next(0, dead.Count) : dead.Count + rng.Next(0, birch.Count);
            float scale = (isDead ? Mathf.Lerp(0.6f, 0.9f, (float)rng.NextDouble()) : Mathf.Lerp(0.45f, 0.7f, (float)rng.NextDouble())) * sizeBias;
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
        float J(float amount) => ((float)rng.NextDouble() - 0.5f) * 2f * amount;

        // Windbreak: two staggered rows along the north and west sides of the farmstead.
        for (int row = 0; row < 2; row++)
        {
            for (float x = -50f; x <= 30f; x += 5.5f)
                Add(x + J(1.2f) + row * 2.7f, 89f + row * 6f + J(1f), 0.4f, 1f);
            for (float z = 2f; z <= 86f; z += 5.5f)
            {
                if (z > 29f && z < 43f) continue; // driveway gap
                Add(-46f - row * 6f + J(1f), z + J(1.2f) + row * 2.7f, 0.4f, 1f);
            }
        }

        foreach (var t in YardTrees) Add(t.x, t.y, 0.7f, 1.15f);

        // Distant tree lines along creeks and field edges, so the horizon is not empty.
        void Line(Vector2 a, Vector2 b, float spacing, float gapChance)
        {
            float len = Vector2.Distance(a, b);
            for (float d = 0f; d < len; d += spacing)
            {
                if (Mathf.PerlinNoise(d * 0.02f + a.x * 0.01f, a.y * 0.01f) < gapChance) continue;
                var p = Vector2.Lerp(a, b, d / len);
                Add(p.x + J(3f), p.y + J(3f), 0.5f, 1.1f);
            }
        }
        Line(new Vector2(-245f, 215f), new Vector2(245f, 205f), 4.5f, 0.35f);
        Line(new Vector2(185f, -245f), new Vector2(175f, 200f), 5f, 0.45f);
        Line(new Vector2(-180f, -245f), new Vector2(-195f, 245f), 5f, 0.5f);
        Line(new Vector2(-60f, -150f), new Vector2(170f, -170f), 6f, 0.55f);
        Line(new Vector2(-240f, -80f), new Vector2(-90f, -70f), 6f, 0.5f);

        // Lone trees on field margins.
        foreach (var f in Fields)
            for (int i = 0; i < 6; i++)
            {
                bool vertical = rng.NextDouble() < 0.5;
                float x = vertical ? (rng.NextDouble() < 0.5 ? f.rect.xMin : f.rect.xMax) + 1.5f : Mathf.Lerp(f.rect.xMin, f.rect.xMax, (float)rng.NextDouble());
                float z = vertical ? Mathf.Lerp(f.rect.yMin, f.rect.yMax, (float)rng.NextDouble()) : (rng.NextDouble() < 0.5 ? f.rect.yMin : f.rect.yMax) + 1.5f;
                if (Mathf.Abs(x - CountyRoadX) < 10f || FarmsteadRect.Contains(new Vector2(x, z))) continue;
                Add(x, z, 0.8f, 1.1f);
            }

        data.SetTreeInstances(instances.ToArray(), true);
        return instances.Count;
    }

    static GameObject LoadTree(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreePrefabFolder}/{name}.prefab");
        if (!prefab) throw new System.InvalidOperationException($"Missing tree prefab {name}; run the forest layout builder once to generate it.");
        return prefab;
    }

    // ---------- Scene objects ----------

    static void PlaceTruck(Transform envRoot)
    {
        var truck = envRoot.Find("Ford F100");
        if (!truck) return;
        Undo.RecordObject(truck, "Build Farm Layout");
        // The truck model is authored Z-up (X rotated 270), with its hood toward local +X.
        truck.SetPositionAndRotation(TruckPosition, Quaternion.Euler(0f, TruckYaw, 0f) * Quaternion.Euler(270f, 0f, 0f));
    }

    static void ConfigureTerrain(Terrain terrain)
    {
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 4f;
        terrain.basemapDistance = 150f;
        terrain.treeDistance = 450f;
        terrain.treeBillboardDistance = 70f;
        terrain.treeCrossFadeLength = 5f;
        terrain.treeMaximumFullLODCount = 60;
        terrain.detailObjectDistance = 80f;
        terrain.detailObjectDensity = 1f;
        terrain.shadowCastingMode = ShadowCastingMode.On;
        EditorUtility.SetDirty(terrain);
    }

    // ---------- Helpers ----------

    static Field? FieldAt(Vector2 p, out float edgeDistance)
    {
        foreach (var f in Fields)
        {
            if (!f.rect.Contains(p)) continue;
            edgeDistance = Mathf.Min(p.x - f.rect.xMin, f.rect.xMax - p.x, p.y - f.rect.yMin, f.rect.yMax - p.y);
            return f;
        }
        edgeDistance = 0f;
        return null;
    }

    /// +1 on the crown of a crop row, -1 in the furrow. One row spans 4 alphamap texels and 2 detail cells; the
    /// quarter-period offset keeps texel and cell centres off the zero crossings so rows paint cleanly.
    static float RowProfile(float x, float z, Field f)
    {
        float coord = f.rowsAlongZ ? x - _terrainOrigin.x : z - _terrainOrigin.z;
        return Mathf.Cos((coord / RowSpacing - 0.25f) * Mathf.PI * 2f);
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
}
#endif
