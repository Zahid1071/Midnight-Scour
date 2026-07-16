using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>
/// Picks one TMP font per locale so text never mixes glyphs from fallback chains.
/// </summary>
public static class UIFont
{
    const string PixelFontNameToken = "Pixelify";
    const string ChineseFontNameToken = "Cubic_11";
    const string JapaneseFontNameToken = "DotGothic";

    static TMP_FontAsset _pixel;
    static TMP_FontAsset _chinese;
    static TMP_FontAsset _japanese;
    static TMP_FontAsset _russian;

    public static void RegisterPixel(TMP_FontAsset font)
    {
        if (font != null)
            _pixel = font;
    }

    public static void RegisterChinese(TMP_FontAsset font)
    {
        if (font != null)
            _chinese = font;
    }

    public static void RegisterJapanese(TMP_FontAsset font)
    {
        if (font != null)
            _japanese = font;
    }

    public static void RegisterRussian(TMP_FontAsset font)
    {
        if (font != null)
            _russian = font;
    }

    static TMP_FontAsset ResolveFont(string nameToken)
    {
        foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (asset != null && asset.name.Contains(nameToken))
                return asset;
        }

        return null;
    }

    static TMP_FontAsset Pixel => _pixel ??= ResolveFont(PixelFontNameToken);
    static TMP_FontAsset Chinese => _chinese ??= ResolveFont(ChineseFontNameToken);
    static TMP_FontAsset Japanese => _japanese ??= ResolveFont(JapaneseFontNameToken);

    /// <summary>Cubic_11 — used for CJK UI and digit-only skippers that should stay consistent.</summary>
    public static TMP_FontAsset ChineseOrNull => Chinese;

    public static TMP_FontAsset ForLocale(string code)
    {
        if (string.IsNullOrEmpty(code))
            code = "en";

        return code switch
        {
            "zh-Hans" => Chinese ?? Pixel,
            "ja" => Japanese ?? Pixel,
            "ru" => _russian ?? Chinese ?? Pixel,
            _ => Pixel
        };
    }

    public static TMP_FontAsset ForCurrentLocale()
    {
        // Reading SelectedLocale before initialization is done forces Unity's Localization
        // package to synchronously complete InitializationOperation right here (WaitForCompletion).
        // If that happens while we're already nested inside another Addressables completion
        // callback, it can crash with "Reentering the Update method is not allowed" - so we only
        // ever read it once init has actually finished. Callers refresh again later via
        // LocaleManager.WhenReady once it's safe.
        var code = "en";
        if (LocalizationSettings.InitializationOperation.IsDone && LocalizationSettings.SelectedLocale != null)
            code = LocalizationSettings.SelectedLocale.Identifier.Code;

        return ForLocale(code);
    }

    public static void Apply(TextMeshProUGUI label)
    {
        if (label == null)
            return;

        var font = ForCurrentLocale();
        if (font == null)
            return;

        if (label.font == font && label.fontSharedMaterial == font.material)
            return;

        label.font = font;
        if (font.material != null)
            label.fontSharedMaterial = font.material;
    }

    public static void ApplyTree(Transform root)
    {
        if (root == null)
            return;

        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            Apply(label);
    }

    public static void SetText(TextMeshProUGUI label, string text)
    {
        if (label == null)
            return;

        Apply(label);
        label.text = text;
        label.ForceMeshUpdate();
    }
}
