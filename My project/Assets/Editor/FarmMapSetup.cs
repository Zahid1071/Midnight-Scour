#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Wires up the hand-drawn farm map: texture import, localized place names, the full-screen map view on the gameplay
/// canvas (M once found) and the paper pinned to the cellar exit door. Re-running replaces what it placed.
/// The image itself is drawn by Tools/draw_farm_map.py.
/// </summary>
public static class FarmMapSetup
{
    const string TexturePath = "Assets/Art/Images/Farm Map.png";
    const string MaterialFolder = "Assets/Art/Materials";
    const string PaperMaterialPath = MaterialFolder + "/Farm Map Paper.mat";
    const string PinMaterialPath = MaterialFolder + "/Farm Map Pin.mat";
    const string ViewName = "Farm Map View";
    const string PickupName = "Farm Map Pickup";
    const string DoorPath = "Door Basement Exit/Door_mesh_A_L";

    // On the stair side of the exit door, at eye height for someone climbing the cellar steps.
    static readonly Vector3 PaperCentre = new Vector3(-7.475f, 1.3f, 24.084f);
    static readonly Vector2 PaperSize = new Vector2(0.4f, 0.29f);
    const float PaperTilt = 2.5f;

    static readonly Color Ink = new Color(0.16f, 0.11f, 0.07f, 1f);

    struct Label
    {
        public string key;
        public Vector3 world;
        public Vector2 pivot;
        public float angle;
        public Label(string key, float x, float z, Vector2 pivot, float angle) { this.key = key; world = new Vector3(x, 0f, z); this.pivot = pivot; this.angle = angle; }
    }

    // Anchored just outside each drawing so the name never covers it.
    static readonly Label[] Labels =
    {
        new Label("map.small_house", 75.5f, 29.5f, new Vector2(0.5f, 1f), -2f),
        new Label("map.farmhouse", -13f, 69.8f, new Vector2(0.5f, 1f), 2f),
        new Label("map.basement", 1.5f, 5f, new Vector2(0.5f, 1f), -2f),
        new Label("map.truck", -58f, 44.6f, new Vector2(0f, 0.5f), 3f),
    };

    static readonly string[] Locales = { "en", "zh-Hans", "fr", "ja", "pl", "pt-BR", "ru", "tr", "es-419", "th" };

    static readonly Dictionary<string, string[]> Strings = new Dictionary<string, string[]>
    {
        { "map.small_house", new[] { "Small House", "小屋", "Petite maison", "小さな家", "Mały dom", "Casa pequena", "Маленький дом", "Küçük ev", "Casa pequeña", "บ้านหลังเล็ก" } },
        { "map.farmhouse", new[] { "Farmhouse", "农舍", "Ferme", "農家", "Dom na farmie", "Casa da fazenda", "Фермерский дом", "Çiftlik evi", "Casa de la granja", "บ้านไร่" } },
        { "map.basement", new[] { "Basement", "地下室", "Sous-sol", "地下室", "Piwnica", "Porão", "Подвал", "Bodrum", "Sótano", "ห้องใต้ดิน" } },
        { "map.truck", new[] { "Truck", "皮卡", "Pick-up", "トラック", "Pikap", "Caminhonete", "Пикап", "Kamyonet", "Camioneta", "รถกระบะ" } },
        { "map.pickup", new[]
            {
                "A hand-drawn map of the farm. Press M to look at it again.",
                "一张手绘的农场地图。按 M 再次查看。",
                "Une carte de la ferme dessinée à la main. Appuyez sur M pour la consulter à nouveau.",
                "手描きの農場の地図。Mキーでもう一度見る。",
                "Ręcznie narysowana mapa farmy. Naciśnij M, aby spojrzeć na nią ponownie.",
                "Um mapa da fazenda desenhado à mão. Pressione M para olhar de novo.",
                "Нарисованная от руки карта фермы. Нажмите M, чтобы посмотреть снова.",
                "Çiftliğin elle çizilmiş bir haritası. Tekrar bakmak için M'ye bas.",
                "Un mapa de la granja dibujado a mano. Presiona M para volver a mirarlo.",
                "แผนที่ฟาร์มที่วาดด้วยมือ กด M เพื่อดูอีกครั้ง",
            }
        },
    };

    [MenuItem("Tools/Environment/Place Farm Map")]
    static void PlaceMenu() => Debug.Log(Place());

    public static string Place()
    {
        var sprite = ImportTexture();
        if (!sprite) return $"Missing {TexturePath}; run Tools/draw_farm_map.py first.";
        string strings = EnsureStrings();

        var readPanel = Object.FindFirstObjectByType<ReadPanelUI>(FindObjectsInactive.Include);
        if (!readPanel) return "No ReadPanelUI in the scene to put the map view next to.";
        var letter = Object.FindFirstObjectByType<ReadableLetter>(FindObjectsInactive.Include);
        var paperSound = letter ? new SerializedObject(letter).FindProperty("interactSFX").objectReferenceValue as AudioClip : null;

        BuildView(readPanel, sprite, paperSound);
        string pickup = PlacePickup(sprite.texture, paperSound);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(readPanel.gameObject.scene);
        return $"Farm map placed. {strings} {pickup}";
    }

    static Sprite ImportTexture()
    {
        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        if (!importer) return null;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(TexturePath);
    }

    static string EnsureStrings()
    {
        var collection = LocalizationEditorSettings.GetStringTableCollection(Loc.Table);
        if (!collection) return "No GameStrings table collection found.";
        var shared = collection.SharedData;
        var ids = new List<string>();
        foreach (var kv in Strings)
        {
            var entry = shared.GetEntry(kv.Key) ?? shared.AddKey(kv.Key);
            ids.Add($"{kv.Key}={entry.Id}");
            foreach (var table in collection.StringTables)
            {
                int i = System.Array.IndexOf(Locales, table.LocaleIdentifier.Code);
                if (i < 0) continue;
                table.AddEntry(entry.Id, kv.Value[i]);
                EditorUtility.SetDirty(table);
            }
        }
        EditorUtility.SetDirty(shared);
        AssetDatabase.SaveAssets();
        return "Strings: " + string.Join(", ", ids) + ".";
    }

    static void BuildView(ReadPanelUI readPanel, Sprite sprite, AudioClip openSound)
    {
        var canvas = readPanel.transform.parent;
        var old = canvas.Find(ViewName);
        if (old) Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject(ViewName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Place Farm Map");
        root.transform.SetParent(canvas, false);
        root.transform.SetSiblingIndex(readPanel.transform.GetSiblingIndex() + 1);
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

        var panel = new GameObject("Panel Root", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        Stretch(panel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.92f);

        var frame = new GameObject("Map Frame", typeof(RectTransform));
        frame.transform.SetParent(panel.transform, false);
        Stretch(frame.GetComponent<RectTransform>(), new Vector2(0.03f, 0.09f), new Vector2(0.97f, 0.97f));

        var map = new GameObject("Map", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
        map.transform.SetParent(frame.transform, false);
        Stretch(map.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
        var image = map.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        var fitter = map.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = sprite.rect.width / sprite.rect.height;

        foreach (var l in Labels)
        {
            var go = new GameObject("Label " + l.key, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(map.transform, false);
            var rt = go.GetComponent<RectTransform>();
            Vector2 at = FarmMapView.ToMap(l.world);
            rt.anchorMin = rt.anchorMax = at;
            rt.pivot = l.pivot;
            rt.sizeDelta = new Vector2(420f, 50f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, l.angle);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = Strings[l.key][0];
            text.fontSize = 34f;
            text.color = Ink;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.alignment = l.pivot.x < 0.25f ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            if (l.pivot.y > 0.75f) text.verticalAlignment = VerticalAlignmentOptions.Top;
            var localized = go.AddComponent<LocalizedUILabel>();
            var so = new SerializedObject(localized);
            so.FindProperty("entryKey").stringValue = l.key;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        UIFont.ApplyTree(map.transform);

        // Same "press any key" footer as the letters.
        var readRoot = new SerializedObject(readPanel).FindProperty("panelRoot").objectReferenceValue as GameObject;
        Transform footer = null;
        if (readRoot)
            foreach (var t in readRoot.GetComponentsInChildren<Transform>(true))
                if (t.name == "Press any key to close") { footer = t; break; }
        if (footer)
        {
            var copy = Object.Instantiate(footer.gameObject, panel.transform, false);
            copy.name = footer.name;
            var rt = copy.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.045f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(900f, 50f);
            if (copy.TryGetComponent(out TextMeshProUGUI footerText))
                footerText.alignment = TextAlignmentOptions.Center;
        }

        var view = root.AddComponent<FarmMapView>();
        var vso = new SerializedObject(view);
        vso.FindProperty("panelRoot").objectReferenceValue = panel;
        vso.FindProperty("openSFX").objectReferenceValue = openSound;
        vso.ApplyModifiedPropertiesWithoutUndo();
        panel.SetActive(false);
    }

    static string PlacePickup(Texture2D texture, AudioClip sound)
    {
        foreach (var old in Object.FindObjectsByType<FarmMapPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Undo.DestroyObjectImmediate(old.gameObject);

        var doorRoot = GameObject.Find(DoorPath.Split('/')[0]);
        var door = doorRoot ? doorRoot.transform.Find(DoorPath.Substring(DoorPath.IndexOf('/') + 1)) : null;
        if (!door) return $"No {DoorPath} found; map pickup not placed.";

        var root = new GameObject(PickupName);
        Undo.RegisterCreatedObjectUndo(root, "Place Farm Map");
        // Parented to the door leaf so it swings with the door.
        root.transform.SetParent(door, false);
        root.transform.SetPositionAndRotation(PaperCentre, Quaternion.Euler(0f, 0f, PaperTilt));
        root.layer = LayerMask.NameToLayer("Interactable_Static");
        var box = root.AddComponent<BoxCollider>();
        Vector3 inv = new Vector3(1f / root.transform.lossyScale.x, 1f / root.transform.lossyScale.y, 1f / root.transform.lossyScale.z);
        box.size = Vector3.Scale(new Vector3(PaperSize.x, PaperSize.y, 0.02f), inv);
        box.center = Vector3.zero;
        var pickup = root.AddComponent<FarmMapPickup>();
        var pso = new SerializedObject(pickup);
        pso.FindProperty("pickupSFX").objectReferenceValue = sound;
        pso.ApplyModifiedPropertiesWithoutUndo();

        // The quad faces -z, toward the stairs.
        var paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
        paper.name = "Paper";
        Object.DestroyImmediate(paper.GetComponent<Collider>());
        paper.transform.SetParent(root.transform, false);
        paper.transform.localScale = Vector3.Scale(new Vector3(PaperSize.x, PaperSize.y, 1f), inv);
        var paperRenderer = paper.GetComponent<MeshRenderer>();
        paperRenderer.sharedMaterial = PaperMaterial(texture);
        paperRenderer.shadowCastingMode = ShadowCastingMode.Off;

        var pinMat = PinMaterial();
        foreach (var corner in new[] { new Vector2(-0.44f, 0.42f), new Vector2(0.44f, 0.42f) })
        {
            var pin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pin.name = "Pin";
            Object.DestroyImmediate(pin.GetComponent<Collider>());
            pin.transform.SetParent(root.transform, false);
            pin.transform.localPosition = Vector3.Scale(new Vector3(corner.x * PaperSize.x, corner.y * PaperSize.y, -0.004f), inv);
            pin.transform.localScale = inv * 0.014f;
            pin.GetComponent<MeshRenderer>().sharedMaterial = pinMat;
            pin.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        return $"Map pinned to {door.name} at {PaperCentre}.";
    }

    static Material PaperMaterial(Texture2D texture)
    {
        var m = LoadOrCreate(PaperMaterialPath);
        m.SetTexture("_BaseMap", texture);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.12f);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material PinMaterial()
    {
        var m = LoadOrCreate(PinMaterialPath);
        m.SetColor("_BaseColor", new Color(0.45f, 0.05f, 0.04f));
        m.SetFloat("_Smoothness", 0.7f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material LoadOrCreate(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m) return m;
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Art", "Materials");
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
#endif
