using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

/// <summary>
/// Runtime helper for Unity Localization string tables.
/// </summary>
public static class LocalizationService
{
    public static event Action LocaleChanged;

    public static bool IsReady =>
        LocalizationSettings.InitializationOperation.IsDone &&
        LocalizationSettings.StringDatabase != null;

    public static string CurrentLocaleCode =>
        LocalizationSettings.SelectedLocale != null
            ? LocalizationSettings.SelectedLocale.Identifier.Code
            : "en";

    public static void EnsureInitialized(Action onReady)
    {
        if (IsReady)
        {
            onReady?.Invoke();
            return;
        }

        LocalizationSettings.InitializationOperation.Completed += _ => onReady?.Invoke();
    }

    public static bool TrySetLocale(string localeCode, bool forceNotify = false)
    {
        if (string.IsNullOrEmpty(localeCode))
            return false;

        var locale = FindLocale(localeCode);
        if (locale == null)
        {
            Debug.LogWarning($"[Localization] Locale not found: {localeCode}");
            return false;
        }

        bool alreadySelected = LocalizationSettings.SelectedLocale == locale;
        if (!alreadySelected)
            LocalizationSettings.SelectedLocale = locale;

        if (!alreadySelected || forceNotify)
            LocaleChanged?.Invoke();

        return true;
    }

    public static Locale FindLocale(string localeCode)
    {
        if (LocalizationSettings.AvailableLocales == null)
            return null;

        var locales = LocalizationSettings.AvailableLocales.Locales;
        if (locales == null)
            return null;

        for (int i = 0; i < locales.Count; i++)
        {
            var l = locales[i];
            if (l != null && string.Equals(l.Identifier.Code, localeCode, StringComparison.OrdinalIgnoreCase))
                return l;
        }

        return null;
    }

    public static int GetLocaleIndex(string localeCode)
    {
        for (int i = 0; i < LocalizationTables.SupportedLocaleCodes.Length; i++)
        {
            if (string.Equals(LocalizationTables.SupportedLocaleCodes[i], localeCode, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return 0;
    }

    public static string GetLocaleCodeFromIndex(int index)
    {
        index = Mathf.Clamp(index, 0, LocalizationTables.SupportedLocaleCodes.Length - 1);
        return LocalizationTables.SupportedLocaleCodes[index];
    }

    public static string Get(string tableName, string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (!IsReady)
            return key;

        try
        {
            var result = LocalizationSettings.StringDatabase.GetLocalizedString(tableName, key);
            return string.IsNullOrEmpty(result) ? key : result;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Localization] Get failed {tableName}/{key}: {ex.Message}");
            return key;
        }
    }

    public static string GetGameplay(string key) =>
        Get(LocalizationTables.Gameplay, key);

    public static string GetMainMenu(string key) =>
        Get(LocalizationTables.MainMenu, key);

    public static string Format(string tableName, string key, IReadOnlyDictionary<string, object> args)
    {
        var template = Get(tableName, key);
        if (args == null || args.Count == 0)
            return template;

        var result = template;
        foreach (var pair in args)
            result = result.Replace("{" + pair.Key + "}", pair.Value?.ToString() ?? string.Empty);
        return result;
    }

    public static string FormatGameplay(string key, IReadOnlyDictionary<string, object> args) =>
        Format(LocalizationTables.Gameplay, key, args);

    public static string FormatGameplay(string key, string argName, object argValue) =>
        FormatGameplay(key, new Dictionary<string, object> { { argName, argValue } });

    public static string FormatGameplay(string key, params (string name, object value)[] args)
    {
        if (args == null || args.Length == 0)
            return GetGameplay(key);

        var dict = new Dictionary<string, object>(args.Length);
        foreach (var t in args)
            dict[t.name] = t.value;
        return FormatGameplay(key, dict);
    }
}
