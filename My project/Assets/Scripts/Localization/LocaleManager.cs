using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

public static class LocaleManager
{
    public const string PrefsKey = "settings_locale";

    public static readonly string[] LocaleCodes =
    {
        "en", "ja", "ru", "zh-Hans", "tr", "pl", "fr", "es-419", "pt-BR"
    };

    public static readonly string[] LocaleNativeNames =
    {
        "English", "日本語", "Русский", "简体中文", "Türkçe",
        "Polski", "Français", "Español (LATAM)", "Português (Brasil)"
    };

    /// <summary>Native endonym for the language picker (e.g. Türkçe, not Turkish).</summary>
    public static string GetLocaleDisplayName(int index) =>
        index >= 0 && index < LocaleNativeNames.Length ? LocaleNativeNames[index] : "";

    public static void WhenReady(Action callback)
    {
        if (callback == null)
            return;

        var op = LocalizationSettings.InitializationOperation;
        if (op.IsDone)
            callback();
        else
            op.Completed += _ => callback();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap() => WhenReady(() =>
    {
        ApplySavedOrSystemLocale();
        RefreshAllUI();
    });

    public static void ApplySavedOrSystemLocale()
    {
        if (PlayerPrefs.HasKey(PrefsKey))
        {
            ApplyLocaleCode(PlayerPrefs.GetString(PrefsKey));
            return;
        }

        ApplyLocaleCode(MapSystemLanguage(Application.systemLanguage));
    }

    public static int GetCurrentLocaleIndex()
    {
        var code = LocalizationSettings.SelectedLocale != null
            ? LocalizationSettings.SelectedLocale.Identifier.Code
            : "en";

        for (int i = 0; i < LocaleCodes.Length; i++)
        {
            if (LocaleCodes[i] == code)
                return i;
        }

        return 0;
    }

    public static void SetLocaleByIndex(int index)
    {
        if (index < 0 || index >= LocaleCodes.Length)
            return;

        ApplyLocaleCode(LocaleCodes[index]);
        PlayerPrefs.SetString(PrefsKey, LocaleCodes[index]);
        PlayerPrefs.Save();
    }

    public static void ApplyLocaleCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            code = "en";

        var locales = LocalizationSettings.AvailableLocales.Locales;
        if (locales == null || locales.Count == 0)
            return;

        Locale match = null;
        for (int i = 0; i < locales.Count; i++)
        {
            if (locales[i].Identifier.Code == code)
            {
                match = locales[i];
                break;
            }
        }

        if (match == null)
        {
            for (int i = 0; i < locales.Count; i++)
            {
                if (locales[i].Identifier.Code == "en")
                {
                    match = locales[i];
                    break;
                }
            }
        }

        if (match != null)
            LocalizationSettings.SelectedLocale = match;
    }

    public static void RefreshAllUI()
    {
        foreach (var bootstrap in UnityEngine.Object.FindObjectsByType<UIFontBootstrap>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            bootstrap.ApplyFonts();

        foreach (var label in UnityEngine.Object.FindObjectsByType<LocalizedUILabel>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            label.Refresh();
    }

    static string MapSystemLanguage(SystemLanguage language) => language switch
    {
        SystemLanguage.Japanese => "ja",
        SystemLanguage.Russian => "ru",
        SystemLanguage.Chinese or SystemLanguage.ChineseSimplified => "zh-Hans",
        SystemLanguage.Turkish => "tr",
        SystemLanguage.Polish => "pl",
        SystemLanguage.French => "fr",
        SystemLanguage.Portuguese => "pt-BR",
        SystemLanguage.Spanish => "es-419",
        _ => "en"
    };
}
