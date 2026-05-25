using UnityEngine;

/// <summary>
/// Applies saved language as soon as localization is initialized (before scene UI).
/// </summary>
public static class LocalizationBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ApplySavedLanguage()
    {
        string code = PlayerPrefs.GetString(GameSettingsManager.LanguagePrefsKey, "en");
        LocalizationService.EnsureInitialized(() =>
        {
            LocalizationService.TrySetLocale(code);
        });
    }
}
