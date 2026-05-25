using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Updates TMP labels under the Canvas from the Main Menu string table when locale changes.
/// Maps inspector English labels (and common variants) to localization keys.
/// </summary>
public class MainMenuTextLocalizer : MonoBehaviour
{
    // Case-sensitive: "Settings" (button) and "SETTINGS" (panel title) are distinct keys.
    private static readonly Dictionary<string, string> LabelToKey =
        new(StringComparer.Ordinal)
        {
            { "Continue", LocalizationTables.MainMenuKeys.BtnContinue },
            { "New Game", LocalizationTables.MainMenuKeys.BtnNewGame },
            { "Settings", LocalizationTables.MainMenuKeys.BtnSettings },
            { "SETTINGS", LocalizationTables.MainMenuKeys.SettingsTitle },
            { "Credits", LocalizationTables.MainMenuKeys.BtnCredits },
            { "Quit Game", LocalizationTables.MainMenuKeys.BtnQuit },
            { "Quit", LocalizationTables.MainMenuKeys.DialogQuitTitle },
            { "Display Mode", LocalizationTables.MainMenuKeys.SettingsDisplayMode },
            { "Brightness", LocalizationTables.MainMenuKeys.SettingsBrightness },
            { "Master Volume", LocalizationTables.MainMenuKeys.SettingsMasterVolume },
            { "Music Volume", LocalizationTables.MainMenuKeys.SettingsMusicVolume },
            { "Music Sound", LocalizationTables.MainMenuKeys.SettingsMusicVolume },
            { "SFX Volume", LocalizationTables.MainMenuKeys.SettingsSfxVolume },
            { "SFX Sound", LocalizationTables.MainMenuKeys.SettingsSfxVolume },
            { "Mouse Sensitivity", LocalizationTables.MainMenuKeys.SettingsMouseSensitivity },
            { "VSync", LocalizationTables.MainMenuKeys.SettingsVsync },
            { "Auto-Save", LocalizationTables.MainMenuKeys.SettingsAutosave },
            { "Choose Value", LocalizationTables.MainMenuKeys.SettingsChooseValue },
            { "Language", LocalizationTables.MainMenuKeys.SettingsLanguage },
            { "Resolution", LocalizationTables.MainMenuKeys.SettingsResolution },
            { "Quality", LocalizationTables.MainMenuKeys.SettingsQuality },
            { "Display", LocalizationTables.MainMenuKeys.TabDisplay },
            { "Audio", LocalizationTables.MainMenuKeys.TabAudio },
            { "Controls", LocalizationTables.MainMenuKeys.TabControls },
            { "Gameplay", LocalizationTables.MainMenuKeys.TabGameplay },
            { "Sound", LocalizationTables.MainMenuKeys.TabSound },
            { "General", LocalizationTables.MainMenuKeys.TabGeneral },
            { "Apply", LocalizationTables.MainMenuKeys.BtnApply },
            { "Back", LocalizationTables.MainMenuKeys.BtnBack },
            { "Default", LocalizationTables.MainMenuKeys.BtnDefault },
            { "Defualt", LocalizationTables.MainMenuKeys.BtnDefault },
            { "Resume", LocalizationTables.MainMenuKeys.BtnResume },
            { "Contınue", LocalizationTables.MainMenuKeys.BtnContinue },
            { "QUIT", LocalizationTables.MainMenuKeys.BtnQuit },
            { "sETTINGS", LocalizationTables.MainMenuKeys.BtnSettings },
            { "Save Game", LocalizationTables.MainMenuKeys.BtnSaveGame },
            { "PAUSE", LocalizationTables.MainMenuKeys.PauseTitle },
            { "Pause", LocalizationTables.MainMenuKeys.PauseTitle },
        };

    private readonly List<(TextMeshProUGUI tmp, string key)> _bindings = new();
    private readonly HashSet<string> _languageDisplayNames = new(
        LocalizationTables.LanguageDisplayNames, StringComparer.Ordinal);

    private void OnEnable()
    {
        LocalizationService.LocaleChanged += OnLocaleChanged;
        LocalizationService.EnsureInitialized(RebuildAndRefresh);
    }

    private void OnDisable()
    {
        LocalizationService.LocaleChanged -= OnLocaleChanged;
    }

    private void Start() => RebuildAndRefresh();

    private void OnLocaleChanged() => RefreshLocalizedText();

    /// <summary>Scan labels once (English inspector text) and apply the current locale.</summary>
    public void RebuildAndRefresh()
    {
        if (!LocalizationService.IsReady)
        {
            LocalizationService.EnsureInitialized(RebuildAndRefresh);
            return;
        }

        if (_bindings.Count == 0)
            BuildBindings();

        RefreshAll();
    }

    /// <summary>Re-apply string table to existing bindings (safe after locale changes).</summary>
    public void RefreshLocalizedText()
    {
        if (!LocalizationService.IsReady)
        {
            LocalizationService.EnsureInitialized(RefreshLocalizedText);
            return;
        }

        if (_bindings.Count == 0)
            BuildBindings();

        RefreshAll();
    }

    private void BuildBindings()
    {
        _bindings.Clear();
        var tmps = GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var tmp in tmps)
        {
            if (tmp == null || ShouldSkip(tmp))
                continue;

            string label = NormalizeLabel(tmp.text);
            if (string.IsNullOrEmpty(label))
                continue;

            if (LabelToKey.TryGetValue(label, out string key))
                _bindings.Add((tmp, key));
        }
    }

    private void RefreshAll()
    {
        foreach (var (tmp, key) in _bindings)
        {
            if (tmp != null)
                tmp.text = LocalizationService.GetMainMenu(key);
        }
    }

    private bool ShouldSkip(TextMeshProUGUI tmp)
    {
        if (string.IsNullOrWhiteSpace(tmp.text))
            return true;

        var selector = tmp.GetComponentInParent<SettingsSelector>(true);
        if (selector != null && selector.IsOptionValueText(tmp))
            return true;

        string label = NormalizeLabel(tmp.text);

        if (_languageDisplayNames.Contains(label))
            return true;

        if (label is "<" or ">" or "—" or "-")
            return true;

        if (label.StartsWith("Option ", StringComparison.OrdinalIgnoreCase))
            return true;

        if (label is "Enabled" or "Disabled" or "Fullscreen" or "Windowed" or "Exclusive Fullscreen" or "High")
            return true;

        if (label.Length > 0 && char.IsDigit(label[0]) && label.Contains('x'))
            return true;

        return false;
    }

    private static string NormalizeLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Trim();
        int newline = text.IndexOf('\n');
        if (newline >= 0)
            text = text.Substring(0, newline).Trim();

        return text;
    }

    /// <summary>Refresh every localizer in loaded scenes (main menu + in-game pause UI).</summary>
    public static void RefreshAllInstances()
    {
        var localizers = UnityEngine.Object.FindObjectsByType<MainMenuTextLocalizer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var localizer in localizers)
            localizer.RefreshLocalizedText();
    }

    /// <summary>Attach localizer to the Canvas that owns <paramref name="uiRoot"/>.</summary>
    public static MainMenuTextLocalizer EnsureOnCanvas(Transform uiRoot)
    {
        if (uiRoot == null)
            return null;

        var canvas = uiRoot.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[MainMenuTextLocalizer] No Canvas parent for " + uiRoot.name);
            return null;
        }

        if (!canvas.TryGetComponent(out MainMenuTextLocalizer localizer))
            localizer = canvas.gameObject.AddComponent<MainMenuTextLocalizer>();

        return localizer;
    }

    /// <inheritdoc cref="EnsureOnCanvas(Transform)"/>
    public static MainMenuTextLocalizer EnsureOnCanvas(Component anyUiInCanvas) =>
        anyUiInCanvas != null ? EnsureOnCanvas(anyUiInCanvas.transform) : null;
}
