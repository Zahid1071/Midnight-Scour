using UnityEngine;

/// <summary>
/// Optional helper on the language settings row. Uses <see cref="SettingsSelector"/>
/// (Setting Type = Language, or call <see cref="Configure"/> from code).
/// </summary>
public class LanguageSettingsUI : MonoBehaviour
{
    [SerializeField] private SettingsSelector languageSelector;

    public void Configure()
    {
        if (languageSelector == null)
            languageSelector = GetComponent<SettingsSelector>();

        if (languageSelector == null)
        {
            Debug.LogWarning("[LanguageSettingsUI] No SettingsSelector assigned.");
            return;
        }

        languageSelector.ConfigureAsLanguageSelector();

        int index = GameSettingsManager.Instance != null
            ? GameSettingsManager.Instance.LanguageIndex
            : LocalizationService.GetLocaleIndex(LocalizationService.CurrentLocaleCode);

        languageSelector.SetIndex(index);
    }

    public int GetSelectedIndex() =>
        languageSelector != null ? languageSelector.GetCurrentIndex() : 0;
}
