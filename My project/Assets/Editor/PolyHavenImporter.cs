#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a Poly Haven 1k FBX download (model + textures/ folder) into URP Lit materials:
/// base colour, OpenGL normal map and a packed metallic/smoothness map (R = metal, A = 1 - roughness).
/// Parts named *_glass become transparent; parts with an alpha/opacity map use alpha clipping.
/// Many of these FBX roots carry a -90 X rotation, so instances must keep the root's own rotation.
/// </summary>
public static class PolyHavenImporter
{
    public const string Root = "Assets/Props/PolyHaven";

    [MenuItem("Tools/Environment/Setup Poly Haven Materials")]
    static void SetupAllMenu() => Debug.Log(SetupAll(false));

    public static string SetupAll(bool overwrite)
    {
        int n = 0;
        foreach (var dir in Directory.GetDirectories(Root))
            if (Setup(dir.Replace('\\', '/'), overwrite)) n++;
        return $"Poly Haven: set up {n} models.";
    }

    /// Returns false when the folder was already set up (remapped) and overwrite is off.
    public static bool Setup(string dir, bool overwrite)
    {
        var fbxPath = Directory.GetFiles(dir, "*.fbx").Select(p => p.Replace('\\', '/')).FirstOrDefault();
        if (fbxPath == null) return false;
        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (!overwrite && importer.GetExternalObjectMap().Count > 0) return false;

        var sets = TextureSets(dir + "/textures");
        var slots = AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath).OfType<Material>().Select(m => m.name).ToList();
        string id = Path.GetFileName(dir);

        foreach (var slot in slots)
        {
            if (!sets.TryGetValue(slot, out var maps))
            {
                // Main slot named differently from its textures (e.g. old_tyre -> tire_r13_01, Barrel_01 -> Barrel_01_explosive).
                var key = sets.Keys.FirstOrDefault(k => k == id) ??
                          sets.Keys.Where(k => k.StartsWith(id, System.StringComparison.OrdinalIgnoreCase) && !slots.Contains(k)).OrderBy(k => k.Length).FirstOrDefault();
                maps = key != null ? sets[key] : new Dictionary<string, string>();
            }
            var mat = BuildMaterial(dir, slot, maps);
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), mat);
        }

        importer.SaveAndReimport();
        return true;
    }

    static Dictionary<string, Dictionary<string, string>> TextureSets(string texDir)
    {
        var sets = new Dictionary<string, Dictionary<string, string>>();
        if (!Directory.Exists(texDir)) return sets;
        string[] mapTokens = { "nor_gl", "diff", "roughness", "rough", "metallic", "metal", "alpha", "opacity" };
        foreach (var file in Directory.GetFiles(texDir))
        {
            if (file.EndsWith(".meta")) continue;
            var name = Path.GetFileNameWithoutExtension(file);
            int res = name.LastIndexOf("_1k");
            if (res < 0) continue;
            var stem = name.Substring(0, res);
            foreach (var token in mapTokens)
            {
                if (!stem.EndsWith("_" + token)) continue;
                var prefix = stem.Substring(0, stem.Length - token.Length - 1);
                if (!sets.TryGetValue(prefix, out var maps)) sets[prefix] = maps = new Dictionary<string, string>();
                string kind = token == "roughness" ? "rough" : token == "metallic" ? "metal" : token == "opacity" ? "alpha" : token;
                maps[kind] = file.Replace('\\', '/');
                break;
            }
        }
        return sets;
    }

    static Material BuildMaterial(string dir, string slot, Dictionary<string, string> maps)
    {
        var path = $"{dir}/{slot}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = slot };
            AssetDatabase.CreateAsset(mat, path);
        }

        bool glass = slot.EndsWith("_glass");
        maps.TryGetValue("diff", out var diff);
        maps.TryGetValue("nor_gl", out var nor);
        maps.TryGetValue("alpha", out var alpha);

        if (nor != null) ConfigureTexture(nor, TextureImporterType.NormalMap, false);
        if (diff != null) ConfigureTexture(diff, TextureImporterType.Default, true);

        var baseTex = diff != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(diff) : null;
        if (alpha != null && !glass)
            baseTex = PackBaseAlpha(dir, slot, diff, alpha);
        mat.SetTexture("_BaseMap", baseTex);
        mat.SetColor("_BaseColor", glass ? new Color(0.85f, 0.88f, 0.9f, 0.18f) : Color.white);

        if (nor != null)
        {
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nor));
            mat.EnableKeyword("_NORMALMAP");
        }

        if (glass)
        {
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.92f);
            SetTransparent(mat);
        }
        else
        {
            var mask = PackMetalGloss(dir, slot, maps);
            if (mask)
            {
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else
            {
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Smoothness", 0.25f);
            }
            if (alpha != null)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
            }
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    static void SetTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static void ConfigureTexture(string path, TextureImporterType type, bool srgb)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null || (ti.textureType == type && ti.sRGBTexture == srgb)) return;
        ti.textureType = type;
        ti.sRGBTexture = srgb;
        ti.SaveAndReimport();
    }

    /// Metallic (R) + smoothness (A) in URP's layout. Null when the set has no roughness or metal map.
    static Texture2D PackMetalGloss(string dir, string slot, Dictionary<string, string> maps)
    {
        maps.TryGetValue("rough", out var rough);
        maps.TryGetValue("metal", out var metal);
        if (rough == null && metal == null) return null;
        var r = rough != null ? ReadLinear(rough) : null;
        var m = metal != null ? ReadLinear(metal) : null;
        int w = r != null ? r.width : m.width, h = r != null ? r.height : m.height;
        var rp = r != null ? Resize(r, w, h).GetPixels() : null;
        var mp = m != null ? Resize(m, w, h).GetPixels() : null;
        var outPx = new Color[w * h];
        for (int i = 0; i < outPx.Length; i++)
            outPx[i] = new Color(mp != null ? mp[i].r : 0f, 0f, 0f, rp != null ? 1f - rp[i].r : 0.4f);
        return SavePng($"{dir}/textures/{slot}_metalgloss.png", outPx, w, h, false);
    }

    static Texture2D PackBaseAlpha(string dir, string slot, string diff, string alpha)
    {
        var a = ReadLinear(alpha);
        int w = a.width, h = a.height;
        var ap = a.GetPixels();
        Color[] dp = null;
        if (diff != null) dp = Resize(ReadSrgb(diff), w, h).GetPixels();
        var outPx = new Color[w * h];
        for (int i = 0; i < outPx.Length; i++)
        {
            var c = dp != null ? dp[i] : Color.white;
            c.a = ap[i].r;
            outPx[i] = c;
        }
        return SavePng($"{dir}/textures/{slot}_basealpha.png", outPx, w, h, true);
    }

    static Texture2D ReadLinear(string path)
    {
        ConfigureTexture(path, TextureImporterType.Default, false);
        return Read(AssetDatabase.LoadAssetAtPath<Texture2D>(path), RenderTextureReadWrite.Linear);
    }

    static Texture2D ReadSrgb(string path) => Read(AssetDatabase.LoadAssetAtPath<Texture2D>(path), RenderTextureReadWrite.sRGB);

    static Texture2D Read(Texture src, RenderTextureReadWrite mode)
    {
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, mode);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false, mode == RenderTextureReadWrite.Linear);
        tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex;
    }

    static Texture2D Resize(Texture2D tex, int w, int h) => tex.width == w && tex.height == h ? tex : ScaleTo(tex, w, h);

    static Texture2D ScaleTo(Texture2D tex, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        outTex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        outTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return outTex;
    }

    static Texture2D SavePng(string path, Color[] px, int w, int h, bool srgb)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.sRGBTexture = srgb;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = srgb;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
#endif
