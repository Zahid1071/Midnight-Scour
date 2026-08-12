using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>
/// Attach to a Canvas root. Applies the locale-appropriate UI font to all TMP labels.
/// </summary>
[DisallowMultipleComponent]
public class UIFontBootstrap : MonoBehaviour
{
    [Header("Locale fonts")]
    [SerializeField] TMP_FontAsset pixelFont;
    [SerializeField] TMP_FontAsset chineseFont;
    [SerializeField] TMP_FontAsset japaneseFont;
    [SerializeField] TMP_FontAsset russianFont;
    [SerializeField] TMP_FontAsset thaiFont;

    [Header("When to apply")]
    [SerializeField] bool applyOnAwake = true;
    [SerializeField] bool applyOnEnable = true;

    void Awake()
    {
        RegisterFonts();

        if (applyOnAwake)
        {
            if (LocaleManager.IsReady)
                ApplyFonts();
            else
                LocaleManager.WhenReady(ApplyFonts);
        }
    }

    void OnEnable()
    {
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;

        if (applyOnEnable)
        {
            if (LocaleManager.IsReady)
                ApplyFonts();
            else
                LocaleManager.WhenReady(ApplyFonts);
        }
    }

    void OnDisable() => LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

    void OnLocaleChanged(UnityEngine.Localization.Locale _) => ApplyFonts();

    public void ApplyFonts()
    {
        if (this == null)
            return;

        RegisterFonts();
        UIFont.ApplyTree(transform);
    }

    void RegisterFonts()
    {
        UIFont.RegisterPixel(pixelFont);
        UIFont.RegisterChinese(chineseFont);
        UIFont.RegisterJapanese(japaneseFont);
        UIFont.RegisterRussian(russianFont);
        UIFont.RegisterThai(thaiFont);
    }
}
