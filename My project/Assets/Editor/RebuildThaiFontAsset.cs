#if UNITY_EDITOR
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Builds a clean Dynamic Thai SDF from the static Regular TTF.
/// The variable-font SDF only had ~11 glyphs and made Thai UI look like pixel garbage.
/// </summary>
public static class RebuildThaiFontAsset
{
    const string RegularTtf = "Assets/TextMesh Pro/Fonts/NotoSansThai-Regular.ttf";
    const string OutSdf = "Assets/TextMesh Pro/Fonts/NotoSansThai-Regular SDF.asset";
    const string LegacyBrokenSdf =
        "Assets/TextMesh Pro/Fonts/NotoSansThai-VariableFont_wdth,wght SDF.asset";
    const string PrefKey = "MidnightScour.ThaiFontRebuilt.v4";

    [InitializeOnLoadMethod]
    static void EnsureOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutSdf);
            bool needsRebuild = existing == null
                || existing.characterTable == null
                || existing.characterTable.Count < 80
                || !EditorPrefs.GetBool(PrefKey, false);

            if (needsRebuild)
                Rebuild();
        };
    }

    [MenuItem("Tools/Localization/Rebuild Thai Font Asset")]
    public static void Rebuild()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(RegularTtf);
        if (font == null)
        {
            Debug.LogError($"[RebuildThaiFontAsset] Missing {RegularTtf}");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutSdf) != null)
            AssetDatabase.DeleteAsset(OutSdf);

        var fontAsset = TMP_FontAsset.CreateFontAsset(
            font,
            72,
            6,
            GlyphRenderMode.SDFAA,
            2048,
            2048,
            AtlasPopulationMode.Dynamic,
            enableMultiAtlasSupport: true);

        if (fontAsset == null)
        {
            Debug.LogError("[RebuildThaiFontAsset] CreateFontAsset failed.");
            return;
        }

        AssetDatabase.CreateAsset(fontAsset, OutSdf);
        if (fontAsset.material != null)
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        if (fontAsset.atlasTextures != null)
        {
            foreach (var tex in fontAsset.atlasTextures)
            {
                if (tex != null)
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
            }
        }

        string charset = BuildCharset();
        fontAsset.TryAddCharacters(charset, out var missing);
        if (!string.IsNullOrEmpty(missing))
            Debug.LogWarning($"[RebuildThaiFontAsset] Missing {missing.Length} chars (first 40): {missing.Substring(0, Mathf.Min(40, missing.Length))}");

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(OutSdf, ImportAssetOptions.ForceUpdate);

        fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutSdf);
        WirePrefabs(fontAsset);
        UIFont.RegisterThai(fontAsset);

        // Stop ResolveFont from picking the broken variable SDF first.
        DisableLegacyBrokenSdf();

        EditorPrefs.SetBool(PrefKey, true);
        int count = fontAsset != null && fontAsset.characterTable != null
            ? fontAsset.characterTable.Count
            : 0;
        Debug.Log($"[RebuildThaiFontAsset] Ready: {OutSdf} with {count} characters.");
    }

    static string BuildCharset()
    {
        var sb = new StringBuilder(256);
        for (int c = 0x20; c <= 0x7E; c++)
            sb.Append((char)c);
        sb.Append('\u00A0');
        // Thai block used by UI copy
        for (int c = 0x0E01; c <= 0x0E3A; c++)
            sb.Append((char)c);
        for (int c = 0x0E40; c <= 0x0E4E; c++)
            sb.Append((char)c);
        sb.Append('\u2013').Append('\u2014').Append('\u2026');
        return sb.ToString();
    }

    static void DisableLegacyBrokenSdf()
    {
        var legacy = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LegacyBrokenSdf);
        if (legacy == null)
            return;

        // Rename so UIFont token "NotoSansThai-Regular" wins; keep file for reference.
        // Soft-disable by clearing source so it can't be used dynamically.
        var so = new SerializedObject(legacy);
        var pop = so.FindProperty("m_AtlasPopulationMode");
        if (pop != null)
            pop.intValue = (int)AtlasPopulationMode.Static;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(legacy);
    }

    static void WirePrefabs(TMP_FontAsset fontAsset)
    {
        if (fontAsset == null)
            return;

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[]
        {
            "Assets/Prefabs/UI",
            "Assets/UI"
        });

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;

            foreach (var bootstrap in root.GetComponentsInChildren<UIFontBootstrap>(true))
            {
                var so = new SerializedObject(bootstrap);
                var prop = so.FindProperty("thaiFont");
                if (prop == null)
                    continue;

                if (prop.objectReferenceValue != fontAsset)
                {
                    prop.objectReferenceValue = fontAsset;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, path);

            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
#endif
