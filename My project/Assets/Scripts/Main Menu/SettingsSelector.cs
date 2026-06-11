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
        Custom
    }

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI optionText;

    [Header("Setting Configuration")]
    [SerializeField] private SettingType settingType = SettingType.Custom;

    [Tooltip("Only used when Setting Type = Custom or Fullscreen.")]
    [SerializeField] private string[] options = { "Option 1", "Option 2", "Option 3" };

    private int currentIndex;

    private void Awake()
    {
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
        Debug.Log($"[SettingsSelector] {settingType} NextOption -> index: {currentIndex} ({GetCurrentOptionString()})");
    }

    public void PreviousOption()
    {
        currentIndex--;
        if (currentIndex < 0)
            currentIndex = GetOptionCount() - 1;
        ClampIndex();
        RefreshOptionText();
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

    public string GetCurrentOptionString()
    {
        int count = GetOptionCount();
        if (count == 0) return "—";

        switch (settingType)
        {
            case SettingType.Resolution:
                var resolutions = GetResolutions();
                if (resolutions != null && currentIndex >= 0 && currentIndex < resolutions.Length)
                    return $"{resolutions[currentIndex].width}x{resolutions[currentIndex].height}";
                break;

            case SettingType.Quality:
                if (currentIndex >= 0 && currentIndex < QualitySettings.names.Length)
                    return QualitySettings.names[currentIndex];
                break;

            case SettingType.Fullscreen:
            case SettingType.Custom:
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
        }
    }

    private int GetOptionCount()
    {
        switch (settingType)
        {
            case SettingType.Resolution:
                var resolutions = GetResolutions();
                return resolutions != null ? resolutions.Length : 0;
            case SettingType.Quality:
                return QualitySettings.names.Length;
            case SettingType.Fullscreen:
            case SettingType.Custom:
                return options != null ? options.Length : 0;
            default:
                return 0;
        }
    }

    private void ClampIndex()
    {
        int count = GetOptionCount();
        if (count > 0)
            currentIndex = Mathf.Clamp(currentIndex, 0, count - 1);
        else
            currentIndex = 0;
    }

    Resolution[] GetResolutions()
    {
        if (GameSettingsManager.Instance != null && GameSettingsManager.Instance.AvailableResolutions != null)
            return GameSettingsManager.Instance.AvailableResolutions;

        return GameSettingsManager.BuildAvailableResolutions();
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
