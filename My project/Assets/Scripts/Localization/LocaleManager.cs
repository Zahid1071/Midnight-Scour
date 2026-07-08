using System;
using System.Collections;
using System.Collections.Generic;
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

    /// <summary>
    /// Runs <paramref name="callback"/> once localization initialization is complete.
    /// Always deferred onto a normal Unity update tick via <see cref="Pump"/> - never invoked
    /// synchronously from inside InitializationOperation.Completed. That event fires from within
    /// Addressables' own ResourceManager.Update(), and localization calls that can themselves
    /// force a synchronous WaitForCompletion (Loc.Get, or reading LocalizationSettings.SelectedLocale
    /// before init) would otherwise try to reenter ResourceManager.Update() and crash with
    /// "Reentering the Update method is not allowed."
    /// </summary>
    public static void WhenReady(Action callback)
    {
        if (callback == null)
            return;

        Pump.Enqueue(callback);
    }

    /// <summary>Hidden runtime object that flushes queued WhenReady callbacks from a safe context.</summary>
    class Pump : MonoBehaviour
    {
        static Pump _instance;
        static readonly List<Action> _pending = new List<Action>();

        static Pump Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[LocaleManager.Pump]") { hideFlags = HideFlags.HideInHierarchy };
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<Pump>();
                }
                return _instance;
            }
        }

        public static void Enqueue(Action callback)
        {
            _pending.Add(callback);
            if (_pending.Count == 1)
                Instance.StartCoroutine(FlushWhenReady());
        }

        static IEnumerator FlushWhenReady()
        {
            var op = LocalizationSettings.InitializationOperation;
            while (!op.IsDone)
                yield return null;

            // Always yield at least once more, so even if IsDone was already true when this
            // coroutine started, callbacks still run on a fresh tick rather than in whatever
            // context queued them.
            yield return null;

            var callbacks = _pending.ToArray();
            _pending.Clear();

            foreach (var cb in callbacks)
            {
                try { cb(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
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
