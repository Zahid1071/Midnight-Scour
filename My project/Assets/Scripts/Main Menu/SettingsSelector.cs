using System;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// UI-only selector: shows a value with left/right arrows.
/// Does NOT apply or save settings — SettingsMenu handles that.
/// </summary>
public class SettingsSelector : MonoBehaviour
{
    public enum SettingType
    {
        Resolution,
        Quality,
        Fullscreen,
        Custom,
        Language
    }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI optionText;

    [Header("Setting Configuration")]
    [SerializeField] private SettingType settingType = SettingType.Custom;

    [Tooltip("Only used when Setting Type = Custom or Fullscreen.")]
    [SerializeField] private string[] options = { "Option 1", "Option 2", "Option 3" };

    private int currentIndex;
    private Resolution[] resolutions;

    /// <summary>Fired after the user changes the index (arrows). Does not apply settings.</summary>
    public event Action<int> OnIndexChanged;

    private void Awake()
    {
        InitializeOptions();
        ClampIndex();
        RefreshOptionText();
    }

    /// <summary>Configures this selector for the project's supported UI languages.</summary>
    public void ConfigureAsLanguageSelector()
    {
        settingType = SettingType.Language;
        InitializeOptions();
        ClampIndex();
        RefreshOptionText();
    }

    public void NextOption()
    {
        currentIndex++;
        if (currentIndex >= GetOptionCount())
            currentIndex = 0;
        ClampIndex();
        RefreshOptionText();
        NotifyIndexChanged();
        Debug.Log($"[SettingsSelector] {settingType} NextOption -> index: {currentIndex} ({GetCurrentOptionString()})");
    }

    public void PreviousOption()
    {
        currentIndex--;
        if (currentIndex < 0)
            currentIndex = GetOptionCount() - 1;
        ClampIndex();
        RefreshOptionText();
        NotifyIndexChanged();
        Debug.Log($"[SettingsSelector] {settingType} PreviousOption -> index: {currentIndex} ({GetCurrentOptionString()})");
    }

    public void RefreshOptionText()
    {
        if (optionText == null) return;
        optionText.text = GetCurrentOptionString();
    }

    public void SetIndex(int index)
    {
        currentIndex = Mathf.Clamp(index, 0, Mathf.Max(0, GetOptionCount() - 1));
        RefreshOptionText();
    }

    public int GetCurrentIndex() => currentIndex;

    /// <summary>True if this TMP is the selector's dynamic value (resolution, language name, etc.).</summary>
    public bool IsOptionValueText(TextMeshProUGUI tmp) => optionText != null && optionText == tmp;

    public string GetCurrentOptionString()
    {
        int count = GetOptionCount();
        if (count == 0) return "—";

        switch (settingType)
        {
            case SettingType.Resolution:
                if (resolutions != null && currentIndex >= 0 && currentIndex < resolutions.Length)
                    return $"{resolutions[currentIndex].width}x{resolutions[currentIndex].height}";
                break;

            case SettingType.Quality:
                if (currentIndex >= 0 && currentIndex < QualitySettings.names.Length)
                    return QualitySettings.names[currentIndex];
                break;

            case SettingType.Fullscreen:
            case SettingType.Custom:
            case SettingType.Language:
                if (options != null && currentIndex >= 0 && currentIndex < options.Length)
                    return options[currentIndex];
                break;
        }

        return "—";
    }

    // ---------- Internal ----------

    private void InitializeOptions()
    {
        switch (settingType)
        {
            case SettingType.Resolution:
                resolutions = Screen.resolutions
                    .Where(r => r.refreshRateRatio.value >= 59.5f)
                    .OrderBy(r => r.width)
                    .ThenBy(r => r.height)
                    .GroupBy(r => new { r.width, r.height })
                    .Select(g => g.First())
                    .ToArray();
                break;

            case SettingType.Quality:
                break;

            case SettingType.Fullscreen:
                if (options == null || options.Length == 0)
                    options = new[] { "Fullscreen", "Windowed", "Exclusive Fullscreen" };
                break;

            case SettingType.Custom:
                if (options == null || options.Length == 0)
                    options = new[] { "Option 1" };
                break;

            case SettingType.Language:
                options = LocalizationTables.LanguageDisplayNames;
                break;
        }
    }

    private int GetOptionCount()
    {
        switch (settingType)
        {
            case SettingType.Resolution:
                return resolutions != null ? resolutions.Length : 0;
            case SettingType.Quality:
                return QualitySettings.names.Length;
            case SettingType.Fullscreen:
            case SettingType.Custom:
            case SettingType.Language:
                return options != null ? options.Length : 0;
            default:
                return 0;
        }
    }

    private void NotifyIndexChanged() => OnIndexChanged?.Invoke(currentIndex);

    private void ClampIndex()
    {
        int count = GetOptionCount();
        if (count > 0)
            currentIndex = Mathf.Clamp(currentIndex, 0, count - 1);
        else
            currentIndex = 0;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (optionText == null && settingType != SettingType.Custom)
        {
            var tmp = GetComponentInChildren<TextMeshProUGUI>();
            if (tmp != null) optionText = tmp;
        }
    }
#endif
}
